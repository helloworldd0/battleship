using System.Windows;
using Battleship.Client.ViewModels;

namespace Battleship.Client;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        EnemyBoard.CellClicked += async (x, y) => await _vm.CellShotAsync(x, y);
        PlacementBoard.CellClicked += (x, y) => _vm.PlacementCellClicked(x, y);
        PlacementBoard.CellHovered += (x, y) =>
            _vm.PlacementBoard = _vm.GetPlacementPreview(x, y);
        PlacementBoard.RightClicked += () =>
        {
            _vm.PlacementRotate();
        };
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.Password = PasswordBox.Password;
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _vm.DisposeAsync();
        base.OnClosed(e);
    }
}