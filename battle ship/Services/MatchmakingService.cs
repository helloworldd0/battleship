using System.Collections.Concurrent;
using battle_ship.Game;

namespace battle_ship.Services;

public record QueuedPlayer(int UserId, string Username, string ConnectionId);

public class MatchmakingService
{
    private readonly ConcurrentQueue<QueuedPlayer> _queue = new();
    private readonly object _lock = new();

    public bool IsInQueue(string connectionId) =>
        _queue.Any(p => p.ConnectionId == connectionId);

    public void Enqueue(QueuedPlayer player)
    {
        Console.WriteLine($"QUEUE COUNT: {_queue.Count}");

        lock (_lock)
        {
            if (_queue.Any(p => p.UserId == player.UserId || p.ConnectionId == player.ConnectionId))
                return;

            _queue.Enqueue(player);
        }
    }

    public void DequeueByConnection(string connectionId)
    {
        lock (_lock)
        {
            var items = _queue.Where(p => p.ConnectionId != connectionId).ToList();
            while (_queue.TryDequeue(out _)) { }

            foreach (var item in items)
                _queue.Enqueue(item);
        }
    }

    public (QueuedPlayer? Player1, QueuedPlayer? Player2) TryMatch()
    {
        lock (_lock)
        {
            if (_queue.Count < 2)
                return (null, null);

            _queue.TryDequeue(out var p1);
            _queue.TryDequeue(out var p2);

            if (p1 is null || p2 is null)
                return (null, null);

            return (p1, p2);
        }
    }
}
