using System.Windows;
using System.Windows.Controls;
using Battleship.Client.Services;
using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;

namespace Battleship.Client;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly GameHubClient _hub = new();

    private Guid _gameId;
    private MatchFoundDto? _match;
    private GameStateDto? _gameState;
    private ShipDto[]? _pendingShips;

    public MainWindow()
    {
        InitializeComponent();
        EnemyBoard.CellClicked += EnemyBoard_CellClicked;
        WireHubEvents();
    }

    private void WireHubEvents()
    {
        _hub.OnError += msg => Dispatcher.Invoke(() => ShowError(msg));
        _hub.OnQueueJoined += () => Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Поиск соперника...";
            FindGameButton.IsEnabled = false;
        });
        _hub.OnQueueLeft += () => Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Очередь покинута.";
            FindGameButton.IsEnabled = true;
        });
        _hub.OnMatchFound += match => Dispatcher.Invoke(() =>
        {
            _match = match;
            _gameId = match.GameId;
            StatusText.Text = $"Соперник найден: {match.OpponentName}";
            ShowPanel(PlacementPanel);
            PlacementInfoText.Text = "Нажмите «Случайная расстановка» или подтвердите уже сгенерированную.";
        });
        _hub.OnGameState += state => Dispatcher.Invoke(() => ApplyGameState(state));
        _hub.OnPlacementConfirmed += () => Dispatcher.Invoke(() =>
            StatusText.Text = "Расстановка принята сервером. Ждём соперника...");
        _hub.OnOpponentReady += () => Dispatcher.Invoke(() =>
            StatusText.Text = "Соперник расставил корабли.");
        _hub.OnGameStarted += firstTurnUserId => Dispatcher.Invoke(() =>
        {
            StatusText.Text = firstTurnUserId == _api.UserId
                ? "Бой начался! Ваш ход."
                : "Бой начался! Ход соперника.";
            ShowPanel(BattlePanel);
        });
        _hub.OnTurnChanged += userId => Dispatcher.Invoke(() =>
        {
            StatusText.Text = userId == _api.UserId ? "Ваш ход." : "Ход соперника.";
            EnemyBoard.IsInteractive = userId == _api.UserId;
        });
        _hub.OnShotResult += (shooterId, result) => Dispatcher.Invoke(() =>
        {
            var who = shooterId == _api.UserId ? "Вы" : "Соперник";
            StatusText.Text = $"{who}: {DescribeShot(result.Result)} ({(char)('A' + result.X)}{result.Y + 1})";
        });
        _hub.OnGameOver += winnerId => Dispatcher.Invoke(() =>
        {
            var message = winnerId == _api.UserId ? "Победа!" : "Поражение.";
            MessageBox.Show(message, "Игра окончена", MessageBoxButton.OK, MessageBoxImage.Information);
            ResetToLobby();
        });
    }

    private async void Login_Click(object sender, RoutedEventArgs e) =>
        await AuthenticateAsync(isRegister: false);

    private async void Register_Click(object sender, RoutedEventArgs e) =>
        await AuthenticateAsync(isRegister: true);

    private async Task AuthenticateAsync(bool isRegister)
    {
        LoginErrorText.Visibility = Visibility.Collapsed;
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        var (success, data, error) = isRegister
            ? await _api.RegisterAsync(username, password)
            : await _api.LoginAsync(username, password);

        if (!success || data is null)
        {
            LoginErrorText.Text = error ?? "Ошибка.";
            LoginErrorText.Visibility = Visibility.Visible;
            return;
        }

        _api.SetAuth(data);
        await _hub.ConnectAsync(data.Token);

        WelcomeText.Text = $"Привет, {data.Username}!";
        StatusText.Text = "Подключено к серверу.";
        ShowPanel(LobbyPanel);
    }

    private async void FindGame_Click(object sender, RoutedEventArgs e)
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

    private async void RandomPlacement_Click(object sender, RoutedEventArgs e)
    {
        _pendingShips = GenerateLocalRandomShips();
        RenderPlacementPreview(_pendingShips);
        ConfirmPlacementButton.IsEnabled = true;
    }

    private async void ConfirmPlacement_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingShips is null)
        {
            ShowError("Сначала сгенерируйте расстановку.");
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

    private async void EnemyBoard_CellClicked(int x, int y)
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

    private async void Surrender_Click(object sender, RoutedEventArgs e)
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

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _hub.DisposeAsync();
        _api.ClearAuth();
        PasswordBox.Clear();
        ShowPanel(LoginPanel);
        StatusText.Text = "Вы вышли из аккаунта.";
    }

    private void ApplyGameState(GameStateDto state)
    {
        _gameState = state;

        if (state.Status == GameStatus.WaitingPlacement)
        {
            MyBoard.Board = state.MyBoard;
            PlacementBoard.Board = state.MyBoard;
            return;
        }

        MyBoard.Board = state.MyBoard;
        EnemyBoard.Board = state.EnemyBoard;
        EnemyBoard.IsInteractive = state.CurrentTurnUserId == _api.UserId;

        if (state.Status == GameStatus.InProgress)
            ShowPanel(BattlePanel);
    }

    private void RenderPlacementPreview(ShipDto[] ships)
    {
        var cells = new List<BoardCellDto>();
        for (var y = 0; y < GameConstants.BoardSize; y++)
        for (var x = 0; x < GameConstants.BoardSize; x++)
            cells.Add(new BoardCellDto(x, y, CellState.Empty));

        foreach (var ship in ships)
        {
            for (var i = 0; i < ship.Length; i++)
            {
                var x = ship.IsHorizontal ? ship.X + i : ship.X;
                var y = ship.IsHorizontal ? ship.Y : ship.Y + i;
                var idx = y * GameConstants.BoardSize + x;
                cells[idx] = new BoardCellDto(x, y, CellState.Ship);
            }
        }

        PlacementBoard.Board = new PlayerBoardDto(cells.ToArray());
    }

    private static ShipDto[] GenerateLocalRandomShips()
    {
        var random = new Random();
        var ships = new List<ShipDto>();

        foreach (var length in GameConstants.ShipLengths)
        {
            var placed = false;
            for (var attempt = 0; attempt < 500 && !placed; attempt++)
            {
                var horizontal = random.Next(2) == 0;
                var maxX = horizontal ? GameConstants.BoardSize - length : GameConstants.BoardSize - 1;
                var maxY = horizontal ? GameConstants.BoardSize - 1 : GameConstants.BoardSize - length;
                var x = random.Next(maxX + 1);
                var y = random.Next(maxY + 1);
                var candidate = new ShipDto(x, y, length, horizontal);

                if (CanPlace(ships, candidate))
                {
                    ships.Add(candidate);
                    placed = true;
                }
            }
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

    private void ResetToLobby()
    {
        _gameId = Guid.Empty;
        _match = null;
        _gameState = null;
        _pendingShips = null;
        FindGameButton.IsEnabled = true;
        ConfirmPlacementButton.IsEnabled = false;
        ShowPanel(LobbyPanel);
        StatusText.Text = "Готов к новой игре.";
    }

    private void ShowPanel(UIElement panel)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        LobbyPanel.Visibility = Visibility.Collapsed;
        PlacementPanel.Visibility = Visibility.Collapsed;
        BattlePanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string DescribeShot(ShotResultType result) => result switch
    {
        ShotResultType.Hit => "Ранение",
        ShotResultType.Sunk => "Потоплен",
        ShotResultType.Miss => "Мимо",
        _ => result.ToString()
    };

    protected override async void OnClosed(EventArgs e)
    {
        await _hub.DisposeAsync();
        base.OnClosed(e);
    }
}
