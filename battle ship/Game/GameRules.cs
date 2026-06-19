using Battleship.Shared.Constants;

namespace battle_ship.Game;

public static class GameRules
{
    public static (bool IsValid, string? Error) ValidatePlacement(List<Ship> ships)
    {
        var expected = GameConstants.ShipLengths.OrderBy(x => x).ToArray();
        var actual = ships.Select(s => s.Length).OrderBy(x => x).ToArray();

        if (!expected.SequenceEqual(actual))
            return (false, "Неверный набор кораблей. Нужно: 1×4, 2×3, 3×2, 4×1.");

        var occupied = new HashSet<(int X, int Y)>();

        foreach (var ship in ships)
        {
            if (ship.Length < 1 || ship.Length > 4)
                return (false, "Длина корабля должна быть от 1 до 4.");

            var cells = ship.GetCells().ToList();
            if (cells.Count != ship.Length)
                return (false, "Корабль выходит за границы поля.");

            foreach (var (x, y) in cells)
            {
                if (!IsInBounds(x, y))
                    return (false, "Корабль выходит за границы поля 10×10.");

                if (!occupied.Add((x, y)))
                    return (false, "Корабли не должны пересекаться.");
            }

            foreach (var (x, y) in cells)
            {
                foreach (var (nx, ny) in NeighborsIncludingDiagonals(x, y))
                {
                    if (occupied.Contains((nx, ny)) && !cells.Contains((nx, ny)))
                        return (false, "Корабли не должны соприкасаться.");
                }
            }
        }

        return (true, null);
    }

    public static bool IsInBounds(int x, int y) =>
        x >= 0 && x < GameConstants.BoardSize && y >= 0 && y < GameConstants.BoardSize;

    private static IEnumerable<(int X, int Y)> NeighborsIncludingDiagonals(int x, int y)
    {
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if (IsInBounds(nx, ny))
                yield return (nx, ny);
        }
    }
}
