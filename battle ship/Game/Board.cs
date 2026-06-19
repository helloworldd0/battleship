using Battleship.Shared.Constants;
using Battleship.Shared.DTOs;
using Battleship.Shared.Enums;

namespace battle_ship.Game;

public class Board
{
    private readonly List<Ship> _ships = [];
    private readonly HashSet<(int X, int Y)> _shots = [];

    public IReadOnlyList<Ship> Ships => _ships;
    public IReadOnlyCollection<(int X, int Y)> Shots => _shots;

    public bool AllShipsSunk => _ships.Count > 0 && _ships.All(s => s.IsSunk);

    public (bool Success, string? Error) PlaceShips(IEnumerable<ShipDto> ships)
    {
        var shipList = ships.Select(s => new Ship
        {
            X = s.X,
            Y = s.Y,
            Length = s.Length,
            IsHorizontal = s.IsHorizontal
        }).ToList();

        var validation = GameRules.ValidatePlacement(shipList);
        if (!validation.IsValid)
            return (false, validation.Error);

        _ships.Clear();
        _ships.AddRange(shipList);
        return (true, null);
    }

    public (ShotResultType Result, Ship? SunkShip) Shoot(int x, int y)
    {
        if (!GameRules.IsInBounds(x, y))
            return (ShotResultType.Invalid, null);

        if (!_shots.Add((x, y)))
            return (ShotResultType.AlreadyShot, null);

        var hitShip = _ships.FirstOrDefault(s => s.GetCells().Contains((x, y)));
        if (hitShip is null)
            return (ShotResultType.Miss, null);

        if (hitShip.GetCells().All(c => _shots.Contains(c)))
        {
            hitShip.IsSunk = true;
            return (ShotResultType.Sunk, hitShip);
        }

        return (ShotResultType.Hit, null);
    }

    public PlayerBoardDto ToOwnBoardDto()
    {
        var cells = new List<BoardCellDto>();

        for (var y = 0; y < GameConstants.BoardSize; y++)
        for (var x = 0; x < GameConstants.BoardSize; x++)
        {
            var state = GetOwnCellState(x, y);
            cells.Add(new BoardCellDto(x, y, state));
        }

        return new PlayerBoardDto(cells.ToArray());
    }

    public PlayerBoardDto ToEnemyViewDto()
    {
        var cells = new List<BoardCellDto>();

        for (var y = 0; y < GameConstants.BoardSize; y++)
        for (var x = 0; x < GameConstants.BoardSize; x++)
        {
            var state = GetEnemyCellState(x, y);
            cells.Add(new BoardCellDto(x, y, state));
        }

        return new PlayerBoardDto(cells.ToArray());
    }

    private CellState GetOwnCellState(int x, int y)
    {
        var ship = _ships.FirstOrDefault(s => s.GetCells().Contains((x, y)));
        if (ship is null)
            return _shots.Contains((x, y)) ? CellState.Miss : CellState.Empty;

        if (ship.IsSunk)
            return CellState.Sunk;

        return _shots.Contains((x, y)) ? CellState.Hit : CellState.Ship;
    }

    private CellState GetEnemyCellState(int x, int y)
    {
        if (!_shots.Contains((x, y)))
            return CellState.Unknown;

        var ship = _ships.FirstOrDefault(s => s.GetCells().Contains((x, y)));
        if (ship is null)
            return CellState.Miss;

        return ship.IsSunk ? CellState.Sunk : CellState.Hit;
    }
}
