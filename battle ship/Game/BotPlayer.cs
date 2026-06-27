using Battleship.Shared.Constants;

namespace battle_ship.Game;

public class BotPlayer
{
    private readonly Random _random = new();
    private readonly HashSet<(int X, int Y)> _shotsFired = new();
    private readonly List<(int X, int Y)> _currentHits = new();

    public (int X, int Y) GetNextShot()
    {
        if (_currentHits.Count >= 2)
        {
            var shot = GetDirectionalShot();
            if (shot.HasValue)
                return shot.Value;
        }

        if (_currentHits.Count == 1)
        {
            foreach (var (nx, ny) in GetNeighbors(_currentHits[0].X, _currentHits[0].Y))
                if (!_shotsFired.Contains((nx, ny)))
                    return (nx, ny);
        }

        int x, y;
        do
        {
            x = _random.Next(GameConstants.BoardSize);
            y = _random.Next(GameConstants.BoardSize);
        } while (_shotsFired.Contains((x, y)));

        return (x, y);
    }

    private (int X, int Y)? GetDirectionalShot()
    {
        bool isHorizontal = _currentHits.Select(h => h.Y).Distinct().Count() == 1;

        if (isHorizontal)
        {
            var minX = _currentHits.Min(h => h.X);
            var maxX = _currentHits.Max(h => h.X);
            var y = _currentHits[0].Y;

            if (minX - 1 >= 0 && !_shotsFired.Contains((minX - 1, y)))
                return (minX - 1, y);
            if (maxX + 1 < GameConstants.BoardSize && !_shotsFired.Contains((maxX + 1, y)))
                return (maxX + 1, y);
        }
        else
        {
            var minY = _currentHits.Min(h => h.Y);
            var maxY = _currentHits.Max(h => h.Y);
            var x = _currentHits[0].X;

            if (minY - 1 >= 0 && !_shotsFired.Contains((x, minY - 1)))
                return (x, minY - 1);
            if (maxY + 1 < GameConstants.BoardSize && !_shotsFired.Contains((x, maxY + 1)))
                return (x, maxY + 1);
        }

        return null;
    }

    public void RegisterShot(int x, int y, bool isHit, bool isSunk)
    {
        _shotsFired.Add((x, y));

        if (isSunk)
        {
            _currentHits.Clear();
        }
        else if (isHit)
        {
            _currentHits.Add((x, y));
        }
    }

    private static IEnumerable<(int X, int Y)> GetNeighbors(int x, int y)
    {
        if (x > 0) yield return (x - 1, y);
        if (x < GameConstants.BoardSize - 1) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y < GameConstants.BoardSize - 1) yield return (x, y + 1);
    }
}