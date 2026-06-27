using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;
using System.Text.Json;

namespace battle_ship.Game;

public class GameSession
{
    public Guid GameId { get; init; }
    public int Player1UserId { get; init; }
    public int Player2UserId { get; init; }
    public string Player1ConnectionId { get; set; } = string.Empty;
    public string Player2ConnectionId { get; set; } = string.Empty;
    public string Player1Username { get; init; } = string.Empty;
    public string Player2Username { get; init; } = string.Empty;

    public Board Player1Board { get; } = new();
    public Board Player2Board { get; } = new();

    public bool Player1Ready { get; set; }
    public bool Player2Ready { get; set; }

    public GameStatus Status { get; set; } = GameStatus.WaitingPlacement;
    public int? CurrentTurnUserId { get; set; }
    public int? WinnerUserId { get; set; }

    public CancellationTokenSource? PlacementTimer { get; set; }
    public CancellationTokenSource? TurnTimer { get; set; }

    public BotPlayer? Bot { get; set; }
    public bool IsVsBot => Bot is not null;

    public const int BotUserId = -1;

    public int GetOpponentUserId(int userId) =>
        userId == Player1UserId ? Player2UserId : Player1UserId;

    public string GetConnectionId(int userId) =>
        userId == Player1UserId ? Player1ConnectionId : Player2ConnectionId;

    public Board GetOwnBoard(int userId) =>
        userId == Player1UserId ? Player1Board : Player2Board;

    public Board GetEnemyBoard(int userId) =>
        userId == Player1UserId ? Player2Board : Player1Board;

    public bool IsPlayerInGame(int userId) =>
        userId == Player1UserId || userId == Player2UserId;

    public string GetUsername(int userId) =>
        userId == Player1UserId ? Player1Username : Player2Username;

    public GameStateDto ToStateDto(int userId)
    {
        var ownBoard = GetOwnBoard(userId);
        var enemyBoard = GetEnemyBoard(userId);

        return new GameStateDto(
            GameId,
            Status,
            CurrentTurnUserId,
            userId,
            ownBoard.ToOwnBoardDto(),
            enemyBoard.ToEnemyViewDto(),
            userId == Player1UserId ? Player1Ready : Player2Ready,
            userId == Player1UserId ? Player2Ready : Player1Ready,
            WinnerUserId
        );
    }

    public static string SerializeShips(IEnumerable<ShipDto> ships) =>
        JsonSerializer.Serialize(ships);

    public static ShipDto[] DeserializeShips(string json) =>
        JsonSerializer.Deserialize<ShipDto[]>(json) ?? [];
}
