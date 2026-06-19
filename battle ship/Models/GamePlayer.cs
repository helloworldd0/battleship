namespace battle_ship.Models;

public class GamePlayer
{
    public int Id { get; set; }
    public Guid GameId { get; set; }
    public int UserId { get; set; }
    public string BoardJson { get; set; } = "[]";
    public string ShotsJson { get; set; } = "[]";
    public bool IsReady { get; set; }

    public Game Game { get; set; } = null!;
    public User User { get; set; } = null!;
}
