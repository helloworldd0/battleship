using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;

namespace Battleship.Shared.Utils;

public static class ShipPlacer
{
    public static ShipDto[] GenerateRandomPlacement()
    {
        var random = new Random();
        var ships = new List<ShipDto>();

        foreach (var length in GameConstants.ShipLengths)
        {
            var placed = false;
            for (var attempt = 0; attempt < 500 && !placed; attempt++)
            {
                var isHorizontal = random.Next(2) == 0;
                var maxX = isHorizontal ? GameConstants.BoardSize - length : GameConstants.BoardSize - 1;
                var maxY = isHorizontal ? GameConstants.BoardSize - 1 : GameConstants.BoardSize - length;
                var x = random.Next(maxX + 1);
                var y = random.Next(maxY + 1);

                var candidate = new ShipDto(x, y, length, isHorizontal);

                if (CanPlace(ships, candidate))
                {
                    ships.Add(candidate);
                    placed = true;
                }
            }

            if (!placed)
                return GenerateRandomPlacement();
        }

        return ships.ToArray();
    }

    private static bool CanPlace(List<ShipDto> existing, ShipDto candidate)
    {
        var cells = GetShipCells(candidate).ToHashSet();
        var occupied = existing.SelectMany(GetShipCells).ToHashSet();

        if (cells.Overlaps(occupied))
            return false;

        foreach (var (x, y) in cells)
        {
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (occupied.Contains((x + dx, y + dy)) && !cells.Contains((x + dx, y + dy)))
                        return false;
                }
        }

        return true;
    }

    private static IEnumerable<(int X, int Y)> GetShipCells(ShipDto ship)
    {
        for (var i = 0; i < ship.Length; i++)
            yield return ship.IsHorizontal ? (ship.X + i, ship.Y) : (ship.X, ship.Y + i);
    }
}