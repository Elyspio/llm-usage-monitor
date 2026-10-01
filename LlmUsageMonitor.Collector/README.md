# @elyspio/llm-usage-collector

Reads the Claude Code and Codex session logs of a workstation (`~/.claude/projects/**/*.jsonl`, `~/.codex/sessions/**/*.jsonl`) and uploads their hourly token usage to [LLM Usage Monitor](https://monitor.llm.elyspio.fr) (`POST /api/token-usage`).

One package, two uses:

- **library**: imported by the Elytools desktop app, which provides its own OIDC sign-in and settings;
- **`llm-usage` CLI**: a standalone executable (Node SEA) for the workstations without Elytools. The same CLI runs through `npx` where Node ≥ 26 is installed.

## Standalone executable

Download `llm-usage-win-x64.exe` or `llm-usage-linux-x64` from the latest [`collector-v*` release](https://github.com/Elyspio/llm-usage-monitor/releases), then:

```sh
./llm-usage-linux-x64 install   # copies itself to ~/.local/bin/llm-usage and schedules `sync` every 5 minutes
llm-usage login                 # once: open the printed URL and enter the code (works over SSH)
llm-usage status
```

On Windows, `install` copies the executable to `%LOCALAPPDATA%\Programs\llm-usage\llm-usage.exe` and creates the scheduled task `LLM Usage Collector`. The task runs it through `conhost --headless`, so no window flashes. On Linux, `install` creates a `systemd --user` timer, or a crontab line when systemd is not available. To keep a user timer running on a server without an open session, run `loginctl enable-linger $USER`.

The Linux binary needs `libatomic1` (`apt install libatomic1`), like the official Node.js 26 build.

| Command                                                    | Effect                                                                                  |
| ---------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `login` / `logout`                                         | Device authorization grant, offline token kept in the data folder / revoked and removed |
| `sync`                                                     | Reads the new log lines and uploads the changed hours (what the schedule runs)          |
| `status`                                                   | Workstation, session, last run and error, pending hours, installation                   |
| `config`, `config set <key> <value>`, `config unset <key>` | `apiBaseUrl`, `machineName` (hostname when empty), `issuer`, `clientId`                 |
| `install` / `uninstall [--purge]`                          | Stable copy + schedule / both removed (`--purge`: the data folder too)                  |
| `update [--check]`                                         | Replaces the installed executable with the latest release                               |

The defaults target `https://monitor.llm.elyspio.fr` and the Keycloak client `i-llm-usage-collector` of `https://auth.elyspio.fr/realms/internal`. The signed-in account needs the `llm-usage-monitor:admin` client role.

## Data folder

`%LOCALAPPDATA%\elyspio\llm-usage` on Windows, `$XDG_DATA_HOME/elyspio/llm-usage` (default `~/.local/share/elyspio/llm-usage`) on Linux. `LLM_USAGE_DATA_DIR` overrides it; such a custom folder never takes over the Elytools data.

| File                        | Content                                                                     |
| --------------------------- | --------------------------------------------------------------------------- |
| `machine-id`                | Stable workstation id, generated once                                       |
| `state.json`, `status.json` | Read offsets, hourly totals not uploaded yet; last run                      |
| `sync.lock`                 | One sync at a time; taken over when its process is gone or after 15 minutes |
| `config.json`, `token.json` | CLI settings; offline refresh token (mode 0600 on Linux)                    |
| `install.json`              | Where `install` put the executable and which scheduler runs it              |
| `logs/collector.log`        | JSON lines, rotated at 1 MB (3 files kept)                                  |

Elytools and the CLI share this folder: on a workstation running both, they upload under the same machine id and take turns through the lock. The first run of either one moves the older Elytools data (`<app folder>/elytools/llm-usage`) here.

## Library

```ts
import { LlmUsageCollector, migrateLegacyData, defaultDataDir } from "@elyspio/llm-usage-collector";

await migrateLegacyData(defaultDataDir());
const collector = new LlmUsageCollector({
	holder: "elytools",
	getAccessToken: () => oidc.getAccessToken(),
	getSettings: () => ({ apiBaseUrl: "https://monitor.llm.elyspio.fr", machineName: "" }),
	logger,
});
const { outcome, error } = await collector.sync(); // "uploaded" | "locked" | "failed"
```

## Development

```sh
pnpm install
pnpm check       # Oxfmt, Oxlint, types
pnpm test
pnpm build       # dist/: library + bin (npm)
pnpm build:exe   # build/: llm-usage-win-x64.exe, llm-usage-linux-x64 (run it from PowerShell on Windows: the Git Bash tar cannot extract the Node archive)
```

`./release.ps1` checks, tests and builds, then publishes the npm package and the GitHub release `collector-v<version>` with both executables. Bump `version` in `package.json` first.
