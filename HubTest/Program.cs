using Microsoft.AspNetCore.SignalR.Client;

var token = args[0];
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5075/gamehub", o => o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .Build();

connection.On<string>("OnError", msg => Console.WriteLine($"OnError: {msg}"));

try
{
    await connection.StartAsync();
    await connection.InvokeAsync("JoinQueue");
}
catch (Exception ex)
{
    Console.WriteLine($"Type: {ex.GetType().Name}");
    Console.WriteLine($"Message: {ex.Message}");
}

await connection.DisposeAsync();
