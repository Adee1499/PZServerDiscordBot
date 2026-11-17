using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZServerDiscordBot;

public sealed partial class PterodactylConsole : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly string panelUrl;
    private readonly string serverId;
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();

    private ClientWebSocket? socket;
    private Task? receiveLoop;
    private TaskCompletionSource authenticated = NewTcs();
    private Action<string>? outputListener;

    public string PowerState { get; private set; } = "unknown";
    public bool IsConnected => socket?.State == WebSocketState.Open && authenticated.Task.IsCompletedSuccessfully;

    public PterodactylConsole(string panelUrl, string serverId, string apiKey)
    {
        this.panelUrl = panelUrl.TrimEnd('/');
        this.serverId = serverId;

        http = new HttpClient { BaseAddress = new Uri($"{this.panelUrl}/api/client/servers/{serverId}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task StartAsync()
    {
        receiveLoop = Task.Run(ConnectionLoopAsync);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> SendCommandAsync(string command, TimeSpan timeout, TimeSpan quietPeriod, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to the server console.");

        if (PowerState != "running")
            throw new InvalidOperationException($"Server is not running (state: {PowerState}).");

        await commandLock.WaitAsync(ct);
        try
        {
            var lines = new List<string>();
            var lastOutput = Environment.TickCount64;
            var gotAny = false;

            outputListener = line =>
            {
                lock (lines) lines.Add(line);
                lastOutput = Environment.TickCount64;
                gotAny = true;
            };

            await SendEventAsync("send command", command, ct);

            var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                await Task.Delay(100, ct);
                if (gotAny && Environment.TickCount64 - lastOutput >= quietPeriod.TotalMilliseconds)
                    break;
            }

            outputListener = null;
            lock (lines) return lines.ToArray();
        }
        finally
        {
            commandLock.Release();
        }
    }

    private async Task ConnectionLoopAsync()
    {
        var delay = TimeSpan.FromSeconds(2);

        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(lifetime.Token);
                delay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Console websocket session failed; reconnecting.");
                Console.WriteLine($"[Pterodactyl] Console connection lost: {ex.Message}. Reconnecting in {delay.TotalSeconds}s.");
            }

            authenticated = NewTcs();
            await Task.Delay(delay, lifetime.Token);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var credentials = await GetWebsocketCredentialsAsync(ct);

        socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", panelUrl);
        await socket.ConnectAsync(new Uri(credentials.Socket), ct);
        await SendEventAsync("auth", credentials.Token, ct);

        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                throw new WebSocketException("Server closed the console websocket.");
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            await HandleMessageAsync(message.ToString(), ct);
            message.Clear();
        }
    }

    private async Task HandleMessageAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var evt = root.GetProperty("event").GetString();
        var arg = root.TryGetProperty("args", out var args) && args.GetArrayLength() > 0 ? args[0].GetString() ?? "" : "";

        switch (evt)
        {
            case "auth success":
                Console.WriteLine("[Pterodactyl] Console authenticated.");
                authenticated.TrySetResult();
                await SendEventAsync("send stats", null, ct);
                break;

            case "token expiring":
            case "token expired":
                var credentials = await GetWebsocketCredentialsAsync(ct);
                await SendEventAsync("auth", credentials.Token, ct);
                break;

            case "status":
                PowerState = arg;
                break;

            case "stats":
                using (var stats = JsonDocument.Parse(arg))
                    if (stats.RootElement.TryGetProperty("state", out var state))
                        PowerState = state.GetString() ?? PowerState;
                break;

            case "console output":
                outputListener?.Invoke(CleanLine(arg));
                break;

            case "jwt error":
            case "daemon error":
                throw new InvalidOperationException($"{evt}: {arg}");
        }
    }

    private async Task SendEventAsync(string evt, string? arg, CancellationToken ct)
    {
        if (socket is not { State: WebSocketState.Open })
            throw new InvalidOperationException("Console websocket is not open.");

        var payload = JsonSerializer.Serialize(new { @event = evt, args = arg is null ? null : new[] { arg } });
        await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);
    }

    private async Task<WebsocketCredentials> GetWebsocketCredentialsAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("websocket", ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Panel returned {(int)response.StatusCode} {response.ReasonPhrase} when requesting console credentials. Check the API key, panel URL and server id.");

        var body = await response.Content.ReadFromJsonAsync<WebsocketResponse>(ct);
        return body?.Data ?? throw new InvalidOperationException("Panel returned an empty websocket credential response.");
    }

    private static string CleanLine(string line) => AnsiRegex().Replace(line, "").TrimEnd();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiRegex();

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (socket is { State: WebSocketState.Open })
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
        socket?.Dispose();
        http.Dispose();
    }

    private sealed record WebsocketResponse(WebsocketCredentials Data);
    private sealed record WebsocketCredentials(string Token, string Socket);
}
