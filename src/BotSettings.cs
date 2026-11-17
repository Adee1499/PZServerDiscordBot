using System.Text.Json;
using System.Text.Json.Serialization;

namespace PZServerDiscordBot;

public sealed class BotSettings
{
    public const string SettingsFile = "pzdiscordbot.conf";

    public string DiscordBotToken   { get; set; } = "";
    public string PterodactylApiKey { get; set; } = "";
    public string PanelUrl          { get; set; } = "";
    public string ServerId          { get; set; } = "";
    public ulong  GuildId           { get; set; }
    public ulong  CommandChannelId  { get; set; }
    public double ResponseTimeoutSeconds   { get; set; } = 5;
    public double ResponseQuietPeriodSeconds { get; set; } = 1;

    [JsonIgnore] public TimeSpan ResponseTimeout     => TimeSpan.FromSeconds(ResponseTimeoutSeconds);
    [JsonIgnore] public TimeSpan ResponseQuietPeriod => TimeSpan.FromSeconds(ResponseQuietPeriodSeconds);

    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public static BotSettings Load()
    {
        BotSettings settings;

        if (File.Exists(SettingsFile))
            settings = JsonSerializer.Deserialize<BotSettings>(File.ReadAllText(SettingsFile), jsonOptions) ?? new BotSettings();
        else
        {
            settings = new BotSettings();
            settings.Save();
        }

        settings.DiscordBotToken   = Env("DISCORD_BOT_TOKEN",    settings.DiscordBotToken);
        settings.PterodactylApiKey = Env("PTERODACTYL_API_KEY",  settings.PterodactylApiKey);
        settings.PanelUrl          = Env("PTERODACTYL_PANEL_URL", settings.PanelUrl).TrimEnd('/');
        settings.ServerId          = Env("PTERODACTYL_SERVER_ID", settings.ServerId);

        if (ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_GUILD_ID"), out var guildId))
            settings.GuildId = guildId;
        if (ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_COMMAND_CHANNEL_ID"), out var channelId))
            settings.CommandChannelId = channelId;

        return settings;
    }

    public void Save() => File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, jsonOptions));

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(DiscordBotToken))   yield return "Discord bot token (DiscordBotToken / DISCORD_BOT_TOKEN)";
        if (string.IsNullOrWhiteSpace(PterodactylApiKey)) yield return "Pterodactyl client API key (PterodactylApiKey / PTERODACTYL_API_KEY)";
        if (string.IsNullOrWhiteSpace(PanelUrl))          yield return "Pterodactyl panel URL (PanelUrl / PTERODACTYL_PANEL_URL)";
        if (string.IsNullOrWhiteSpace(ServerId))          yield return "Pterodactyl server identifier (ServerId / PTERODACTYL_SERVER_ID)";
        if (GuildId == 0)                                 yield return "Discord guild id (GuildId / DISCORD_GUILD_ID)";
        if (CommandChannelId == 0)                        yield return "Discord command channel id (CommandChannelId / DISCORD_COMMAND_CHANNEL_ID)";
    }

    private static string Env(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
