# IPChanger

A Windows utility for quickly switching static IP addresses, DHCP, and DNS on network adapters.

## Features

- View current network interface settings
- Set static IP, mask, gateway, and DNS
- Switch to DHCP
- Save presets for each adapter
- Available languages: English, Russian, Serbian, Uzbek
- Single instance application

## Tray Menu

![Tray Menu](traymenu.png)

The right-click context menu on the tray icon provides the following options for each interface:

- **Presets** — saved configurations. Each preset shows IP, mask, gateway, and DNS in its tooltip.
  - **Apply** — apply the preset's network settings
  - **Edit** — modify the preset
  - **Delete** — remove the preset
  - **No saved presets** — shown when no presets exist for the interface
- **Add preset** — create a new preset from scratch
- **Save current settings as preset** — save the interface's current active settings as a new preset
- **Enable DHCP** — switch the interface to automatic IP and DNS (DHCP)
- **Properties** — open the Windows TCP/IPv4 properties dialog for the interface

Global menu items (at the bottom):

- **ipconfig /all** — display full IP configuration in a window
- **Network Connections** — open the Windows Network Connections folder
- **Language** — select UI language (English, Russian, Serbian, Uzbek)
- **About** — show the About dialog with version info
- **Exit** — close the application

## Requirements

- Windows 10/11
- .NET 9.0 Runtime
- Administrator privileges

## Build

```bash
dotnet build --configuration Release
```

## Publish (single-file exe)

```bash
dotnet publish --configuration Release
```

Builds a 64-bit Windows executable with embedded translations.

## Build MSI Installer

```bash
dotnet build --configuration Release -t:Publish
```

Automatically builds the application and MSI installer (via WiX). The installer displays the license text during installation.

## Settings

Presets are stored in `%LOCALAPPDATA%\IPChanger\profiles\*.json`. Application settings are stored in `%LOCALAPPDATA%\IPChanger\settings.json`.

Translations are stored in `%LOCALAPPDATA%\IPChanger\Translations\*.json` and are also embedded as content in the installation directory.

## License

MIT. See [LICENSE](LICENSE) for details.

The MSI installer displays the license text during installation.
