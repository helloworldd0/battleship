namespace Battleship.Shared.DTOs;

public record RegisterRequest(string Username, string Password);

public record LoginRequest(string Username, string Password);

public record AuthResponse(string Token, int UserId, string Username);

public record UserProfileDto(int UserId, string Username);
