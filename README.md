# BeoControl

A .NET 10 control system for Bang & Olufsen audio equipment via the Masterlink bus. BeoControl bridges legacy B&O hardware with modern software, supporting both USB PC2 controllers and ESP32-based Beo4 remote emulators over Serial/USB or Bluetooth LE.

## Overview

BeoControl lets you control B&O Masterlink devices (TV, Radio, CD, DVD, etc.) from a PC, offering:

- **Unified command interface** across heterogeneous B&O hardware
- **Audio control** — volume, bass, treble, balance, loudness
- **Source switching** — TV, Radio, CD, DVD, SAT, PC and more
- **Multi-transport support** — USB (PC2), Serial/USB (ESP32), Bluetooth LE (ESP32)
- **UI-agnostic design** — the core logic is fully decoupled from the UI layer, making it straightforward to add new frontends

## UI Flexibility

The hardware and adapter layers are completely independent of any user interface. The `IDevice` abstraction is the only contract a UI needs to implement against, so different frontends can be built without touching the core logic:

| Frontend | Status | Description |
|---|---|---|
| **Terminal (TUI)** | ✅ included | Full-featured console UI using [RazorConsole](https://github.com/lofcz/razorconsole) — great for headless servers and SSH sessions |
| **Web (Blazor)** | 🧪 POC | ASP.NET Core Blazor Server UI — proof of concept showing the web frontend path |
| **Mobile** | 🔧 possible | A .NET MAUI app could connect over BLE directly to the ESP32 or talk to a Blazor backend — no core changes needed |
| **REST / API** | 🔧 possible | Wrap `IDevice` in a minimal ASP.NET Core API to integrate with Home Assistant, shortcuts, scripts, etc. |
| **Desktop (WPF/WinUI)** | 🔧 possible | A native Windows UI is just another consumer of `IDevice` |

The architecture is designed so that **adding a new UI is simply a matter of referencing the `Interfaces` and `Adapters` packages** and calling `device.SendCommand(...)`.

```
Any UI  ──→  IDevice  ──→  Adapter  ──→  Transport  ──→  B&O Hardware
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                            UI Layer                                 │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌─────────┐  │
│  │ BeoControl   │  │ BeoControl   │  │  Mobile App  │  │   ...   │  │
│  │     TUI      │  │   Blazor     │  │ (.NET MAUI)  │  │         │  │
│  │(RazorConsole)│  │(Blazor Server│  │  (possible)  │  │         │  │
│  │              │  │    POC)      │  │              │  │         │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └────┬────┘  │
└─────────┼─────────────────┼─────────────────┼───────────────┼───────┘
          └─────────────────┴───────┬─────────┴───────────────┘
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
  │   M5 Atom S3     │
  │                  │
  │  5V ─────────────┼──────────────────── 5V
  │                  │                      │
  └──────────────────┘         ┌────────────┼────────────┐
                                │            │            │
                               R10          R10          R10   (10 Ω each)
                                │            │            │
                              LED1         LED2         LED3   (TSHA 6203)
                                │            │            │
                                └────────────┴────────────┘
                                                │
                                        ┌───────┴───────┐
                                        │    BC847      │
  ┌──────────────────┐                  │               │
  │   M5 Atom S3     │                  │  C (Collector)│
  │                  │                  │               │
  │  G38 ────────────┼──────────── R470─┤  B (Base)     │
  │                  │                  │               │
  │  GND ────────────┼──────────────────┤  E (Emitter)  │
  │                  │                  │               │
  └──────────────────┘                  └───────────────┘
```

> **G38** → **R470** limits base current into the **BC847**. Each of the 3× **TSHA 6203** IR LEDs has its own **R10** resistor in series to V+. All three LED+R10 branches are in parallel, connected to the **BC847** collector. **GND** connects to the emitter.

## Credits & Acknowledgements

- **[aanban/esp32_beo4](https://github.com/aanban/esp32_beo4)** — ESP32 Beo4 remote firmware. A huge thanks for this project which made ESP32-based control possible.
- **[toresbe/beoported](https://github.com/toresbe/beoported/releases)** — The base and inspiration for the PC2/Masterlink protocol implementation.
- **Herwin Jan Steehouwer** — For the KPC2 project and invaluable work on reverse-engineering the PC2 hardware and Masterlink protocol.

## License

This project is provided as-is for personal/hobbyist use with Bang & Olufsen audio equipment.
