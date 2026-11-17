using System.Text;
using Discord;
using Discord.Interactions;

namespace PZServerDiscordBot;

public sealed class PZCommands(PterodactylConsole console, BotSettings settings) : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxMessageLength = 2000;
    private const string CodeFence = "```";

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
        await RespondAsync($"Server state: **{console.PowerState}** · console: **{connected}**", ephemeral: true);
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
