using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using battle_ship.Data;
using battle_ship.Models;
using Battleship.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace battle_ship.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtSettings _jwt;

    public AuthService(AppDbContext db, IOptions<JwtSettings> jwt)
    {
        _db = db;
        _jwt = jwt.Value;
    }

    public async Task<(bool Success, AuthResponse? Response, string? Error)> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
            return (false, null, "Имя пользователя должно быть не короче 3 символов.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 4)
            return (false, null, "Пароль должен быть не короче 4 символов.");

        var username = request.Username.Trim();

        if (await _db.Users.AnyAsync(u => u.Username == username))
            return (false, null, "Пользователь с таким именем уже существует.");

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return (true, CreateAuthResponse(user), null);
    }

    public async Task<(bool Success, AuthResponse? Response, string? Error)> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username.Trim());
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return (false, null, "Неверное имя пользователя или пароль.");

        return (true, CreateAuthResponse(user), null);
    }

    public async Task<UserProfileDto?> GetProfileAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        return user is null ? null : new UserProfileDto(user.Id, user.Username);
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var token = GenerateToken(user);
        return new AuthResponse(token, user.Id, user.Username);
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwt.ExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
