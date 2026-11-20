using System.Text.Json;
using Discord;

namespace PZServerDiscordBot;

public sealed class ModUpdateWatcher(PterodactylConsole console, ServerRestarter restarter, BotSettings settings)
{
    private const string StateFile = "pzbot-state.json";
    private Dictionary<string, long> lastSeen = LoadState();

    public IReadOnlyList<WorkshopItem> Items { get; private set; } = [];
    public DateTimeOffset? LastCheck { get; private set; }

    public Task StartAsync(CancellationToken ct) => Task.Run(() => LoopAsync(ct), ct);

    private async Task LoopAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        while (!ct.IsCancellationRequested)
        {
            try { await CheckAsync(ct); }
            catch (Exception ex) { Logger.LogException(ex, "Mod update check failed"); }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, settings.ModCheckIntervalMinutes)), ct);
        }
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        var ids = await ReadWorkshopIdsAsync(ct);
        Items = await SteamWorkshop.GetDetailsAsync(ids, ct);
        LastCheck = DateTimeOffset.UtcNow;

        var updated = new List<WorkshopItem>();
        var firstRun = lastSeen.Count == 0;

        foreach (var item in Items)
        {
            var ts = item.Updated.ToUnixTimeSeconds();
            if (lastSeen.TryGetValue(item.Id, out var seen) && ts > seen)
                updated.Add(item);
            lastSeen[item.Id] = ts;
        }

        SaveState();

        if (firstRun)
        {
            Console.WriteLine($"[Mods] Tracking {Items.Count} workshop items.");
            return;
        }

        if (updated.Count == 0) return;

        Console.WriteLine($"[Mods] {updated.Count} workshop item(s) updated.");

        var embed = new EmbedBuilder()
            .WithTitle("Workshop mod update detected")
            .WithColor(Color.Orange)
            .WithDescription(string.Join("\n", updated.Select(m => $"• [{m.Title}]({m.Url}) — updated <t:{m.Updated.ToUnixTimeSeconds()}:R>")))
            .WithCurrentTimestamp();

        if (!settings.AutoRestartOnModUpdate)
        {
            await restarter.Notify("", embed.Build());
            return;
        }

        if (console.PowerState != "running")
        {
            embed.WithFooter("Server is not running; no restart scheduled.");
            await restarter.Notify("", embed.Build());
            return;
        }

        if (restarter.InProgress)
        {
            embed.WithFooter("A restart is already in progress.");
            await restarter.Notify("", embed.Build());
            return;
        }

        await restarter.Notify("", embed.Build());
        restarter.Schedule(TimeSpan.FromMinutes(settings.ModUpdateRestartDelayMinutes), "workshop mod update");
    }

    private async Task<IReadOnlyList<string>> ReadWorkshopIdsAsync(CancellationToken ct)
    {
        var ini = await console.ReadFileAsync(settings.ZomboidIniPath, ct);

        foreach (var line in ini.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("WorkshopItems=", StringComparison.OrdinalIgnoreCase))
                return trimmed["WorkshopItems=".Length..]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct()
                    .ToList();
        }

        return [];
    }

    private static Dictionary<string, long> LoadState()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(StateFile)) ?? [];
        }
        catch (Exception ex) { Logger.LogException(ex, "Failed to load mod state"); }
        return [];
    }

    private void SaveState()
    {
        try { File.WriteAllText(StateFile, JsonSerializer.Serialize(lastSeen)); }
        catch (Exception ex) { Logger.LogException(ex, "Failed to save mod state"); }
    }
}
