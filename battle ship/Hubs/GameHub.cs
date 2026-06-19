using System.Security.Claims;
using battle_ship.Game;
using battle_ship.Services;
using Battleship.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace battle_ship.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly MatchmakingService _matchmaking;
    private readonly GameService _gameService;

    public GameHub(MatchmakingService matchmaking, GameService gameService)
    {
        _matchmaking = matchmaking;
        _gameService = gameService;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _matchmaking.DequeueByConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinQueue()
    {
        Console.WriteLine("USER: " + Context.User?.Identity?.Name);

        try
        {
            var user = GetCurrentUser();

            var existing = _gameService.GetSessionByUserId(user.UserId);
            if (existing is not null)
            {
                _gameService.UpdateConnection(existing, user.UserId, Context.ConnectionId);
                await Groups.AddToGroupAsync(Context.ConnectionId, existing.GameId.ToString());
                await Clients.Caller.SendAsync("OnMatchFound",
                    new MatchFoundDto(existing.GameId, existing.GetOpponentUserId(user.UserId),
                        existing.GetUsername(existing.GetOpponentUserId(user.UserId))));
                await Clients.Caller.SendAsync("OnGameState", existing.ToStateDto(user.UserId));
                return;
            }

            _matchmaking.Enqueue(new QueuedPlayer(user.UserId, user.Username, Context.ConnectionId));
            await Clients.Caller.SendAsync("OnQueueJoined");

            var (p1, p2) = _matchmaking.TryMatch();
            if (p1 is null || p2 is null)
                return;

            var session = _gameService.CreateSession(p1, p2);
            var groupName = session.GameId.ToString();

            await Groups.AddToGroupAsync(p1.ConnectionId, groupName);
            await Groups.AddToGroupAsync(p2.ConnectionId, groupName);

            await Clients.Client(p1.ConnectionId).SendAsync("OnMatchFound",
                new MatchFoundDto(session.GameId, p2.UserId, p2.Username));
            await Clients.Client(p2.ConnectionId).SendAsync("OnMatchFound",
                new MatchFoundDto(session.GameId, p1.UserId, p1.Username));

            await Clients.Client(p1.ConnectionId).SendAsync("OnGameState", session.ToStateDto(p1.UserId));
            await Clients.Client(p2.ConnectionId).SendAsync("OnGameState", session.ToStateDto(p2.UserId));

        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("OnError", ex.ToString());
            
        }
    }

    public async Task LeaveQueue()
    {
        _matchmaking.DequeueByConnection(Context.ConnectionId);
        await Clients.Caller.SendAsync("OnQueueLeft");
    }

    public async Task PlaceShips(Guid gameId, ShipDto[] ships)
    {
        var user = GetCurrentUser();
        var session = _gameService.GetSession(gameId);

        if (session is null || !session.IsPlayerInGame(user.UserId))
        {
            await Clients.Caller.SendAsync("OnError", "Игра не найдена.");
            return;
        }

        var result = _gameService.PlaceShips(session, user.UserId, ships);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        await Clients.Caller.SendAsync("OnPlacementConfirmed");
        await Clients.Caller.SendAsync("OnGameState", session.ToStateDto(user.UserId));

        var opponentId = session.GetOpponentUserId(user.UserId);
        await Clients.Client(session.GetConnectionId(opponentId))
            .SendAsync("OnOpponentReady");

        if (session.Status == Battleship.Shared.Enums.GameStatus.InProgress)
        {
            await Clients.Group(gameId.ToString()).SendAsync("OnGameStarted", session.CurrentTurnUserId);
            await NotifyGameState(session);
        }
    }

    public async Task PlaceRandomShips(Guid gameId)
    {
        var ships = Game.GameSession.GenerateRandomPlacement();
        await PlaceShips(gameId, ships);
    }

    public async Task Shoot(Guid gameId, int x, int y)
    {
        var user = GetCurrentUser();
        var session = _gameService.GetSession(gameId);

        if (session is null || !session.IsPlayerInGame(user.UserId))
        {
            await Clients.Caller.SendAsync("OnError", "Игра не найдена.");
            return;
        }

        var result = _gameService.Shoot(session, user.UserId, x, y);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        await Clients.Group(gameId.ToString()).SendAsync("OnShotResult", user.UserId, result.Result);

        if (session.Status == Battleship.Shared.Enums.GameStatus.Finished)
        {
            await Clients.Group(gameId.ToString()).SendAsync("OnGameOver", session.WinnerUserId);
            _gameService.RemoveSession(gameId);
        }
        else
        {
            await Clients.Group(gameId.ToString()).SendAsync("OnTurnChanged", session.CurrentTurnUserId);
        }

        await NotifyGameState(session);
    }

    public async Task Surrender(Guid gameId)
    {
        var user = GetCurrentUser();
        var session = _gameService.GetSession(gameId);

        if (session is null || !session.IsPlayerInGame(user.UserId))
            return;

        session.Status = Battleship.Shared.Enums.GameStatus.Finished;
        session.WinnerUserId = session.GetOpponentUserId(user.UserId);

        await Clients.Group(gameId.ToString()).SendAsync("OnGameOver", session.WinnerUserId);
        _gameService.RemoveSession(gameId);
    }

    public async Task GetGameState(Guid gameId)
    {
        var user = GetCurrentUser();
        var session = _gameService.GetSession(gameId);

        if (session is null || !session.IsPlayerInGame(user.UserId))
            return;

        await Clients.Caller.SendAsync("OnGameState", session.ToStateDto(user.UserId));
    }

    private async Task NotifyGameState(GameSession session)
    {
        await Clients.Client(session.Player1ConnectionId)
            .SendAsync("OnGameState", session.ToStateDto(session.Player1UserId));
        await Clients.Client(session.Player2ConnectionId)
            .SendAsync("OnGameState", session.ToStateDto(session.Player2UserId));
    }

    private (int UserId, string Username) GetCurrentUser()
    {
        var idClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new HubException("Не авторизован.");
        var name = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Player";
        return (int.Parse(idClaim), name);
    }
}
