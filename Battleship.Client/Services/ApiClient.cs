using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Battleship.Shared.DTOs;

namespace Battleship.Client.Services;

public class ApiClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(AppConfig.ServerUrl) };

    public string? Token { get; private set; }
    public int UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;

    public void SetAuth(AuthResponse auth)
    {
        Token = auth.Token;
        UserId = auth.UserId;
        Username = auth.Username;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    public void ClearAuth()
    {
        Token = null;
        UserId = 0;
        Username = string.Empty;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> RegisterAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, password));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, null, err?.Error ?? "Ошибка регистрации.");
        }

        var data = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return (true, data, null);
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, null, err?.Error ?? "Ошибка входа.");
        }

        var data = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return (true, data, null);
    }

    private record ErrorResponse(string Error);
}
