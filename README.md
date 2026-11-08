# Claude Launcher

Runs several Claude Desktop instances side by side, each with its own login, and arranges their windows. Windows (WinForms, .NET Framework 4) and macOS (SwiftUI) versions.

## Features

- One card per instance: name, color, signed-in email, running status, open/show, close.
- Active switch per card: only active instances join **Open & arrange**.
- Usage limit (5-hour / weekly) per card, read from the last rate-limit snapshot Claude cached with a conversation. Not live: the card shows when the snapshot was taken, and marks windows whose reset time has passed.
- Window layouts: templates (Focus, Columns, Rows, Grid, Priority Grid) and custom zones drawn in a zone editor, per monitor.
- Presets: save the active instances + layout + monitor under a name and open them in one click.
- Copy setup between instances (Desktop MCP servers, app preferences, tool toggles, Claude Code session presets, extensions). Login data is never copied; overwritten files are backed up to `launcher-backup/`.
- Template instance: new instances inherit its setup.
- System tray (Windows) / menu bar (macOS), global hotkeys (`Ctrl+Alt+1..9` / `⌃⌥1..9` show instance N, `Ctrl+Alt+0` / `⌃⌥0` open & arrange), optional start at login.

Each extra instance is Claude Desktop started with its own `--user-data-dir`.

| | Windows | macOS |
|---|---|---|
| Instance data | `%USERPROFILE%\.claude-<id>` | `~/.claude-launcher/profiles/<id>` |
| Launcher data | `%USERPROFILE%\.claude-launcher\` | `~/.claude-launcher/` |

## Build

### Windows

Uses the C# compiler that ships with .NET Framework 4:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize /out:ClaudeLauncher.exe /win32icon:assets\icon.ico `
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
  ClaudeLauncher.cs LayoutEditor.cs Features.cs Usage.cs
```

### macOS

Needs Xcode (the SwiftUI macro plugins are not in the Command Line Tools). `build.sh` defaults to `/Applications/Xcode-beta.app`; set `DEVELOPER_DIR` if Xcode lives elsewhere. The app is universal (Apple Silicon + Intel), macOS 14+.

```bash
mac/build.sh      # installs ~/Applications/Claude Launcher.app
mac/make-dmg.sh   # also writes mac/build/ClaudeLauncher.dmg
```

Open & arrange needs Accessibility permission (System Settings → Privacy & Security → Accessibility). The app is ad-hoc signed, so the permission has to be granted again after each rebuild.
