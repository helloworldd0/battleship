using System.Collections.Concurrent;
using System.Text.Json;
using battle_ship.Data;
using battle_ship.Game;
using battle_ship.Models;
using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace battle_ship.Services;

public class GameService
{
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public GameService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RestoreSessionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeGames = await db.Games
            .Where(g => g.Status != GameStatus.Finished)
            .Include(g => g.Players)
            .ThenInclude(p => p.User)
            .ToListAsync();

        foreach (var game in activeGames)
        {
            if (game.Players.Count != 2)
                continue;

            var players = game.Players.OrderBy(p => p.Id).ToList();
            var p1 = players[0];
            var p2 = players[1];


            var session = new GameSession
            {
                GameId = game.Id,
                Player1UserId = p1.UserId,
                Player2UserId = p2.UserId,
                Player1Username = p1.User.Username,
                Player2Username = p2.User.Username,
                Status = game.Status,
                CurrentTurnUserId = game.CurrentTurnUserId,
                WinnerUserId = game.WinnerUserId,
                Player1Ready = p1.IsReady,
                Player2Ready = p2.IsReady
            };

            var p1Ships = GameSession.DeserializeShips(p1.BoardJson);
            var p2Ships = GameSession.DeserializeShips(p2.BoardJson);

            if (p1Ships.Length > 0)
                session.Player1Board.PlaceShips(p1Ships);

            if (p2Ships.Length > 0)
                session.Player2Board.PlaceShips(p2Ships);

            var p1Shots = JsonSerializer.Deserialize<List<CoordinateDto>>(p1.ShotsJson) ?? [];
            var p2Shots = JsonSerializer.Deserialize<List<CoordinateDto>>(p2.ShotsJson) ?? [];

            foreach (var s in p1Shots)
                session.Player2Board.Shoot(s.X, s.Y);

            foreach (var s in p2Shots)
                session.Player1Board.Shoot(s.X, s.Y);

            _sessions[session.GameId] = session;
        }
    }

    public GameSession? GetOrRestoreSessionByUserId(int userId)
    {
        var session = GetSessionByUserId(userId);
        if (session != null)
            return session;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var game = db.Games
            .Include(g => g.Players)
            .ThenInclude(p => p.User)
            .FirstOrDefault(g => g.Status != GameStatus.Finished &&
                                 g.Players.Any(p => p.UserId == userId));

        if (game is null || game.Players.Count != 2)
            return null;

        var players = game.Players.OrderBy(p => p.Id).ToList();
        var p1 = players[0];
        var p2 = players[1];


        var restored = new GameSession
        {
            GameId = game.Id,
            Player1UserId = p1.UserId,
            Player2UserId = p2.UserId,
            Player1Username = p1.User.Username,
            Player2Username = p2.User.Username,
            Status = game.Status,
            CurrentTurnUserId = game.CurrentTurnUserId,
            WinnerUserId = game.WinnerUserId,
            Player1Ready = p1.IsReady,
            Player2Ready = p2.IsReady
        };

        var p1Ships = GameSession.DeserializeShips(p1.BoardJson);
        var p2Ships = GameSession.DeserializeShips(p2.BoardJson);

        if (p1Ships.Length > 0)
            restored.Player1Board.PlaceShips(p1Ships);

        if (p2Ships.Length > 0)
            restored.Player2Board.PlaceShips(p2Ships);

        var p1Shots = JsonSerializer.Deserialize<List<CoordinateDto>>(p1.ShotsJson) ?? [];
        var p2Shots = JsonSerializer.Deserialize<List<CoordinateDto>>(p2.ShotsJson) ?? [];

        foreach (var s in p1Shots)
            restored.Player2Board.Shoot(s.X, s.Y);

        foreach (var s in p2Shots)
            restored.Player1Board.Shoot(s.X, s.Y);

        _sessions[restored.GameId] = restored;
        return restored;
    }

    public async Task<GameSession> CreateSession(QueuedPlayer p1, QueuedPlayer p2)
    {
        var gameId = Guid.NewGuid();
        var session = new GameSession
        {
            GameId = gameId,
            Player1UserId = p1.UserId,
            Player2UserId = p2.UserId,
            Player1ConnectionId = p1.ConnectionId,
            Player2ConnectionId = p2.ConnectionId,
            Player1Username = p1.Username,
            Player2Username = p2.Username
        };

        _sessions[gameId] = session;
        await PersistNewGameAsync(session);
        return session;
    }

    public GameSession? GetSession(Guid gameId) =>
        _sessions.TryGetValue(gameId, out var session) ? session : null;

    public GameSession? GetSessionByUserId(int userId) =>
        _sessions.Values.FirstOrDefault(s => s.IsPlayerInGame(userId));

    public async Task<(bool Success, string? Error)> PlaceShips(GameSession session, int userId, ShipDto[] ships)
    {
        if (session.Status != GameStatus.WaitingPlacement)
            return (false, "Расстановка уже завершена.");

        var board = session.GetOwnBoard(userId);
        var result = board.PlaceShips(ships);
        if (!result.Success)
            return result;

        if (userId == session.Player1UserId)
            session.Player1Ready = true;
        else
            session.Player2Ready = true;

        await PersistBoardAsync(session, userId, ships);


        if (session.Player1Ready && session.Player2Ready)
        {
            session.Status = GameStatus.InProgress;
            session.CurrentTurnUserId = session.Player1UserId;
            _ = UpdateGameStatusAsync(session);
        }

        return (true, null);
    }

    public async Task<(bool Success, ShotResultDto? Result, string? Error)> Shoot(GameSession session, int userId, int x, int y)
    {
        if (session.Status != GameStatus.InProgress)
            return (false, null, "Игра ещё не началась или уже завершена.");

        if (session.CurrentTurnUserId != userId)
            return (false, null, "Сейчас не ваш ход.");

        var enemyBoard = session.GetEnemyBoard(userId);
        var (resultType, sunkShip) = enemyBoard.Shoot(x, y);

        if (resultType is ShotResultType.Invalid or ShotResultType.AlreadyShot)
            return (false, null, resultType == ShotResultType.AlreadyShot
                ? "Вы уже стреляли в эту клетку."
                : "Некорректные координаты.");

        var dto = new ShotResultDto(x, y, resultType, sunkShip?.ToDto());

        await PersistShotAsync(session, userId, x, y);

        if (enemyBoard.AllShipsSunk)
        {
            session.Status = GameStatus.Finished;
            session.WinnerUserId = userId;
            _ = FinishGameAsync(session);
        }
        else if (resultType == ShotResultType.Miss)
        {
            session.CurrentTurnUserId = session.GetOpponentUserId(userId);
        }

        return (true, dto, null);
    }

    public void UpdateConnection(GameSession session, int userId, string connectionId)
    {
        if (userId == session.Player1UserId)
            session.Player1ConnectionId = connectionId;
        else
            session.Player2ConnectionId = connectionId;
    }

    public void RemoveSession(Guid gameId) => _sessions.TryRemove(gameId, out _);

    private async Task PersistNewGameAsync(GameSession session)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var game = new Models.Game
            {
                Id = session.GameId,
                Status = GameStatus.WaitingPlacement
            };

            db.Games.Add(game);
            if (session.Player1UserId != GameSession.BotUserId)
                db.GamePlayers.Add(new GamePlayer { GameId = session.GameId, UserId = session.Player1UserId });

            if (session.Player2UserId != GameSession.BotUserId)
                db.GamePlayers.Add(new GamePlayer { GameId = session.GameId, UserId = session.Player2UserId });


            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] PersistNewGameAsync: {ex}");
            throw;
        }
    }

    private async Task PersistBoardAsync(GameSession session, int userId, ShipDto[] ships)
    {
        if (userId == GameSession.BotUserId) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var player = await db.GamePlayers
                .FirstOrDefaultAsync(gp => gp.GameId == session.GameId && gp.UserId == userId);

            if (player is null) return;

            player.BoardJson = GameSession.SerializeShips(ships);
            player.IsReady = true;

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] PersistBoardAsync: {ex}");
            throw;
        }
    }


    private async Task PersistShotAsync(GameSession session, int userId, int x, int y)
    {
        if (userId == GameSession.BotUserId) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var player = await db.GamePlayers
                .FirstOrDefaultAsync(gp => gp.GameId == session.GameId && gp.UserId == userId);

            if (player is null) return;

            var shots = JsonSerializer.Deserialize<List<CoordinateDto>>(player.ShotsJson) ?? [];
            shots.Add(new CoordinateDto(x, y));
            player.ShotsJson = JsonSerializer.Serialize(shots);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] PersistShotAsync: {ex}");
            throw;
        }
    }

    private async Task UpdateGameStatusAsync(GameSession session)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var game = await db.Games.FindAsync(session.GameId);
            if (game is null) return;

            game.Status = session.Status;
            game.CurrentTurnUserId = session.CurrentTurnUserId;

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] UpdateGameStatusAsync: {ex}");
            throw;
        }
    }

    public async Task FinishGameAsync(GameSession session)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var game = await db.Games.FindAsync(session.GameId);
            if (game is null) return;

            game.Status = GameStatus.Finished;
            game.WinnerUserId = session.WinnerUserId;
            game.FinishedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] FinishGameAsync: {ex}");
            throw;
        }
    }
}
