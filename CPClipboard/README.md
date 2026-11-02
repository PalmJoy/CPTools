# CPClipboard

[日本語](README.ja.md)

A snippet tool for Windows and macOS with a cyberpunk popup board. Save reusable text, choose a snippet, and paste it into your previous app.

- Open with **Ctrl twice**, or **Alt / ⌥ + middle-click**. Triggers are configurable; the optional hotkey is off by default.
- Organize text into **ten groups (0–9)**. Each group holds up to 500 snippets; each snippet supports up to 10,000 characters.
- Click or press Enter to paste. **Shift + click / Shift + Enter** copies only.
- By default, the selected snippet stays on the clipboard after pasting, ready for another Ctrl+V / ⌘V. Restoring the previous clipboard is optional (off by default; 500 ms when enabled).
- Customize colors, background images and effects. UI languages: English and Japanese.

## Install and first launch

[Public releases](https://github.com/PalmJoy/CPTools/releases) · [Download and setup](https://palmjoy.github.io/CPTools/CPClipboard/#setup)

Download links become available when a release is published. The app includes .NET; a separate runtime installation is unnecessary.

| Platform | Archive | Requirements |
| --- | --- | --- |
| Windows x64 | `CPClipboard-win-x64.zip` | Windows 10 (1809+) / 11 |
| Windows ARM64 | `CPClipboard-win-arm64.zip` | Windows 10 (1809+) / 11 |
| macOS | `CPClipboard-macos-arm64.zip` | macOS 15+, Apple silicon |

**Windows:** extract the entire zip and run `CPClipboard.exe`. Keep `uiohook.dll` beside it. Check the download source before allowing a Windows security prompt.

**macOS:** extract the zip and move `CPClipboard.app` to Applications. In System Settings → Privacy & Security → Accessibility, allow CPClipboard to read triggers and send the paste shortcut. Until permission is granted, snippets are copied only. For an unnotarized build, confirm the source before allowing it through Privacy & Security. The menu bar icon offers a permission guide.

The app lives in the system tray or menu bar. Open **Edit…**, choose a group, and add text. Edits save automatically. Quit from the tray or menu bar menu.

## Use

| Action | Shortcut |
| --- | --- |
| Choose a snippet | ↑ / ↓ |
| Paste / copy one of the first 26 snippets | a–z / Shift+a–z |
| Paste | Enter / click |
| Copy only | Shift + Enter / Shift + click |
| Switch group | 0–9 |
| Next / previous nonempty group | → / ← or Ctrl+Tab / Ctrl+Shift+Tab |
| First / last snippet | Home / End |
| Close the board | Esc |

A row’s context menu offers Paste, Copy only and Edit. Hover over truncated or multiline text to preview it. In Edit…, edit, duplicate, reorder or move text between groups. List operations support one step of Undo.

Automatic paste requires a valid target and input permission. Elevated Windows apps and macOS Secure Input cause copy-only fallback. Clipboard formats that cannot be safely captured, or snapshots over 32 MiB, cannot be restored. Clipboard ownership is checked before restoration; cross-process comparison and writing are not atomic on macOS.

Open History… to search and reuse copied text. The filter is at the bottom; a–z selects the first 26 results, with Shift to copy only. The History settings tab provides Clear history, automatic expiry in minutes, and a freely entered maximum count capped at 1000. Snippet and history triggers are configured separately. History is stored locally in history.json and excluded from backups and exports. Variables/macros, rich-text snippets and cloud sync are outside the app’s scope. Update checks notify you of published releases and open the download site; updates are installed manually.

## Data and backup

| Platform | Data folder |
| --- | --- |
| Windows | `%APPDATA%\CPClipboard\` |
| macOS | `~/Library/Application Support/CPClipboard/` |

The folder contains `settings.json`, `templates.json`, `background/`, `backup/` and optional `logs/`. Open it from Settings → Data. Templates have generation backups (ten by default, at most one every ten minutes). Back up the whole folder while the app is stopped. Settings → Data also provides app-data export/import. Data from a newer schema is not overwritten.

For portable storage, create `portable.flag` beside the executable; data then goes into the adjacent `data/` folder. On macOS this means the executable inside the app bundle; keep portable data outside update replacements yourself.

## Build

Requires the .NET 10 SDK. On macOS, icon generation additionally uses Quick Look, ImageMagick and `iconutil`.

```sh
dotnet build src/CPClipboard.sln
dotnet test src/CPClipboard.sln
```

Stop the app before rebuilding. Create self-contained Release packages using `build-release.bat -Zip` on Windows or `./build-release.sh --zip` on macOS. Windows builds x64 and ARM64; macOS builds Apple silicon. Output: `bin/release/`. Mac signing selects `CODESIGN_IDENTITY`, the local `work/macos-signing.identity` file, or `FileLauncher Dev`, in that order. Put the SHA-1 fingerprint of a valid code-signing identity in the local file (never a private key). Missing or ambiguous identities stop the build; the signer never changes automatically. `CODESIGN_IDENTITY=-` explicitly requests ad-hoc signing for CI or temporary builds. Ad-hoc signing is not notarization. Optional signing/notarization settings are in the publish scripts under `src/build/`.

Launch the macOS app from Finder, or with `open bin/osx-arm64/CPClipboard.app`, so macOS launches the application bundle. Use this entry point when checking Accessibility permission.

Tag builds (`cpclipboard-v<Version>`, matching the Desktop csproj) test both platforms, package archives and create a **draft** GitHub Release with checksums, attestations and libuiohook source. Publishing is manual. Workflow dispatch builds artifacts without creating a Release. GitHub Pages serves `docs/` from the repository.

## License

Copyright © 2026 PalmJoy. [GPL-3.0-only](LICENSE). See [third-party notices](THIRD-PARTY-NOTICES.txt) for bundled libraries and fonts. The native LGPL libuiohook library is replaceable separately from the Windows executable.

[CPFileLauncher](https://github.com/PalmJoy/CPTools) is the companion file launcher.

Build from the CPTools root with Common present. See the [CPTools build instructions](../README.md).
