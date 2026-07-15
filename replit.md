# RedSea

RedSea is a Steam companion application. It consists of two parts:

- **Web server + API** (`server.js`) — Express app on port 5000 with a key management system
- **Discord bot** (`bot.js`) — Admin interface for generating/revoking access keys
- **Desktop app** (`desktop/`) — C#/WPF Windows app (not runnable on Replit)

## Running

- Web server: `node server.js`
- Discord bot: `node bot.js` (requires `BOT_TOKEN` secret)

## Building the Windows .exe

The desktop app is built via GitHub Actions (Windows runner, .NET 9). To trigger a build:

```bash
bash fix-git.sh
```

This pushes to GitHub and tags `v1.0.0`, which triggers `.github/workflows/release.yml`. The compiled `RedSea.exe` will appear as a GitHub Release.

## Environment variables

| Variable | Required | Purpose |
|---|---|---|
| `BOT_TOKEN` | Bot only | Discord bot token |
| `BOT_SECRET` | Server + Bot | Shared secret for admin API endpoints |
| `BOT_OWNER_ID` | Optional | Discord user ID allowed to run bot commands |
| `SERVER_URL` | Optional | URL the bot uses to reach the API (default: `http://localhost:5000`) |
| `SESSION_SECRET` | Optional | Session signing secret |

## User preferences
