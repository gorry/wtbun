# wtbun - Windows Terminal Bundler

[日本語](README.md) | English

## What is this?

Windows Terminal can group multiple terminals into one window as tabs. wtbun is a Windows app that bundles these tabbed windows further, one window per project.

- When you open a project folder, wtbun opens a Windows Terminal window for that project. The tabs it opens are defined in a configuration file in each project.
- wtbun is a thin tab strip that sticks to the top of the terminal window. Select a tab to switch to that project's window.
- All project windows share the same position and size. When you move or resize one, the others follow.
- When you exit wtbun, it closes the project windows too. The next time you start it, it reopens the previous projects.

This app is a personal project and is not affiliated with Microsoft.

## Requirements

- Windows 10 / 11
- [Windows Terminal](https://aka.ms/terminal) (tested with 1.24)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Usage

### Open a project

1. Start `Wtbun.exe`. A window with only a thin tab strip appears.
2. Click "+" on the tab strip, choose "Select folder…", and select the project folder.
3. A Windows Terminal window for the project opens, and the tab strip attaches to its top.

Use "+" in the same way to open as many projects as you like. If you select a folder that is already open, you can choose "Reopen" (reloads `.wtbun.toml` and opens the window again; running commands end), "Bring to front" (only switches to the open window), or "Cancel".

Folders you have opened can be selected from "History" in the "+" menu (up to 20, newest first). With "Clear history…", you can clear either the invalid folders only (folders that no longer exist or no longer have `.wtbun.toml`) or all folders.

### Switch, reorder, and close projects

- **Switch**: Click a tab on the tab strip to bring that project's window to the front. If you select a window directly, for example from the taskbar, the tab strip attaches to that window.
- **Reorder**: Drag a tab, or right-click it and choose "Move left" or "Move right".
- **Close**: Click "×" on the tab, or right-click it and choose "Close". If you close the window in Windows Terminal, the tab disappears automatically.

### Window position and size

- All project windows have the same position and size. When you move or resize the front window, the other windows follow when you finish.
- Drag an empty part of the tab strip to move the tab strip and all windows together.
- To leave room for the tab strip, a window moved to the top edge of the screen is shifted down by the height of the tab strip.
- When you maximize a window, it fills the screen below the tab strip. Maximize it again to restore the original size.

### Exit and next start

- Choose "Exit wtbun" from "≡" at the right end of the tab strip to close all project windows, from the leftmost tab, and exit.
- The next time you start wtbun, it reopens the previous projects in the same order and position.
- For a window with multiple tabs, Windows Terminal asks for confirmation when it closes. The confirmations appear one at a time; answer one to move on to the next window. A window you cancel stays open and is used as is the next time you start wtbun. If you don't need the confirmation, turn it off in the Windows Terminal settings ("Warn when closing more than one tab").

### Settings

Choose from "≡" at the right end of the tab strip.

- **Settings…**: Choose the display language (English or 日本語). It switches immediately. At first, wtbun follows the Windows display language (English unless Windows is in Japanese).
- **Show terminals on the taskbar**: When off, project windows are not shown on the taskbar; only wtbun is. Clicking the wtbun button also shows the selected project's window.

### Version information

Choose "About wtbun…" from "≡" at the right end of the tab strip to see the version, copyright, release page, and licenses (NOTICE and LICENSE). "Copy version info" copies the app name and version.

## Project configuration file

When you open a project, wtbun creates `.wtbun.toml` directly in the project folder (if it already exists, wtbun opens the project as written). At first, it opens a single tab. To open more tabs, right-click the tab and choose "Edit project configuration…", or edit this file. The file wtbun creates includes comments that explain each key, with examples of adding tabs. The comments are written in the display language at the time the file is created.

The tab shows `.wtbun.ico` or `.wtbun.png` (searched in this order) in the project folder as its icon. If neither exists, it shows the wtbun icon. In "Edit project configuration…", choose an image with "Import…" to crop its center to a square and save it as `.wtbun.png`.

The file is written in [TOML](https://toml.io/en/). Write tabs one at a time, each starting with a `[[tabs]]` line. Text from `#` to the end of the line is a comment. Enclose values in `'…'` (single quotes) to write `\` and `"` in Windows paths and commands as is.

```toml
version = 1
name = 'My Project'

[[tabs]]
title = 'main'
dir = '.'

[[tabs]]
title = 'files'
dir = '.'
profile = 'Windows PowerShell'
command = 'powershell -NoExit -Command Get-ChildItem'

[[tabs.panes]]
split = 'right'
size = 0.4
command = 'npm run dev'
```

In this example, the second tab opens with the Windows PowerShell profile, lists the files in the folder, and waits at the prompt. That tab is then split to the right, and `npm run dev` runs in a pane 40% wide.

| Key | Description |
|---|---|
| `name` | Project name shown on the wtbun tab. If omitted, the folder name. Write it before the first `[[tabs]]` |
| `title` | Tab title. If omitted, the Windows Terminal default |
| `dir` | Starting folder. Relative paths are relative to the project folder |
| `command` | Command to run in the tab. If omitted, the profile default (a shell). Batch files (`.cmd`, `.bat`) such as `npm` can be written as is. The tab closes when the command ends; to keep it open, put `cmd /k` (for PowerShell, `powershell -NoExit -Command`) before the command |
| `profile` | Windows Terminal profile name (as shown in the Windows Terminal settings). If omitted, the default profile |
| `append_command_line` | If `true`, `command` is appended to the command line of the profile (write the arguments for the profile's command in `command`) |
| `tab_color` | Tab color, as `'#RRGGBB'` or `'#RGB'`. If omitted, the profile setting |
| `color_scheme` | Color scheme name (for example, `'One Half Dark'`). If omitted, the profile setting |
| `suppress_application_title` | If `true`, running programs can't change the tab title. If `false`, they can. If omitted, the profile setting |
| `inherit_environment` | If `true`, the environment variables of Windows Terminal are inherited; if `false`, they are loaded fresh. If omitted, they are inherited when `command` is set |
| `selected` | If `true`, this tab is selected when opened. Only one tab can have it. If omitted, the last tab |

To split a tab, write `[[tabs.panes]]` after its `[[tabs]]`. Each one splits the previous pane, in order. In `[[tabs.panes]]`, you can write the keys in the table above (from `title` to `inherit_environment`) and the following keys.

| Key | Description |
|---|---|
| `split` | Where to put the new pane: `'right'` or `'down'`. If omitted, it depends on the pane's width and height |
| `size` | Size of the new pane, as a ratio of the pane before splitting: a number greater than 0 and less than 1, such as `0.4`. If omitted, half |
| `duplicate` | If `true`, the pane opens with the same profile and folder as the previous pane |
| `selected` | If `true`, this pane is selected within the tab when opened. Only one pane per tab can have it. If omitted, the first pane (the one opened by `[[tabs]]`) |

The tab shows the title of the selected pane.

### Run commands

For example, `command = 'ssh host'` connects to the host with ssh when you open the project.

`command = 'npm run dev'` starts the development server when you open the project (if the folder has `package.json` with a `dev` script).

#### Connect with an ssh profile and open a shell in a folder on the host

If you have a Windows Terminal profile that connects with ssh (for example, "myserver" with the command line `ssh.exe myserver`), you can also append a command to that profile.

```toml
[[tabs]]
title = 'server'
profile = 'myserver'
command = '-t "cd ~/work/project && exec $SHELL -l"'
append_command_line = true
```

Two points:

- Add `-t`. When a command follows, ssh doesn't allocate a terminal, so without `-t` you get a shell with no prompt
- Put the part to run on the host in one `"…"`. ssh joins the arguments with spaces and sends them to the host, so nested quotes such as `zsh -c "…"` break apart on the host

#### Create local folders for wtbun projects

With ssh, the folder you actually work in is on the host, but wtbun needs a local folder to hold `.wtbun.toml` to open it as a project. It is convenient to create a folder for each remote project in your local home folder.

```
%USERPROFILE%\wtbun\
├─ myserver-project\     ← open this folder with "+"
│   ├─ .wtbun.toml       ← opens ~/work/project on the host with ssh
│   └─ .wtbun.png        ← tab icon (optional)
└─ otherserver-tools\
    └─ .wtbun.toml
```

- The folder name is also the name shown on the wtbun tab (if `name` is not written)
- The contents of the local folder are not used, so `.wtbun.toml` alone is enough

`.wtbun.toml` is the layout used when opening. Tabs you add in Windows Terminal after opening are not saved to this file. If the file has a syntax error or a misspelled key, wtbun tells you the line or the key.

### Notes

- If Windows Terminal is set to restore the previous windows at startup, windows may open twice together with wtbun's reopening. Turn this setting off when you use wtbun.
- If you open another Windows Terminal window while wtbun is opening a project with "+", wtbun may mistake it for the project window.

## Run locally (development)

You need the .NET 10 SDK. If the Visual Studio C++ tools are installed, `wtbunhook.dll` (x64), which handles the confirmation when closing from the taskbar, is built too. Without them, you get a warning and only that confirmation doesn't work. The specification is in [memo/doc/spec.md](memo/doc/spec.md) and the design in [memo/doc/design.md](memo/doc/design.md) (both in Japanese).

```sh
dotnet build wtbun.slnx              # build
dotnet run --project src/Wtbun       # run
```

You can also open `wtbun.slnx` in Visual Studio and run it.

To try it without using your usual wtbun state (open projects and settings), specify another data folder with `--data-dir`.

```sh
dotnet run --project src/Wtbun -- --data-dir C:\temp\wtbun-test
```

To change the display language only for that run without changing the setting, pass `en-US` or `ja-JP` to `--lang`.

```sh
dotnet run --project src/Wtbun -- --lang ja-JP
```

A release build creates `src/Wtbun/bin/Release/net10.0-windows/Wtbun.exe`.

```sh
dotnet build wtbun.slnx -c Release
```

`release.cmd` in the repository root creates the distribution package. It puts the release binaries, README.md, README.en.md, LICENSE, and NOTICE in `release/wtbun_(version)/`, and zips that folder as `release/wtbun_(version).zip`. It also includes `wtbunhook.dll`, so the Visual Studio C++ tools are required.

```sh
release.cmd
```

## Structure

| Location | Contents |
|---|---|
| `src/Wtbun/` | The app (C# / WPF) |
| `src/Wtbun/Config/` | Reading and writing `.wtbun.toml`, the saved state, and the settings |
| `src/Wtbun/Localization/` | UI text (`Strings.resx` is English, `Strings.ja.resx` is Japanese) and language switching |
| `src/Wtbun/Terminal/` | Opening, finding, and closing Windows Terminal windows |
| `src/Wtbun/Layout/` | Aligning window positions and sizes |
| `src/Wtbun/Native/` | Win32 API calls (window operations and monitoring, taskbar) |
| `Directory.Build.props`, `Directory.Build.targets` | Read Profile.ini at build time and create the version information and constants |
| `Profile.ini` | Constants such as the app name, version, and copyright |
| `memo/doc/` | Design and specification documents (in Japanese) |
| `memo/log/` | Work logs (`YYYYMMDD.md`, in Japanese) |

The build output `bin/` and `obj/` are not tracked by Git (`.gitignore`).

wtbun saves the open projects and the settings in `%APPDATA%\wtbun\` (`state.json`, `settings.json`).

## Copyright

Copyright (C) 2026 GORRY. \
This package may be used, modified, and redistributed under the Apache License 2.0 (the LICENSE file). See the NOTICE file for the copyright and third-party notices.

## Contact

https://github.com/gorry/wtbun

## [EOF]
