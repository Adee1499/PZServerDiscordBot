namespace PZServerDiscordBot;

public static class Logger
{
    public const string LogFile = "pzbot.log";

    public static void LogException(Exception ex, string context = "")
    {
        var text = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] {context}\n{ex}\n";
        try { File.AppendAllText(LogFile, text); } catch { }
    }
}
