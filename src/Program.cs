using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using PZServerDiscordBot;

var settings = BotSettings.Load();
var missing = settings.Validate().ToList();

if (missing.Count > 0)
{
    Console.WriteLine($"Missing configuration. Set the following in {BotSettings.SettingsFile} or via environment variables:");
    foreach (var item in missing) Console.WriteLine($"  - {item}");
    return 1;
}

await using var console = new PterodactylConsole(settings.PanelUrl, settings.ServerId, settings.PterodactylApiKey);

var client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds });
var interactions = new InteractionService(client);
var restarter = new ServerRestarter(console, client, settings);
var mods = new ModUpdateWatcher(console, restarter, settings);

var services = new ServiceCollection()
    .AddSingleton(settings)
    .AddSingleton(console)
    .AddSingleton(client)
    .AddSingleton(interactions)
    .AddSingleton(restarter)
    .AddSingleton(mods)
    .BuildServiceProvider();

client.Log += Log;
interactions.Log += Log;

client.InteractionCreated += async interaction =>
{
    var ctx = new SocketInteractionContext(client, interaction);
    await interactions.ExecuteCommandAsync(ctx, services);
};

client.Ready += async () =>
{
    await interactions.RegisterCommandsToGuildAsync(settings.GuildId);
    Console.WriteLine($"[Discord] Ready as {client.CurrentUser}. Commands registered to guild {settings.GuildId}.");
};

await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services);
await console.StartAsync();
await client.LoginAsync(TokenType.Bot, settings.DiscordBotToken);
await client.StartAsync();

var shutdown = new TaskCompletionSource();
var lifetime = new CancellationTokenSource();
_ = mods.StartAsync(lifetime.Token);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.TrySetResult();
await shutdown.Task;

lifetime.Cancel();
await client.StopAsync();
return 0;

static Task Log(LogMessage msg)
{
    Console.WriteLine($"[{msg.Source}] {msg.Message}{(msg.Exception is null ? "" : " " + msg.Exception.Message)}");
    if (msg.Exception is not null) Logger.LogException(msg.Exception, msg.Source);
    return Task.CompletedTask;
}
