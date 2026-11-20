using Discord;
using Discord.WebSocket;

namespace PZServerDiscordBot;

public sealed class ServerRestarter(PterodactylConsole console, DiscordSocketClient discord, BotSettings settings)
{
    private static readonly int[] announceMinutes = [60, 30, 15, 10, 5, 2, 1];
    private readonly object gate = new();
    private CancellationTokenSource? current;

    public bool InProgress => current is not null;
    public DateTimeOffset? RestartAt { get; private set; }

    public bool Schedule(TimeSpan delay, string reason)
    {
        lock (gate)
        {
            if (current is not null) return false;
            current = new CancellationTokenSource();
            RestartAt = DateTimeOffset.UtcNow + delay;
            _ = RunAsync(delay, reason, current.Token);
            return true;
        }
    }

    public bool Cancel()
    {
        lock (gate)
        {
            if (current is null) return false;
            current.Cancel();
            return true;
        }
    }

    private async Task RunAsync(TimeSpan delay, string reason, CancellationToken ct)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Notify($"🔄 Server restart scheduled <t:{RestartAt!.Value.ToUnixTimeSeconds()}:R> — {reason}");
                await Countdown(delay, reason, ct);
            }

            await Notify("🔄 Restarting server now…");

            if (console.PowerState == "running")
            {
                await TrySend($"servermsg \"Server is restarting now ({reason})\"");
                await TrySend("save");
                await Task.Delay(3000, ct);
                await TrySend("quit");
                if (!await console.WaitForStateAsync("offline", TimeSpan.FromMinutes(2), ct))
                    await console.PowerAsync("kill", ct);
                await console.WaitForStateAsync("offline", TimeSpan.FromSeconds(30), ct);
            }

            await console.PowerAsync("start", ct);

            if (await console.WaitForStateAsync("running", TimeSpan.FromMinutes(10), ct))
                await Notify("✅ Server is back online.");
            else
                await Notify($"⚠️ Server did not report `running` within 10 minutes (state: `{console.PowerState}`).");
        }
        catch (OperationCanceledException)
        {
            await TrySend("servermsg \"Scheduled restart has been cancelled\"");
            await Notify("❎ Restart cancelled.");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Restart failed");
            await Notify($"❌ Restart failed: {ex.Message}");
        }
        finally
        {
            lock (gate) { current?.Dispose(); current = null; RestartAt = null; }
        }
    }

    private async Task Countdown(TimeSpan delay, string reason, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + delay;
        var totalMinutes = (int)Math.Ceiling(delay.TotalMinutes);

        await TrySend($"servermsg \"Server restart in {totalMinutes} minute(s) - {reason}\"");

        foreach (var minutes in announceMinutes.Where(m => m < totalMinutes))
        {
            var wait = end - TimeSpan.FromMinutes(minutes) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            await TrySend($"servermsg \"Server restart in {minutes} minute(s) - {reason}\"");
        }

        var remaining = end - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
    }

    private async Task TrySend(string command)
    {
        try { await console.SendCommandAsync(command, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(300)); }
        catch (Exception ex) { Logger.LogException(ex, $"Failed to send '{command}'"); }
    }

    public async Task Notify(string message, Embed? embed = null)
    {
        try
        {
            if (discord.GetChannel(settings.EffectiveNotifyChannelId) is IMessageChannel channel)
                await channel.SendMessageAsync(message, embed: embed);
        }
        catch (Exception ex) { Logger.LogException(ex, "Failed to send Discord notification"); }
    }
}
