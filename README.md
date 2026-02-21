# CryptoPrice

Desktop widget for Windows that displays live cryptocurrency prices from Binance via WebSocket.

## Features

- Real-time price streaming from Binance WebSocket API
- 24h price change percentage
- Multiple crypto symbols with add/remove management
- Left/right navigation between symbols
- Borderless transparent always-on-top widget
- Drag to move anywhere on screen
- Zoom in/out (context menu or hotkeys)
- Customizable background and font colors with HSV color picker
- Opacity control
- Toggleable UI elements (navigation arrows, symbol label, 24h change)
- Settings persist between sessions

## Requirements

- Windows 10/11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for building)

## Run in development

```bash
dotnet run
```

## Build for production

Single-file self-contained executable (no .NET installation required on target machine):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true
```

| Flag | Description |
|------|-------------|
| `-c Release` | Release configuration with optimizations |
| `-r win-x64` | Target platform: Windows x64 |
| `--self-contained true` | Bundles .NET runtime into the executable |
| `-p:PublishSingleFile=true` | Packs everything into a single `.exe` |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | Embeds native libraries inside the exe |
| `-p:PublishTrimmed=true` | Removes unused code to reduce file size |

Output: `bin/Release/net10.0-windows/win-x64/publish/CryptoPrice.exe`

To publish into a custom folder:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true -o publish
```

## Usage

- **Right-click** on the widget to open the context menu
- **Drag** the widget to reposition
- **Settings...** — customize colors, opacity, and visible elements
- **Crypto List** — add, remove, or switch between symbols
- **Zoom In / Zoom Out** — scale the widget
