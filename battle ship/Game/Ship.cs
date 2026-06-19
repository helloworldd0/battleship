using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;

namespace battle_ship.Game;

public class Ship
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Length { get; set; }
    public bool IsHorizontal { get; set; }
    public bool IsSunk { get; set; }

    public IEnumerable<(int X, int Y)> GetCells()
    {
        for (var i = 0; i < Length; i++)
        {
            yield return IsHorizontal ? (X + i, Y) : (X, Y + i);
        }
    }

    public ShipDto ToDto() => new(X, Y, Length, IsHorizontal);
}
