using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;

namespace Battleship.Client.Controls;

public class BoardControl : Grid
{
    public static readonly DependencyProperty BoardProperty =
        DependencyProperty.Register(nameof(Board), typeof(PlayerBoardDto), typeof(BoardControl),
            new PropertyMetadata(null, OnBoardChanged));

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(BoardControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowShipsProperty =
        DependencyProperty.Register(nameof(ShowShips), typeof(bool), typeof(BoardControl),
            new PropertyMetadata(true));

    public event Action<int, int>? CellClicked;

    public PlayerBoardDto? Board
    {
        get => (PlayerBoardDto?)GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public bool ShowShips
    {
        get => (bool)GetValue(ShowShipsProperty);
        set => SetValue(ShowShipsProperty, value);
    }

    public BoardControl()
    {
        for (var i = 0; i < GameConstants.BoardSize; i++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
    }

    private static void OnBoardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BoardControl control)
            control.RenderBoard();
    }

    private void RenderBoard()
    {
        Children.Clear();

        if (Board is null)
            return;

        foreach (var cell in Board.Cells)
        {
            var border = new Border
            {
                Background = GetBrush(cell.State),
                BorderBrush = Brushes.DarkSlateGray,
                BorderThickness = new Thickness(0.5),
                Tag = (cell.X, cell.Y)
            };

            if (IsInteractive)
            {
                border.Cursor = Cursors.Hand;
                border.MouseLeftButtonDown += (_, _) => CellClicked?.Invoke(cell.X, cell.Y);
            }

            Grid.SetRow(border, cell.Y);
            Grid.SetColumn(border, cell.X);
            Children.Add(border);
        }
    }

    private Brush GetBrush(CellState state)
    {
        if (!ShowShips && state == CellState.Ship)
            state = CellState.Unknown;

        return state switch
        {
            CellState.Empty => (Brush)Application.Current.Resources["WaterBrush"],
            CellState.Ship => (Brush)Application.Current.Resources["ShipBrush"],
            CellState.Miss => (Brush)Application.Current.Resources["MissBrush"],
            CellState.Hit => (Brush)Application.Current.Resources["HitBrush"],
            CellState.Sunk => (Brush)Application.Current.Resources["HitBrush"],
            _ => (Brush)Application.Current.Resources["WaterBrush"]
        };
    }
}
