using Battleship.Shared.Enums;

namespace Battleship.Shared.DTOs;

public record CoordinateDto(int X, int Y);

public record ShipDto(int X, int Y, int Length, bool IsHorizontal);

public record MatchFoundDto(Guid GameId, int OpponentId, string OpponentName);

public record ShotResultDto(int X, int Y, ShotResultType Result, ShipDto? SunkShip);

public record BoardCellDto(int X, int Y, CellState State);

public record PlayerBoardDto(BoardCellDto[] Cells);

public record GameStateDto(
    Guid GameId,
    GameStatus Status,
    int? CurrentTurnUserId,
    int MyUserId,
    PlayerBoardDto MyBoard,
    PlayerBoardDto EnemyBoard,
    bool IsReady,
    bool OpponentReady,
    int? WinnerUserId
);
