using Battleship.Shared.Enums;

namespace battle_ship.Models;

public class Game
{
    public Guid Id { get; set; }
    public GameStatus Status { get; set; } = GameStatus.WaitingPlacement;
    public int? CurrentTurnUserId { get; set; }
    public int? WinnerUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public ICollection<GamePlayer> Players { get; set; } = [];
}
