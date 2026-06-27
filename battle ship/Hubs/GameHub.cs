using battle_ship.Game;
using battle_ship.Services;
using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace battle_ship.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly MatchmakingService _matchmaking;
    private readonly GameService _gameService;
    private static readonly ConcurrentDictionary<Guid, HashSet<int>> _rematchVotes = new();
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> _placementTimers = new();
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> _turnTimers = new();

    private readonly IHubContext<GameHub> _hubContext;

    public GameHub(MatchmakingService matchmaking, GameService gameService, IHubContext<GameHub> hubContext)
    {
        _matchmaking = matchmaking;
        _gameService = gameService;
        _hubContext = hubContext;
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

            var existing = _gameService.GetOrRestoreSessionByUserId(user.UserId);

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

            var session = await _gameService.CreateSession(p1, p2);
            var groupName = session.GameId.ToString();

            await Groups.AddToGroupAsync(p1.ConnectionId, groupName);
            await Groups.AddToGroupAsync(p2.ConnectionId, groupName);

            await Clients.Client(p1.ConnectionId).SendAsync("OnMatchFound",
                new MatchFoundDto(session.GameId, p2.UserId, p2.Username));
            await Clients.Client(p2.ConnectionId).SendAsync("OnMatchFound",
                new MatchFoundDto(session.GameId, p1.UserId, p1.Username));

            StartPlacementTimer(session);

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

    public async Task JoinBotGame()
    {
        try
        {
            var user = GetCurrentUser();

            var botPlayer = new QueuedPlayer(GameSession.BotUserId, "Бот", "bot");
            var humanPlayer = new QueuedPlayer(user.UserId, user.Username, Context.ConnectionId);

            var session = await _gameService.CreateSession(humanPlayer, botPlayer);
            session.Bot = new BotPlayer();

            var groupName = session.GameId.ToString();
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            await Clients.Caller.SendAsync("OnMatchFound",
                new MatchFoundDto(session.GameId, GameSession.BotUserId, "Бот"));

            var botShips = Battleship.Shared.Utils.ShipPlacer.GenerateRandomPlacement();
            await _gameService.PlaceShips(session, GameSession.BotUserId, botShips);

            StartPlacementTimer(session);

            await Clients.Caller.SendAsync("OnGameState", session.ToStateDto(user.UserId));
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("OnError", ex.ToString());
        }
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

        var result = await _gameService.PlaceShips(session, user.UserId, ships);
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
            if (_placementTimers.TryRemove(session.GameId, out var cts))
                cts.Cancel();

            await Clients.Group(gameId.ToString()).SendAsync("OnGameStarted", session.CurrentTurnUserId);
            await NotifyGameState(session);
            StartTurnTimer(session);

            if (session.IsVsBot && session.CurrentTurnUserId == GameSession.BotUserId)
            {
                await Task.Delay(800);
                await BotShootAsync(session);
            }
        }
    }

    public async Task PlaceRandomShips(Guid gameId)
    {
        var ships = Battleship.Shared.Utils.ShipPlacer.GenerateRandomPlacement();
        await PlaceShips(gameId, ships);
    }

    private void StartPlacementTimer(GameSession session)
    {
        var cts = new CancellationTokenSource();
        _placementTimers[session.GameId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(GameConstants.PlacementTimeSeconds),
                    cts.Token);

                var loserId = !session.Player1Ready
                    ? session.Player1UserId
                    : session.Player2UserId;

                session.Status = Battleship.Shared.Enums.GameStatus.Finished;
                session.WinnerUserId = session.GetOpponentUserId(loserId);

                await _hubContext.Clients.Group(session.GameId.ToString())
                    .SendAsync("OnPlacementTimeout", loserId);
                await _hubContext.Clients.Group(session.GameId.ToString())
                    .SendAsync("OnGameOver", session.WinnerUserId);

                await _gameService.FinishGameAsync(session);
                _gameService.RemoveSession(session.GameId);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[TIMER ERROR] StartPlacementTimer: {ex}");
            }
        });
    }

    private void StartTurnTimer(GameSession session)
    {
        if (_turnTimers.TryRemove(session.GameId, out var old))
            old.Cancel();

        var cts = new CancellationTokenSource();
        _turnTimers[session.GameId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(GameConstants.TurnTimeSeconds),
                    cts.Token);

                var loserId = session.CurrentTurnUserId;
                if (loserId is null) return;

                session.Status = Battleship.Shared.Enums.GameStatus.Finished;
                session.WinnerUserId = session.GetOpponentUserId(loserId.Value);

                await _gameService.FinishGameAsync(session);

                await _hubContext.Clients.Group(session.GameId.ToString())
                    .SendAsync("OnTurnTimeout", loserId);
                await _hubContext.Clients.Group(session.GameId.ToString())
                    .SendAsync("OnGameOver", session.WinnerUserId);

                _gameService.RemoveSession(session.GameId);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[TIMER ERROR] StartTurnTimer: {ex}");
            }
        });
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

        var result = await _gameService.Shoot(session, user.UserId, x, y);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("OnError", result.Error);
            return;
        }

        await Clients.Group(gameId.ToString()).SendAsync("OnShotResult", user.UserId, result.Result);

        if (session.Status == Battleship.Shared.Enums.GameStatus.Finished)
        {
            if (_turnTimers.TryRemove(session.GameId, out var cts))
                cts.Cancel();
            await Clients.Group(gameId.ToString()).SendAsync("OnGameOver", session.WinnerUserId);
            await NotifyGameState(session);
        }
        else
        {
            await Clients.Group(gameId.ToString()).SendAsync("OnTurnChanged", session.CurrentTurnUserId);
            StartTurnTimer(session);
            await NotifyGameState(session);
            if (session.IsVsBot && session.CurrentTurnUserId == GameSession.BotUserId)
                _ = Task.Run(async () => await BotShootAsync(session));

        }
    }

    private async Task BotShootAsync(GameSession session)
    {
        while (session.Status == Battleship.Shared.Enums.GameStatus.InProgress &&
               session.CurrentTurnUserId == GameSession.BotUserId)
        {
            await Task.Delay(3000);
            var (x, y) = session.Bot!.GetNextShot();
            var result = await _gameService.Shoot(session, GameSession.BotUserId, x, y);

            if (!result.Success) break;

            var isHit = result.Result!.Result == Battleship.Shared.Enums.ShotResultType.Hit ||
                        result.Result.Result == Battleship.Shared.Enums.ShotResultType.Sunk;
            var isSunk = result.Result.Result == Battleship.Shared.Enums.ShotResultType.Sunk;

            session.Bot.RegisterShot(x, y, isHit, isSunk);

            await _hubContext.Clients.Group(session.GameId.ToString())
                .SendAsync("OnShotResult", GameSession.BotUserId, result.Result);

            if (session.Status == Battleship.Shared.Enums.GameStatus.Finished)
            {
                if (_turnTimers.TryRemove(session.GameId, out var cts))
                    cts.Cancel();
                await _hubContext.Clients.Group(session.GameId.ToString())
                    .SendAsync("OnGameOver", session.WinnerUserId);
                _gameService.RemoveSession(session.GameId);
                return;
            }

            await _hubContext.Clients.Group(session.GameId.ToString())
                .SendAsync("OnTurnChanged", session.CurrentTurnUserId);
            await NotifyGameState(session);

            if (isHit)
                await Task.Delay(1200);
            else
                break; 
        }
    }

    public async Task Surrender(Guid gameId)
    {
        var user = GetCurrentUser();
        var session = _gameService.GetSession(gameId);

        if (session is null || !session.IsPlayerInGame(user.UserId))
            return;

        session.Status = Battleship.Shared.Enums.GameStatus.Finished;
        session.WinnerUserId = session.GetOpponentUserId(user.UserId);

        if (_turnTimers.TryRemove(session.GameId, out var cts))
            cts.Cancel();

        await _gameService.FinishGameAsync(session);
        await Clients.Group(gameId.ToString()).SendAsync("OnGameOver", session.WinnerUserId);
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
        if (!string.IsNullOrEmpty(session.Player1ConnectionId))
            await _hubContext.Clients.Client(session.Player1ConnectionId)
                .SendAsync("OnGameState", session.ToStateDto(session.Player1UserId));

        if (!string.IsNullOrEmpty(session.Player2ConnectionId) &&
            session.Player2UserId != GameSession.BotUserId)
            await _hubContext.Clients.Client(session.Player2ConnectionId)
                .SendAsync("OnGameState", session.ToStateDto(session.Player2UserId));
    }

    private (int UserId, string Username) GetCurrentUser()
    {
        var idClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new HubException("Не авторизован.");
        var name = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Player";
        return (int.Parse(idClaim), name);
    }

    public async Task RequestRematch(Guid gameId)
    {
        var user = GetCurrentUser();
        var session = _gameService.GetSession(gameId);
        if (session is null) return;

        if (session.IsVsBot)
        {
            _gameService.RemoveSession(gameId);

            var humanPlayer = new QueuedPlayer(user.UserId, user.Username, Context.ConnectionId);
            var botPlayer = new QueuedPlayer(GameSession.BotUserId, "Бот", "bot");

            var newSession = await _gameService.CreateSession(humanPlayer, botPlayer);
            newSession.Bot = new BotPlayer();

            await Groups.AddToGroupAsync(Context.ConnectionId, newSession.GameId.ToString());

            await Clients.Caller.SendAsync("OnMatchFound",
                new MatchFoundDto(newSession.GameId, GameSession.BotUserId, "Бот"));

            var botShips = Battleship.Shared.Utils.ShipPlacer.GenerateRandomPlacement();
            await _gameService.PlaceShips(newSession, GameSession.BotUserId, botShips);

            StartPlacementTimer(newSession);

            await Clients.Caller.SendAsync("OnGameState", newSession.ToStateDto(user.UserId));
            return;
        }


        var votes = _rematchVotes.GetOrAdd(gameId, _ => new HashSet<int>());
        bool bothReady;
        lock (votes)
        {
            votes.Add(user.UserId);
            bothReady = votes.Count >= 2;
        }

        if (bothReady)
        {
            _rematchVotes.TryRemove(gameId, out _);
            _gameService.RemoveSession(gameId);

            var p1 = new QueuedPlayer(session.Player1UserId, session.Player1Username,
                session.Player1ConnectionId);
            var p2 = new QueuedPlayer(session.Player2UserId, session.Player2Username,
                session.Player2ConnectionId);

            var newSession = await _gameService.CreateSession(p1, p2);
            var groupName = newSession.GameId.ToString();

            await Groups.AddToGroupAsync(p1.ConnectionId, groupName);
            await Groups.AddToGroupAsync(p2.ConnectionId, groupName);

            await Clients.Client(p1.ConnectionId).SendAsync("OnMatchFound",
                new MatchFoundDto(newSession.GameId, p2.UserId, p2.Username));
            await Clients.Client(p2.ConnectionId).SendAsync("OnMatchFound",
                new MatchFoundDto(newSession.GameId, p1.UserId, p1.Username));

            await Clients.Client(p1.ConnectionId).SendAsync("OnGameState",
                newSession.ToStateDto(p1.UserId));
            await Clients.Client(p2.ConnectionId).SendAsync("OnGameState",
                newSession.ToStateDto(p2.UserId));
        }
        else
        {
            await Clients.Group(gameId.ToString()).SendAsync("OnRematchRequested", user.UserId);
        }
    }

    public async Task DeclineRematch(Guid gameId)
    {
        var user = GetCurrentUser();
        _rematchVotes.TryRemove(gameId, out _);
        _gameService.RemoveSession(gameId);
        await Clients.Group(gameId.ToString()).SendAsync("OnRematchDeclined", user.UserId);
    }
}
