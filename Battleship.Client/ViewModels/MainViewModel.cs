using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Battleship.Client.Commands;
using Battleship.Client.Services;
using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;
using Battleship.Shared.Utils;

namespace Battleship.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ApiClient _api = new();
    private readonly GameHubClient _hub = new();

    private Guid _gameId;
    private MatchFoundDto? _match;
    private GameStateDto? _gameState;
    private ShipDto[]? _pendingShips;

    private string _statusText = "Добро пожаловать";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => Set(ref _username, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    private string _loginError = string.Empty;
    public string LoginError
    {
        get => _loginError;
        set { Set(ref _loginError, value); OnPropertyChanged(nameof(LoginErrorVisible)); }
    }

    public bool LoginErrorVisible => !string.IsNullOrEmpty(_loginError);

    private string _welcomeText = string.Empty;
    public string WelcomeText
    {
        get => _welcomeText;
        set => Set(ref _welcomeText, value);
    }

    private string _placementInfo = string.Empty;
    public string PlacementInfo
    {
        get => _placementInfo;
        set => Set(ref _placementInfo, value);
    }

    private string _currentPanel = "Login";
    public string CurrentPanel
    {
        get => _currentPanel;
        set
        {
            Set(ref _currentPanel, value);
            OnPropertyChanged(nameof(LoginVisible));
            OnPropertyChanged(nameof(LobbyVisible));
            OnPropertyChanged(nameof(PlacementVisible));
            OnPropertyChanged(nameof(BattleVisible));
        }
    }

    private bool _cancelSearchVisible = false;
    public bool CancelSearchVisible
    {
        get => _cancelSearchVisible;
        set => Set(ref _cancelSearchVisible, value);
    }

    public bool LoginVisible => CurrentPanel == "Login";
    public bool LobbyVisible => CurrentPanel == "Lobby";
    public bool PlacementVisible => CurrentPanel == "Placement";
    public bool BattleVisible => CurrentPanel == "Battle";

    private bool _findGameEnabled = true;
    public bool FindGameEnabled
    {
        get => _findGameEnabled;
        set => Set(ref _findGameEnabled, value);
    }

    private bool _confirmPlacementEnabled = false;
    public bool ConfirmPlacementEnabled
    {
        get => _confirmPlacementEnabled;
        set => Set(ref _confirmPlacementEnabled, value);
    }

    private bool _enemyBoardInteractive = false;
    public bool EnemyBoardInteractive
    {
        get => _enemyBoardInteractive;
        set => Set(ref _enemyBoardInteractive, value);
    }

    private PlayerBoardDto? _myBoard;
    public PlayerBoardDto? MyBoard
    {
        get => _myBoard;
        set => Set(ref _myBoard, value);
    }

    private PlayerBoardDto? _enemyBoard;
    public PlayerBoardDto? EnemyBoard
    {
        get => _enemyBoard;
        set => Set(ref _enemyBoard, value);
    }

    private PlayerBoardDto? _placementBoard;
    public PlayerBoardDto? PlacementBoard
    {
        get => _placementBoard;
        set => Set(ref _placementBoard, value);
    }

    private bool _gameOverVisible = false;
    public bool GameOverVisible
    {
        get => _gameOverVisible;
        set => Set(ref _gameOverVisible, value);
    }

    private string _gameOverText = string.Empty;
    public string GameOverText
    {
        get => _gameOverText;
        set => Set(ref _gameOverText, value);
    }

    private string _rematchStatusText = string.Empty;
    public string RematchStatusText
    {
        get => _rematchStatusText;
        set => Set(ref _rematchStatusText, value);
    }

    private bool _rematchButtonsEnabled = true;
    public bool RematchButtonsEnabled
    {
        get => _rematchButtonsEnabled;
        set => Set(ref _rematchButtonsEnabled, value);
    }

    private string _timerText = string.Empty;
    public string TimerText
    {
        get => _timerText;
        set => Set(ref _timerText, value);
    }

    private CancellationTokenSource? _uiTimer;

    private int _currentShipIndex = 0;
    private bool _placementIsHorizontal = true;

    public bool PlacementIsHorizontal
    {
        get => _placementIsHorizontal;
        set => Set(ref _placementIsHorizontal, value);
    }

    private readonly List<int> _shipsToPlace = GameConstants.ShipLengths.ToList();
    private readonly List<ShipDto> _placedShips = new();

    public string CurrentShipInfo
    {
        get
        {
            if (_currentShipIndex >= _shipsToPlace.Count)
                return "Все корабли расставлены!";
            return $"Ставим корабль: {_shipsToPlace[_currentShipIndex]} клетки " +
                   $"({(_placementIsHorizontal ? "горизонтально" : "вертикально")})";
        }
    }

    public ICommand LoginCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand FindGameCommand { get; }
    public ICommand PlayVsBotCommand { get; }
    public ICommand RandomPlacementCommand { get; }
    public ICommand ManualPlacementCommand { get; }
    public ICommand ConfirmPlacementCommand { get; }
    public ICommand SurrenderCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand CancelSearchCommand { get; }
    public ICommand RematchCommand { get; }
    public ICommand DeclineRematchCommand { get; }

    public Action<int, int>? OnCellShot { get; set; }

    public MainViewModel()
    {
        LoginCommand = new RelayCommand(async _ => await AuthenticateAsync(false));
        RegisterCommand = new RelayCommand(async _ => await AuthenticateAsync(true));
        FindGameCommand = new RelayCommand(async _ => await FindGameAsync());
        PlayVsBotCommand = new RelayCommand(async _ => await PlayVsBotAsync());
        RandomPlacementCommand = new RelayCommand(_ => RandomPlacement());
        ManualPlacementCommand = new RelayCommand(_ => ResetManualPlacement());
        ConfirmPlacementCommand = new RelayCommand(async _ => await ConfirmPlacementAsync(),
            _ => ConfirmPlacementEnabled);
        SurrenderCommand = new RelayCommand(async _ => await SurrenderAsync());
        LogoutCommand = new RelayCommand(async _ => await LogoutAsync());
        CancelSearchCommand = new RelayCommand(async _ => await CancelSearchAsync());
        RematchCommand = new RelayCommand(async _ => await RequestRematchAsync());
        DeclineRematchCommand = new RelayCommand(async _ => await DeclineRematchAsync());

        WireHubEvents();
    }

    private void WireHubEvents()
    {
        _hub.OnError += msg => App.Current.Dispatcher.Invoke(() => ShowError(msg));

        _hub.OnQueueJoined += () => App.Current.Dispatcher.Invoke(() =>
        {
            StatusText = "Поиск соперника...";
            FindGameEnabled = false;
            CancelSearchVisible = true;
        });

        _hub.OnQueueLeft += () => App.Current.Dispatcher.Invoke(() =>
        {
            StatusText = "Очередь покинута.";
            FindGameEnabled = true;
            CancelSearchVisible = false;
        });

        _hub.OnMatchFound += match => App.Current.Dispatcher.Invoke(() =>
        {
            CancelSearchVisible = false;
            _match = match;
            _gameId = match.GameId;
            _hub.SetCurrentGame(match.GameId);
            GameOverVisible = false;
            RematchButtonsEnabled = true;
            RematchStatusText = string.Empty;
            StatusText = $"Соперник найден: {match.OpponentName}";
            PlacementInfo = "Нажмите «Случайная расстановка» или подтвердите уже сгенерированную.";
            CurrentPanel = "Placement";
            _currentShipIndex = 0;
            _placementIsHorizontal = true;
            _placedShips.Clear();
            OnPropertyChanged(nameof(CurrentShipInfo));
            StartUiTimer(GameConstants.PlacementTimeSeconds);
            
            Console.WriteLine($"Ships to place: {_shipsToPlace.Count}, current index: {_currentShipIndex}");
        });

        _hub.OnGameState += state => App.Current.Dispatcher.Invoke(() => ApplyGameState(state));

        _hub.OnPlacementConfirmed += () => App.Current.Dispatcher.Invoke(() =>
        {
            StopUiTimer();

            StatusText = "Расстановка принята сервером. Ждём соперника...";
        });

        _hub.OnOpponentReady += () => App.Current.Dispatcher.Invoke(() =>
            StatusText = "Соперник расставил корабли.");

        _hub.OnGameStarted += firstTurnUserId => App.Current.Dispatcher.Invoke(() =>
        {
            StartUiTimer(GameConstants.TurnTimeSeconds);
            StatusText = firstTurnUserId == _api.UserId
                ? "Бой начался! Ваш ход."
                : "Бой начался! Ход соперника.";
            CurrentPanel = "Battle";
        });

        _hub.OnTurnChanged += userId => App.Current.Dispatcher.Invoke(() =>
        {
            StartUiTimer(GameConstants.TurnTimeSeconds);
            StatusText = userId == _api.UserId ? "Ваш ход." : "Ход соперника.";
            EnemyBoardInteractive = userId == _api.UserId;
        });

        _hub.OnShotResult += (shooterId, result) => App.Current.Dispatcher.Invoke(() =>
        {
            var who = shooterId == _api.UserId ? "Вы" : "Соперник";
            StatusText = $"{who}: {DescribeShot(result.Result)} ({(char)('A' + result.X)}{result.Y + 1})";
        });

        _hub.OnGameOver += winnerId => App.Current.Dispatcher.Invoke(() =>
        {
            StopUiTimer();
            GameOverText = winnerId == _api.UserId ? "🏆 Победа!" : "💀 Поражение.";
            RematchStatusText = "Сыграть ещё раз?";
            RematchButtonsEnabled = true;
            GameOverVisible = true;
            StatusText = GameOverText;
        });

        _hub.OnRematchRequested += userId => App.Current.Dispatcher.Invoke(() =>
        {
            if (userId != _api.UserId)
                RematchStatusText = "Соперник хочет реванш! Ваше решение?";
        });

        _hub.OnRematchDeclined += userId => App.Current.Dispatcher.Invoke(() =>
        {
            RematchStatusText = userId == _api.UserId
                ? "Вы отказались от реванша."
                : "Соперник отказался от реванша.";
            RematchButtonsEnabled = false;
            Task.Delay(2000).ContinueWith(_ =>
                App.Current.Dispatcher.Invoke(ResetToLobby));
        });

        _hub.OnPlacementTimeout += loserId => App.Current.Dispatcher.Invoke(() =>
        {
            StopUiTimer();
            StatusText = loserId == _api.UserId
                ? "Время на расстановку вышло — вы проиграли."
                : "Соперник не успел расставить корабли — вы победили!";
            ResetToLobby();
        });

        _hub.OnTurnTimeout += userId => App.Current.Dispatcher.Invoke(() =>
        {
            StopUiTimer();
            StatusText = userId == _api.UserId
                ? "Время хода вышло — вы проиграли."
                : "Время хода соперника вышло — вы победили!";
            ResetToLobby();
        });
    }

    private void StartUiTimer(int seconds)
    {
        _uiTimer?.Cancel();
        _uiTimer = new CancellationTokenSource();
        var token = _uiTimer.Token;
        var remaining = seconds;

        Task.Run(async () =>
        {
            while (remaining >= 0 && !token.IsCancellationRequested)
            {
                App.Current.Dispatcher.Invoke(() => TimerText = $"⏱ {remaining}с");
                await Task.Delay(1000, token).ContinueWith(_ => { });
                remaining--;
            }
        }, token);
    }

    private void StopUiTimer()
    {
        _uiTimer?.Cancel();
        _uiTimer = null;
        TimerText = string.Empty;
    }
    private async Task AuthenticateAsync(bool isRegister)
    {
        LoginError = string.Empty;

        var (success, data, error) = isRegister
            ? await _api.RegisterAsync(Username, Password)
            : await _api.LoginAsync(Username, Password);

        if (!success || data is null)
        {
            LoginError = error ?? "Ошибка.";
            return;
        }

        _api.SetAuth(data);
        await _hub.ConnectAsync(data.Token);

        WelcomeText = $"Привет, {data.Username}!";
        StatusText = "Подключено к серверу.";
        CurrentPanel = "Lobby";
    }

    private async Task FindGameAsync()
    {
        try
        {
            await _hub.JoinQueueAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.ToString());
        }
    }

    private async Task CancelSearchAsync()
    {
        try
        {
            await _hub.LeaveQueueAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task PlayVsBotAsync()
    {
        try
        {
            await _hub.JoinBotGameAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.ToString());
        }
    }

    private void RandomPlacement()
    {
        _placedShips.Clear();                    
        _currentShipIndex = _shipsToPlace.Count; 
        OnPropertyChanged(nameof(CurrentShipInfo)); 

        _pendingShips = ShipPlacer.GenerateRandomPlacement();
        PlacementBoard = RenderPlacementPreview(_pendingShips);
        ConfirmPlacementEnabled = true;
    }

    private void ResetManualPlacement()
    {
        _placedShips.Clear();
        _currentShipIndex = 0;
        _placementIsHorizontal = true;
        _pendingShips = null;
        ConfirmPlacementEnabled = false;
        PlacementBoard = RenderPlacementPreview(Array.Empty<ShipDto>());
        OnPropertyChanged(nameof(CurrentShipInfo));
    }

    public void PlacementCellClicked(int x, int y)
    {
        if (_currentShipIndex >= _shipsToPlace.Count) return;

        var length = _shipsToPlace[_currentShipIndex];
        var candidate = new ShipDto(x, y, length, _placementIsHorizontal);

        if (!CanPlaceShip(_placedShips, candidate)) return;

        _placedShips.Add(candidate);
        _currentShipIndex++;

        PlacementBoard = RenderPlacementPreview(_placedShips.ToArray());
        OnPropertyChanged(nameof(CurrentShipInfo));

        if (_currentShipIndex >= _shipsToPlace.Count)
        {
            _pendingShips = _placedShips.ToArray();
            ConfirmPlacementEnabled = true;
            OnPropertyChanged(nameof(CurrentShipInfo));
        }
    }

    public void PlacementRotate()
    {
        PlacementIsHorizontal = !PlacementIsHorizontal;
        OnPropertyChanged(nameof(CurrentShipInfo));
    }

    public PlayerBoardDto GetPlacementPreview(int hoverX, int hoverY)
    {
        if (_currentShipIndex >= _shipsToPlace.Count)
            return PlacementBoard ?? RenderPlacementPreview(Array.Empty<ShipDto>());

        var length = _shipsToPlace[_currentShipIndex];
        var candidate = new ShipDto(hoverX, hoverY, length, _placementIsHorizontal);
        var canPlace = CanPlaceShip(_placedShips, candidate);

        return RenderPlacementWithPreview(_placedShips.ToArray(), candidate, canPlace);
    }

    private static bool CanPlaceShip(List<ShipDto> existing, ShipDto candidate)
    {
        var cells = GetShipCells(candidate).ToList();
        if (cells.Count != candidate.Length) return false;
        if (cells.Any(c => c.X < 0 || c.X >= GameConstants.BoardSize ||
                           c.Y < 0 || c.Y >= GameConstants.BoardSize)) return false;

        var occupied = existing.SelectMany(GetShipCells).ToHashSet();
        if (cells.Any(c => occupied.Contains(c))) return false;

        foreach (var (x, y) in cells)
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (occupied.Contains((x + dx, y + dy)) && !cells.Contains((x + dx, y + dy)))
                        return false;

        return true;
    }

    private static IEnumerable<(int X, int Y)> GetShipCells(ShipDto ship)
    {
        for (var i = 0; i < ship.Length; i++)
            yield return ship.IsHorizontal ? (ship.X + i, ship.Y) : (ship.X, ship.Y + i);
    }

    private static PlayerBoardDto RenderPlacementWithPreview(ShipDto[] placed, ShipDto preview, bool canPlace)
    {
        var cells = new List<BoardCellDto>();
        for (var y = 0; y < GameConstants.BoardSize; y++)
            for (var x = 0; x < GameConstants.BoardSize; x++)
                cells.Add(new BoardCellDto(x, y, CellState.Empty));

        foreach (var ship in placed)
            foreach (var (sx, sy) in GetShipCells(ship))
                cells[sy * GameConstants.BoardSize + sx] = new BoardCellDto(sx, sy, CellState.Ship);

        var previewState = canPlace ? CellState.Hit : CellState.Sunk; 
        foreach (var (px, py) in GetShipCells(preview))
            if (px >= 0 && px < GameConstants.BoardSize && py >= 0 && py < GameConstants.BoardSize)
                cells[py * GameConstants.BoardSize + px] = new BoardCellDto(px, py, previewState);

        return new PlayerBoardDto(cells.ToArray());
    }

    private async Task ConfirmPlacementAsync()
    {
        if (_pendingShips is null)
        {
            ShowError("Сначала сгенерируйте расстановку.");
            return;
        }

        if (_currentShipIndex < _shipsToPlace.Count && _placedShips.Count > 0)
        {
            ShowError($"Расставьте все корабли. Осталось: {_shipsToPlace.Count - _currentShipIndex}");
            return;
        }


        try
        {
            await _hub.PlaceShipsAsync(_gameId, _pendingShips);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    public async Task CellShotAsync(int x, int y)
    {
        if (_gameState?.CurrentTurnUserId != _api.UserId)
            return;

        try
        {
            await _hub.ShootAsync(_gameId, x, y);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task SurrenderAsync()
    {
        if (MessageBox.Show("Сдаться?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        try
        {
            await _hub.SurrenderAsync(_gameId);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task LogoutAsync()
    {
        if (CancelSearchVisible)
        {
            try { await _hub.LeaveQueueAsync(); } catch { }
        }

        await _hub.DisposeAsync();
        _api.ClearAuth();
        Password = string.Empty;
        FindGameEnabled = true;       
        CancelSearchVisible = false;  
        CurrentPanel = "Login";
        StatusText = "Вы вышли из аккаунта.";
    }

    private async Task RequestRematchAsync()
    {
        RematchStatusText = "Ждём соперника...";
        RematchButtonsEnabled = false;
        try { await _hub.RequestRematchAsync(_gameId); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task DeclineRematchAsync()
    {
        try { await _hub.DeclineRematchAsync(_gameId); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ApplyGameState(GameStateDto state)
    {
        _gameState = state;

        if (state.Status == GameStatus.WaitingPlacement)
        {
            MyBoard = state.MyBoard;
            if (_placedShips.Count == 0 && _pendingShips is null)
                PlacementBoard = state.MyBoard;
            return;
        }

        MyBoard = state.MyBoard;
        EnemyBoard = state.EnemyBoard;
        EnemyBoardInteractive = state.CurrentTurnUserId == _api.UserId;

        if (state.Status == GameStatus.InProgress)
            CurrentPanel = "Battle";
    }

    private static PlayerBoardDto RenderPlacementPreview(ShipDto[] ships)
    {
        var cells = new List<BoardCellDto>();
        for (var y = 0; y < GameConstants.BoardSize; y++)
            for (var x = 0; x < GameConstants.BoardSize; x++)
                cells.Add(new BoardCellDto(x, y, CellState.Empty));

        foreach (var ship in ships)
            for (var i = 0; i < ship.Length; i++)
            {
                var x = ship.IsHorizontal ? ship.X + i : ship.X;
                var y = ship.IsHorizontal ? ship.Y : ship.Y + i;
                cells[y * GameConstants.BoardSize + x] = new BoardCellDto(x, y, CellState.Ship);
            }

        return new PlayerBoardDto(cells.ToArray());
    }

    private void ResetToLobby()
    {
        _hub.ClearCurrentGame();
        _gameId = Guid.Empty;
        _match = null;
        _gameState = null;
        _pendingShips = null;
        FindGameEnabled = true;
        ConfirmPlacementEnabled = false;
        CurrentPanel = "Lobby";
        StatusText = "Готов к новой игре.";
        GameOverVisible = false;
        GameOverText = string.Empty;
        RematchStatusText = string.Empty;
        RematchButtonsEnabled = true;
        _currentShipIndex = 0;
        _placementIsHorizontal = true;
        _placedShips.Clear();
    }

    private void ShowError(string message)
    {
        StatusText = message;
        MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string DescribeShot(ShotResultType result) => result switch
    {
        ShotResultType.Hit => "Ранение",
        ShotResultType.Sunk => "Потоплен",
        ShotResultType.Miss => "Мимо",
        _ => result.ToString()
    };

    public async Task DisposeAsync() => await _hub.DisposeAsync();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}