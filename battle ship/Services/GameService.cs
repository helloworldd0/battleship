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

    public GameSession CreateSession(QueuedPlayer p1, QueuedPlayer p2)
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
        _ = PersistNewGameAsync(session);
        return session;
    }

    public GameSession? GetSession(Guid gameId) =>
        _sessions.TryGetValue(gameId, out var session) ? session : null;

    public GameSession? GetSessionByUserId(int userId) =>
        _sessions.Values.FirstOrDefault(s => s.IsPlayerInGame(userId));

    public (bool Success, string? Error) PlaceShips(GameSession session, int userId, ShipDto[] ships)
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

        _ = PersistBoardAsync(session, userId, ships);

        if (session.Player1Ready && session.Player2Ready)
        {
            session.Status = GameStatus.InProgress;
            session.CurrentTurnUserId = session.Player1UserId;
            _ = UpdateGameStatusAsync(session);
        }

        return (true, null);
    }

    public (bool Success, ShotResultDto? Result, string? Error) Shoot(GameSession session, int userId, int x, int y)
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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var game = new Models.Game
        {
            Id = session.GameId,
            Status = GameStatus.WaitingPlacement
        };

        db.Games.Add(game);
        db.GamePlayers.AddRange(
            new GamePlayer { GameId = session.GameId, UserId = session.Player1UserId },
            new GamePlayer { GameId = session.GameId, UserId = session.Player2UserId }
        );

        await db.SaveChangesAsync();
    }

    private async Task PersistBoardAsync(GameSession session, int userId, ShipDto[] ships)
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

    private async Task UpdateGameStatusAsync(GameSession session)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var game = await db.Games.FindAsync(session.GameId);
        if (game is null) return;

        game.Status = session.Status;
        game.CurrentTurnUserId = session.CurrentTurnUserId;
        await db.SaveChangesAsync();
    }

    private async Task FinishGameAsync(GameSession session)
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
}
