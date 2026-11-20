# PZ Server Discord Bot (Pterodactyl edition)

A minimal Discord bot that lets you run Project Zomboid server console commands from Discord and see the output, for a dedicated server hosted on a [Pterodactyl](https://pterodactyl.io) panel.

The bot connects to the panel's console WebSocket, forwards your command, and returns whatever the server prints in response.

## Commands

| Command | Description |
|---|---|
| `/pz <command>` | Send a console command (e.g. `players`, `servermsg "Restart in 5"`, `save`) and get the output. Only works in the configured command channel. |
| `/pz-status` | Show the server power state, console connection and any pending restart. |
| `/pz-restart [minutes] [reason]` | Restart the server now or after a countdown. Players are warned in game at 15/10/5/2/1 minutes; Discord is notified when the restart starts and when the server is back online. |
| `/pz-restart-cancel` | Cancel a pending restart. |
| `/pz-mods` | List tracked Workshop mods and when they were last updated. |

## Automatic restarts on mod updates

Every `ModCheckIntervalMinutes` the bot reads `WorkshopItems=` from the server's `.ini` (via the panel file API) and checks each item's last-update time on Steam. When a mod is updated it posts an embed to the notify channel and, if `AutoRestartOnModUpdate` is on, schedules a restart after `ModUpdateRestartDelayMinutes` with in-game countdown messages. Last-seen timestamps are stored in `pzbot-state.json` in the working directory (`/data` in Docker) — mount it so a bot restart doesn't lose state.

## Requirements

- A Pterodactyl **Client API key** — create one in the panel under *Account → API Credentials*.
- Your server's **identifier** — the 8-character ID shown in the server's URL (`/server/abcd1234`).
- A Discord bot token with the bot invited to your guild (`applications.commands` and `bot` scopes).
- Your Discord **guild ID** and the **channel ID** where commands are allowed (enable Developer Mode in Discord, right-click → Copy ID).

## Configuration

Settings are read from `pzdiscordbot.conf` (JSON, created on first run in the working directory). Each value can be overridden by an environment variable, which is the recommended way to supply secrets.

| JSON key | Environment variable | Description |
|---|---|---|
| `DiscordBotToken` | `DISCORD_BOT_TOKEN` | Discord bot token |
| `PterodactylApiKey` | `PTERODACTYL_API_KEY` | Pterodactyl client API key (`ptlc_…`) |
| `PanelUrl` | `PTERODACTYL_PANEL_URL` | Panel URL, e.g. `https://panel.example.com` |
| `ServerId` | `PTERODACTYL_SERVER_ID` | Server identifier |
| `GuildId` | `DISCORD_GUILD_ID` | Discord guild ID the commands are registered to |
| `CommandChannelId` | `DISCORD_COMMAND_CHANNEL_ID` | Channel where `/pz` may be used |
| `NotifyChannelId` | `DISCORD_NOTIFY_CHANNEL_ID` | Channel for restart/mod-update notifications (defaults to the command channel) |
| `ZomboidIniPath` | `ZOMBOID_INI_PATH` | Server ini path inside the container (default `/.cache/Server/Pterodactyl.ini`) |
| `ModCheckIntervalMinutes` | `MOD_CHECK_INTERVAL_MINUTES` | How often to check Steam for mod updates (default `10`) |
| `ModUpdateRestartDelayMinutes` | `MOD_UPDATE_RESTART_DELAY_MINUTES` | Countdown before restarting after a mod update (default `15`) |
| `AutoRestartOnModUpdate` | `AUTO_RESTART_ON_MOD_UPDATE` | Restart automatically when a mod updates (default `true`) |
| `ResponseTimeoutSeconds` | — | Max time to wait for console output (default `5`) |
| `ResponseQuietPeriodSeconds` | — | Stop collecting once the console has been silent this long (default `1`) |

## Running

### Docker (prebuilt image)

Images are published to GitHub Container Registry on every push to `main`:

```sh
docker run -d --name pzbot --restart unless-stopped \
  -e DISCORD_BOT_TOKEN=... \
  -e PTERODACTYL_API_KEY=ptlc_... \
  -e PTERODACTYL_PANEL_URL=https://panel.example.com \
  -e PTERODACTYL_SERVER_ID=abcd1234 \
  -e DISCORD_GUILD_ID=123456789012345678 \
  -e DISCORD_COMMAND_CHANNEL_ID=123456789012345678 \
  ghcr.io/adee1499/pzserverdiscordbot:latest
```

On Unraid: *Docker → Add Container*, set **Repository** to `ghcr.io/adee1499/pzserverdiscordbot:latest`, then add the six variables above under *Add another Path, Port, Variable*. Optionally map a host path to `/data` to keep `pzbot.log`.

### Docker (build locally)

```sh
docker build -t pzbot .
docker run -d --name pzbot --restart unless-stopped --env-file pzbot.env pzbot
```

### .NET

Requires the .NET 10 SDK.

```sh
cd src
dotnet run -c Release
```

Fill in `pzdiscordbot.conf` (or set the environment variables) and run again.

## Notes

- Project Zomboid does not tag command responses, so the bot collects everything the console prints until it has been quiet for `ResponseQuietPeriodSeconds` (or `ResponseTimeoutSeconds` elapses). Busy servers may include unrelated log lines in the output.
- Commands are only accepted while the panel reports the server as `running`.
- The bot automatically reconnects to the console if the connection drops or the server restarts.

## License

GPL-3.0, see [LICENSE](LICENSE). Originally based on [egebilecen/PZServerDiscordBot](https://github.com/egebilecen/PZServerDiscordBot).
