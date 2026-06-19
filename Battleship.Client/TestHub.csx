using Microsoft.AspNetCore.SignalR.Client;
var token = Environment.GetEnvironmentVariable("TOKEN")!;
var c = new HubConnectionBuilder().WithUrl("http://localhost:5075/gamehub", o => o.AccessTokenProvider = () => Task.FromResult<string?>(token)).Build();
c.On("OnQueueJoined", () => Console.WriteLine("OK: OnQueueJoined"));
await c.StartAsync();
try { await c.InvokeAsync("JoinQueue"); Console.WriteLine("OK: JoinQueue"); }
catch (Exception ex) { Console.WriteLine("FAIL: " + ex.Message); if (ex.InnerException != null) Console.WriteLine("Inner: " + ex.InnerException.Message); }
await c.DisposeAsync();
