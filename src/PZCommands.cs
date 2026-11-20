using System.Text;
using Discord;
using Discord.Interactions;

namespace PZServerDiscordBot;

public sealed class PZCommands(PterodactylConsole console, ServerRestarter restarter, ModUpdateWatcher mods, BotSettings settings) : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxMessageLength = 2000;
    private const string CodeFence = "```";

    private bool InCommandChannel => Context.Channel.Id == settings.CommandChannelId;

    [SlashCommand("pz-restart", "Restart the Project Zomboid server, optionally after a countdown.")]
    public async Task Restart(
        [Summary("minutes", "Countdown in minutes before restarting (default 0 = now)")] [MinValue(0)] [MaxValue(180)] int minutes = 0,
        [Summary("reason", "Reason shown to players and in Discord")] string reason = "manual restart")
    {
        if (!InCommandChannel) { await RespondAsync("This command can only be used in the configured command channel.", ephemeral: true); return; }

        if (restarter.InProgress)
        {
            await RespondAsync($"❌ A restart is already in progress (restarting <t:{restarter.RestartAt?.ToUnixTimeSeconds()}:R>). Use `/pz-restart-cancel` first.", ephemeral: true);
            return;
        }

        restarter.Schedule(TimeSpan.FromMinutes(minutes), $"{reason} (by {Context.User.Username})");
        await RespondAsync(minutes > 0
            ? $"✅ Restart scheduled in {minutes} minute(s). Players will be warned in game."
            : "✅ Restarting now…");
    }

    [SlashCommand("pz-restart-cancel", "Cancel a pending server restart.")]
    public async Task RestartCancel()
    {
        if (!InCommandChannel) { await RespondAsync("This command can only be used in the configured command channel.", ephemeral: true); return; }

        await RespondAsync(restarter.Cancel() ? "✅ Restart cancelled." : "Nothing to cancel.", ephemeral: !restarter.InProgress);
    }

    [SlashCommand("pz-mods", "List tracked workshop mods and when they were last updated.")]
    public async Task Mods()
    {
        if (mods.Items.Count == 0)
        {
            await RespondAsync("No workshop items tracked yet.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Tracked workshop mods ({mods.Items.Count})")
            .WithColor(Color.Blue)
            .WithDescription(string.Join("\n", mods.Items.OrderByDescending(m => m.Updated).Take(40)
                .Select(m => $"• [{m.Title}]({m.Url}) — <t:{m.Updated.ToUnixTimeSeconds()}:R>")))
            .WithFooter($"Last check: {mods.LastCheck:yyyy-MM-dd HH:mm} UTC · auto-restart {(settings.AutoRestartOnModUpdate ? "on" : "off")}")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("pz", "Send a command to the Project Zomboid server console and show its output.")]
    public async Task PZ([Summary("command", "The console command to run, e.g. players")] string command)
    {
        if (Context.Channel.Id != settings.CommandChannelId)
        {
            await RespondAsync("This command can only be used in the configured command channel.", ephemeral: true);
            return;
        }

        await DeferAsync();

        IReadOnlyList<string> output;
        try
        {
            output = await console.SendCommandAsync(command, settings.ResponseTimeout, settings.ResponseQuietPeriod);
        }
        catch (InvalidOperationException ex)
        {
            await FollowupAsync($"❌ {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Failed to send command: {command}");
            await FollowupAsync($"❌ Failed to send command: {ex.Message}");
            return;
        }

        var header = $"**>** `{command}`";

        if (output.Count == 0)
        {
            await FollowupAsync($"{header}\n*(no output within {settings.ResponseTimeout.TotalSeconds:0.#}s)*");
            return;
        }

        var first = true;
        foreach (var chunk in Chunk(output, MaxMessageLength - CodeFence.Length * 2 - header.Length - 4))
        {
            var text = (first ? header + "\n" : "") + CodeFence + "\n" + chunk + CodeFence;
            await FollowupAsync(text);
            first = false;
        }
    }

    [SlashCommand("pz-status", "Show the Project Zomboid server power state and console connection.")]
    public async Task Status()
    {
        var connected = console.IsConnected ? "connected" : "disconnected";
        var restart = restarter.InProgress ? $" · restart <t:{restarter.RestartAt?.ToUnixTimeSeconds()}:R>" : "";
        await RespondAsync($"Server state: **{console.PowerState}** · console: **{connected}**{restart}", ephemeral: true);
    }

    private static IEnumerable<string> Chunk(IEnumerable<string> lines, int maxLength)
    {
        var sb = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Replace("```", "'''");

            while (line.Length > maxLength)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                yield return line[..maxLength] + "\n";
                line = line[maxLength..];
            }

            if (sb.Length + line.Length + 1 > maxLength)
            {
                yield return sb.ToString();
                sb.Clear();
            }

            sb.Append(line).Append('\n');
        }

        if (sb.Length > 0) yield return sb.ToString();
    }
}
