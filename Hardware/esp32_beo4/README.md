# ESP32 Beo4 IR Emulator — Serial Command Interface

Send B&O Beo4 IR commands by typing human-readable commands into the serial monitor (115200 baud). The ESP32 transmits the corresponding 455 kHz modulated IR signal via the LED on GPIO 32.

## Quick Start
1. Flash the firmware, open serial monitor at **115200 baud**
2. Type `help` to see all available commands
3. Type e.g. `cd`, `vol+`, `radio 3`, `standby`

## Command Reference

| Command | Description |
|---------|-------------|
| **Source selection** (sets active source context) | |
| `tv` | Select TV (video) |
| `radio` | Select Radio (audio) |
| `cd` | Select CD (audio) |
| `phono` | Select Phono |
| `dvd` | Select DVD |
| `sat` | Select SAT |
| `vtape` | Select V.Tape |
| `pc` | Select PC |
| `light` | Select Light |
| `a.aux` / `v.aux` | Audio / Video Aux |
| **Numbers** | |
| `0` – `9` | Number keys (uses current source) |
| **Combined** | |
| `cd 4` | Select CD, then send digit 4 |
| `radio 31` | Select Radio, then send digits 3, 1 |
| **Volume & Mute** | |
| `vol+` / `vol-` | Volume up / down |
| `mute` | Mute |
| **Transport** | |
| `go` / `play` | Play / Go |
| `stop` | Stop |
| `record` | Record |
| **Navigation** | |
| `up` / `down` / `left` / `right` | Cursor |
| `menu` / `exit` / `return` / `select` | Menu navigation |
| **Power** | |
| `standby` / `off` | Standby |
| **Color buttons** | |
| `red` / `green` / `blue` / `yellow` | Color keys |
| **Other** | |
| `list`, `index`, `store`, `clear`, `tune` | Misc |
| `bass`, `treble`, `balance`, `loudness` | Sound settings |
| `0x0192` | Send raw hex beoCode |
| `help` | Show command list |
| `status` | Show current source |

## IR Sender Hardware
The circuit with two TIP121 transistors was successfully tested with a BeoSystem 2500. The receivers in Beo systems are designed for 880nm — use TSHA6500 diodes from Vishay.
