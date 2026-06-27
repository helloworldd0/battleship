using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;
using Microsoft.AspNetCore.SignalR.Client;

namespace Battleship.Client.Services;

public class GameHubClient : IAsyncDisposable
{
    private HubConnection? _connection;
    private string? _token;
    private Guid? _currentGameId;

    public void SetCurrentGame(Guid gameId) => _currentGameId = gameId;
    public void ClearCurrentGame() => _currentGameId = null;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public event Action<string>? OnError;
    public event Action? OnQueueJoined;
    public event Action? OnQueueLeft;
    public event Action<MatchFoundDto>? OnMatchFound;
    public event Action? OnPlacementConfirmed;
    public event Action? OnOpponentReady;
    public event Action<int>? OnGameStarted;
    public event Action<int, ShotResultDto>? OnShotResult;
    public event Action<int?>? OnTurnChanged;
    public event Action<int?>? OnGameOver;
    public event Action<GameStateDto>? OnGameState;
    public event Action<int>? OnRematchRequested;
    public event Action? OnRematchAccepted;
    public event Action<int>? OnRematchDeclined;
    public event Action<int?>? OnPlacementTimeout;
    public event Action<int?>? OnTurnTimeout;
    public event Func<string?, Task>? Reconnected;

    public async Task ConnectAsync(string token)
    {
        _token = token;

        if (_connection is not null)
            await _connection.DisposeAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl(AppConfig.HubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_token);
            })
            .WithAutomaticReconnect()
            .Build();

        RegisterHandlers();

        await _connection.StartAsync();
        await WaitForConnectionAsync();
    }

    public async Task WaitForConnectionAsync()
    {
        while (_connection is null ||
               _connection.State == HubConnectionState.Connecting ||
               _connection.State == HubConnectionState.Reconnecting)
        {
            await Task.Delay(50);
        }
    }

    public Task JoinBotGameAsync() => InvokeAsync("JoinBotGame");

    private void RegisterHandlers()
    {
        _connection!.On<string>("OnError", msg => OnError?.Invoke(msg));
        _connection.On("OnQueueJoined", () => OnQueueJoined?.Invoke());
        _connection.On("OnQueueLeft", () => OnQueueLeft?.Invoke());
        _connection.On<MatchFoundDto>("OnMatchFound", dto => OnMatchFound?.Invoke(dto));
        _connection.On("OnPlacementConfirmed", () => OnPlacementConfirmed?.Invoke());
        _connection.On("OnOpponentReady", () => OnOpponentReady?.Invoke());
        _connection.On<int>("OnGameStarted", id => OnGameStarted?.Invoke(id));
        _connection.On<int, ShotResultDto>("OnShotResult", (userId, result) => OnShotResult?.Invoke(userId, result));
        _connection.On<int?>("OnTurnChanged", id => OnTurnChanged?.Invoke(id));
        _connection.On<int?>("OnGameOver", id => OnGameOver?.Invoke(id));
        _connection.On<GameStateDto>("OnGameState", state => OnGameState?.Invoke(state));
        _connection.On<int>("OnRematchRequested", id => OnRematchRequested?.Invoke(id));
        _connection.On<int>("OnRematchDeclined", id => OnRematchDeclined?.Invoke(id));
        _connection.On<int>("OnPlacementTimeout", id => OnPlacementTimeout?.Invoke(id));
        _connection.On<int?>("OnTurnTimeout", id => OnTurnTimeout?.Invoke(id));
        _connection.Reconnected += async (_) =>
        {
            await WaitForConnectionAsync();
            if (_currentGameId.HasValue)
                await InvokeAsync("GetGameState", _currentGameId.Value);
        };

        _connection.Closed += async (error) =>
        {
            Console.WriteLine("HUB CLOSED: " + error);
        };

        _connection.Reconnecting += (error) =>
        {
            Console.WriteLine("HUB RECONNECTING: " + error);
            return Task.CompletedTask;
        };
    }

    private async Task InvokeAsync(string method, params object?[] args)
    {
        await WaitForConnectionAsync();

        if (_connection is null || _connection.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Нет подключения к серверу.");

        switch (args.Length)
        {
            case 0:
                await _connection.InvokeAsync(method);
                break;
            case 1:
                await _connection.InvokeAsync(method, args[0]);
                break;
            case 2:
                await _connection.InvokeAsync(method, args[0], args[1]);
                break;
            case 3:
                await _connection.InvokeAsync(method, args[0], args[1], args[2]);
                break;
            default:
                throw new InvalidOperationException("Слишком много аргументов.");
        }
    }

    public Task JoinQueueAsync() => InvokeAsync("JoinQueue");
    public Task LeaveQueueAsync() => InvokeAsync("LeaveQueue");
    public Task PlaceRandomShipsAsync(Guid gameId) => InvokeAsync("PlaceRandomShips", gameId);
    public Task PlaceShipsAsync(Guid gameId, ShipDto[] ships) => InvokeAsync("PlaceShips", gameId, ships);
    public Task ShootAsync(Guid gameId, int x, int y) => InvokeAsync("Shoot", gameId, x, y);
    public Task SurrenderAsync(Guid gameId) => InvokeAsync("Surrender", gameId);
    public Task GetGameStateAsync(Guid gameId) => InvokeAsync("GetGameState", gameId);
    public Task RequestRematchAsync(Guid gameId) => InvokeAsync("RequestRematch", gameId);
    public Task DeclineRematchAsync(Guid gameId) => InvokeAsync("DeclineRematch", gameId);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
