# BeoControl

A .NET 10 control system for Bang & Olufsen audio equipment via the Masterlink bus. BeoControl bridges legacy B&O hardware with modern software, supporting both USB PC2 controllers and ESP32-based Beo4 remote emulators over Serial/USB or Bluetooth LE.

## Overview

BeoControl lets you control B&O Masterlink devices (TV, Radio, CD, DVD, etc.) from a PC, offering:

- **Unified command interface** across heterogeneous B&O hardware
- **Audio control** — volume, bass, treble, balance, loudness
- **Source switching** — TV, Radio, CD, DVD, SAT, PC and more
- **Multi-transport support** — USB (PC2), Serial/USB (ESP32), Bluetooth LE (ESP32)
- **UI-agnostic design** — the core logic is fully decoupled from the UI layer, making it straightforward to add new frontends
- **Shared settings** — all UIs share a single settings file (`%APPDATA%\BeoControl\beocontrol.settings.json`) and auto-connect on startup

## UI Flexibility

The hardware and adapter layers are completely independent of any user interface. The `IDevice` abstraction is the only contract a UI needs to implement against, so different frontends can be built without touching the core logic:

| Frontend | Status | Description |
|---|---|---|
| **Terminal (TUI)** | ✅ included | Full-featured console UI using [RazorConsole](https://github.com/lofcz/razorconsole) — great for headless servers and SSH sessions |
| **Web (Blazor Server)** | ✅ included | ASP.NET Core Blazor Server UI — browser-based remote control |
| **Desktop (MAUI)** | ✅ included | Windows tray app — sits in the system tray, pops up above the taskbar on click, xcopy deploy |
| **REST / API** | 🔧 possible | Wrap `IDevice` in a minimal ASP.NET Core API to integrate with Home Assistant, shortcuts, scripts, etc. |

The architecture is designed so that **adding a new UI is simply a matter of referencing the `Interfaces` and `Adapters` packages** and calling `device.SendCommand(...)`.

```
Any UI  ──→  IDevice  ──→  Adapter  ──→  Transport  ──→  B&O Hardware
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                                   UI Layer                                              │
│                                                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐   ┌──────────────────┐│
│  │ BeoControl   │  │ BeoControl   │  │     BeoControl MAUI      │   │Beoport / Beolink ││
│  │     TUI      │  │Blazor Server │  │    Windows tray app      │   │  ControlExample  ││
│  │              │  │              │  │    IOs / OSX /Android    │   │ (a simple demo)  ││
│  │              │  │              │  │                          │   │                  ││
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────────┘   └─────────┬────────┘│
└─────────┼─────────────────┼─────────────────────┼─────────────────────────────┼─────────┘
          └─────────────────┴───────┬─────────────┴─────────────────────────────┘
                                    │ IDevice
          ┌─────────────────────────┴────────────────────────┐
          │                   Adapter Layer                  │
          │                                                  │
          │  ┌──────────────────────┐  ┌──────────────────┐  │
          │  │     Beo4Device       │  │    Pc2Device     │  │
          │  │   (Serial / BLE)     │  │    (USB PC2)     │  │
          │  └──────────┬───────────┘  └─────────┬────────┘  │
          └─────────────┼────────────────────────┼───────────┘
                        │                        │
          ┌─────────────┴───────┐  ┌─────────────┴──────────┐
          │  Transport Layer    │  │    Hardware Layer      │
          │  SerialTransport    │  │  Pc2Core (event loop)  │
          │  BluetoothTransport │  │  Pc2Mixer (audio HW)   │
          │  (BLE NUS)          │  │  Beolink (Masterlink)  │
          └──┬──────────────┬───┘  └───────────┬────────────┘
             │              │                  │
    ┌────────┴───┐  ┌───────┴────┐    ┌────────┴────┐
    │ M5 Atom S3 │  │ M5 Stamp S3│    │ Beolink PC2 │
    │ (Serial /  │  │ (Serial /  │    │   (USB)     │
    │   BLE)     │  │   BLE)     │    │             │
    └─────┬──────┘  └─────┬──────┘    └──────┬──────┘
          │ IR            │ IR               │ Masterlink
          ▼               ▼                  ▼
```

## Projects

| Project | Description |
|---|---|
| `Interfaces` | Core abstractions: `IDevice`, `ITransport`, `BeoCommands`, `Pc2Commands` |
| `Adapters/Beo4Adapter` | Adapts ESP32 Beo4 (Serial or BLE) to `IDevice` |
| `Adapters/Pc2Adapter` | Adapts PC2 USB hardware to `IDevice` |
| `Hardware/dotnet_pc2` | Low-level Masterlink/PC2 protocol implementation |
| `Hardware/esp32_beo4` | ESP32 firmware (PlatformIO) for Beo4 remote emulation |
| `UI/BeoControlTUI` | Terminal UI using RazorConsole |
| `UI/BeoControlBlazor` | Web UI using ASP.NET Core Blazor Server |
| `UI/BeoControlMaui` | Windows tray app using .NET MAUI + Blazor Hybrid |
| `UI/PC2ControlExample` | Minimal console app demonstrating direct PC2 adapter usage |

## Hardware Support

### PC2 USB Gateway

The PC2 is a USB device (VID `0x0CD4`, PID `0x0101`) that bridges the PC to the B&O Masterlink bus. BeoControl uses [LibUsbDotNet](https://github.com/LibUsbDotNet/LibUsbDotNet) for low-level USB communication and implements the full Masterlink telegram protocol.

The PC2 implementation and Masterlink protocol knowledge is based on the work of **Herwin Jan Steehouwer** on the KPC2 project, and on [**beoported**](https://github.com/toresbe/beoported/releases) by Tore Sinding Bekkedal — a big thank you to both for reverse-engineering and documenting the PC2 and Masterlink protocol.

### ESP32 Beo4 Remote

The ESP32 firmware emulates a Beo4 IR remote and communicates with BeoControl over Serial/USB or Bluetooth LE (Nordic UART Service). It runs on an M5 Atom S3 and is based on the excellent [**esp32_beo4**](https://github.com/aanban/esp32_beo4) project by **aanban** — many thanks for this great foundation!

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A B&O Masterlink system with either:
  - A **PC2 USB device** (connect to USB, install LibUSB driver), or
  - An **ESP32 Beo4** device (M5 Atom S3 with [esp32_beo4](https://github.com/aanban/esp32_beo4) firmware)

### Build

```bash
dotnet build BeoControl.sln
```

### Run TUI

```bash
dotnet run --project UI/BeoControlTUI
```

### Run Blazor Server

```bash
dotnet run --project UI/BeoControlBlazor/BeoControlBlazorServer
# Open http://localhost:5000 in a browser
```

### Run MAUI (Windows)

Build and run from Visual Studio, or publish for xcopy deployment:

```powershell
cd UI/BeoControlBlazor/BeoControlMaui
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -r win-arm64 --self-contained true -p:PublishTrimmed=false
```

Copy the `publish\` folder to any Windows machine — no installer or runtime install needed.

### GitHub Release (MAUI Windows)

The repository includes a GitHub Actions workflow at `.github/workflows/release-maui-windows.yml` that builds Windows release zips for the MAUI app and attaches them to a GitHub release.

- Push a tag like `v1.0.0` to trigger the workflow automatically.
- Or run the `Release MAUI Windows` workflow manually and provide a tag such as `v1.0.0`.
- The workflow publishes both `win-x64` and `win-arm64` self-contained builds.
- Each build is uploaded as a zip asset named `BeoControlMaui-<tag>-<runtime>.zip`.

The workflow creates the GitHub release automatically if it does not already exist.

### Run PC2 Example

```bash
dotnet run --project UI/PC2ControlExample/PC2ControlExample
```

### Linux

```bash
# Run targeting the non-Windows framework
dotnet run --project UI/BeoControlTUI --framework net10.0

# Grant serial port access (one-time, requires re-login)
sudo usermod -aG dialout $USER
```

### TUI Commands

| Command | Description |
|---|---|
| `/port` | Connect via auto-detected serial port |
| `/port scan` | List available serial ports |
| `/bt` | Connect via Bluetooth LE |
| `/bt scan` | Scan for BLE Beo4 devices |
| `/bt-last` | Reconnect to last BLE device |
| `/pc2` | Connect via PC2 USB |
| `/help` | Show all available commands |
| `/debug` | Toggle debug output |
| `/clear` | Clear screen |
| `/exit` | Exit application |

Once connected, type any B&O command directly (e.g. `tv`, `radio`, `vol+`, `standby`).

## Supported Commands

**Sources:** `tv`, `radio`, `cd`, `dvd`, `dvd2`, `phono`, `sat`, `pc`

**Volume:** `vol+`, `vol-`, `mute`, `loudness`

**Tone:** `bass+`, `bass-`, `treble+`, `treble-`, `balance+`, `balance-`

**Navigation:** `up`, `down`, `left`, `right`, `menu`, `exit`, `select`

**Colors:** `red`, `green`, `blue`, `yellow`

**Power:** `standby`, `allstandby`

## MAUI Tray App

The Windows tray app provides a compact always-available remote:

- **Starts hidden** to the system tray on launch
- **Click tray icon** → window appears just above the taskbar, centered on the icon
- **X button** hides to tray (does not exit); right-click for Show / Exit
- **Auto-connects** on startup to the last used device (shared settings with Blazor Server)
- **Remembers window size** — resize once, persisted across restarts
- **Connection indicator** — tray icon and in-app bar show green/red connection state
- **Xcopy deployment** — no installer needed; copy the `publish\` folder to any Windows ARM64 or x64 machine

## Shared Settings

All UIs share a single settings file so the last device is remembered across apps:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\BeoControl\beocontrol.settings.json` |
| Linux | `~/.config/BeoControl/beocontrol.settings.json` |
| macOS | `~/Library/Application Support/BeoControl/beocontrol.settings.json` |

The TUI uses a separate file (`beocontrol-tui.settings.json`) in the same folder.

## Simple HW — Direct IR to B&O Receiver

If you place the ESP32 very close to the B&O IR receiver, a single IR LED wired directly to the M5 Atom S3 is all the hardware you need:

```
          ┌──────────────────┐
          │   M5 Atom S3     │
          │                  │
          │  G38 ────────────┼──── LED anode  (+)
          │                  │        │ TSHA 6203 IR LED
          │  GND ────────────┼──── LED cathode (-)
          │                  │
          └──────────────────┘
```

> **G38** drives the IR LED signal. **GND** is the return path. No resistor is strictly required at very short range, but a 33–100 Ω resistor in series with the LED is recommended to protect the pin.

## Advanced HW — Transistor Driver with 3× IR LEDs

For greater range, a BC847 NPN transistor switches a higher current through three IR LEDs in parallel, each with its own R10 current limiting resistor, powered from V+:

```
  ┌──────────────────┐
  │               5V─┼────────────────────────────┐
  │                  │                            │
  │   M5 Atom S3     │                            │
  │                  │                  ┌─────────┼─────────┐
  │              G38─┼───────────┐     R10       R10       R10   (10 Ω each)
  │                  │           │      │         │         │
  │                  │           │     LED1      LED2      LED3  (TSHA 6203)
  │              GND─┼─┐         │      │         │         │
  └──────────────────┘ │         │      └─────────┴─────────┘
                       │       R470               │
                       │  ┌──────┼──────────┐     │
                       │  │      B          │     │
                       │  │                 │     │
                       │  │     BC847     C─┼─────┘
                       └──┼─E               │
                          └─────────────────┘
```

> **G38** → **R470** limits base current into the **BC847**. Each of the 3× **TSHA 6203** IR LEDs has its own **R10** resistor in series to V+. All three LED+R10 branches are in parallel, connected to the **BC847** collector. **GND** connects to the emitter.

## Credits & Acknowledgements

- **[aanban/esp32_beo4](https://github.com/aanban/esp32_beo4)** — ESP32 Beo4 remote firmware. A huge thanks for this project which made ESP32-based control possible.
- **[toresbe/beoported](https://github.com/toresbe/beoported/releases)** — The base and inspiration for the PC2/Masterlink protocol implementation.
- **[Herwin Jan Steehouwer](https://sourceforge.net/p/kpc2/code/)** — For the KPC2 project and invaluable work on reverse-engineering the PC2 hardware and Masterlink protocol.

## License

This project is provided as-is for personal/hobbyist use with Bang & Olufsen audio equipment.
