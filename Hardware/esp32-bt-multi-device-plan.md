# ESP32 BLE multi-device support plan

## Problem
Evaluate whether the ESP32 Bluetooth channel can be changed so one ESP32 device can be used by more than one remote device, and identify the code changes required.

## Confirmed scope
- Target behavior: multiple devices connected at the same time to one ESP32.
- Command/session state: shared state is acceptable.

## Current state
- The ESP32 firmware is in `Hardware\esp32_beo4`.
- BLE is implemented in `Hardware\esp32_beo4\src\BtChannel.h` using NimBLE NUS.
- The current firmware is effectively single-client:
  - `BtChannel` stores a single `_connected` flag and a single RX ring buffer.
  - TX notifications are sent through one `_txChar` without tracking which client(s) are subscribed.
  - `main.cpp` routes all commands through one global `active` channel and one global command buffer.
  - Command state is global in `CommandProcessor.h` (`currentSrc`, `currentSrcName`), so all clients would currently share one session state.
- The .NET app side (`Adapters\Beo4Adapter\Transport\BluetoothTransport.cs`) is also single-connection per app instance, but that does not block multiple external clients talking to the same ESP32. It only matters if we want one BeoControl process to manage multiple simultaneous BLE sessions.

## Feasibility
- Likely feasible on the ESP32-S3/NimBLE side.
- Evidence:
  - NimBLE server callbacks expose per-connection metadata (`NimBLEConnInfo`), which is the shape typically used for multi-client peripherals.
  - NimBLE is built around a configurable maximum connection count, so the stack is not inherently single-client.
- The work is not a small toggle. The current firmware architecture assumes exactly one active channel and one shared command session.

## Recommended implementation approach
1. Implement shared-session multi-client BLE support.
   - Allow multiple clients to connect concurrently.
   - Serialize incoming commands into one shared command session on the ESP32.
   - Keep one shared `currentSrc/currentSrcName` state for all clients.

2. Refactor the firmware channel model.
   - Replace the single-client state in `BtChannel.h` with connection-aware bookkeeping.
   - Track connected peers and subscription state instead of only `_connected`.
   - Keep advertising enabled/restarted while capacity remains available.

3. Refactor command intake in `main.cpp`.
   - Remove the assumption that one global `active` channel owns all input.
   - Poll serial and BLE independently, or introduce a small dispatcher that drains whichever channel has input.
   - If BLE supports multiple simultaneous writers, queue complete lines before calling `processCommand`.

4. Keep shared command/session state.
   - Retain one shared `currentSrc/currentSrcName` state and document that all connected clients control the same target session.
   - Do not add per-client session isolation in the first implementation.

5. Verify host-side impact.
   - Firmware-only multi-client support should not require major changes in `BluetoothTransport.cs`.
   - If we want the BeoControl app itself to open and manage multiple ESP32 BLE links simultaneously, `DeviceService` and the `IDevice`/`ITransport` ownership model would need a separate multi-device redesign.

## Files likely to change
- `Hardware\esp32_beo4\src\BtChannel.h`
  - Main firmware change for multi-client BLE connection handling.
- `Hardware\esp32_beo4\src\main.cpp`
  - Remove the single `active` channel bottleneck.
- `Hardware\esp32_beo4\src\CommandProcessor.h`
  - Probably documentation/comments only for the first version, because shared state is acceptable.
- `Hardware\esp32_beo4\platformio.ini`
  - May need a build flag / config adjustment if the NimBLE max connection count must be raised explicitly.
- `Adapters\Beo4Adapter\Transport\BluetoothTransport.cs`
  - Only if we also want the desktop/mobile app to manage multiple concurrent BLE devices, which is a separate scope item.

## Proposed work breakdown
1. Inspect NimBLE config for effective max peripheral connections in the current PlatformIO/Arduino setup.
2. Refactor `BtChannel` to manage more than one BLE peer.
3. Refactor `main.cpp` channel dispatch so BLE no longer depends on one global active channel.
4. Keep shared command-state handling and document it clearly.
5. Build firmware for `m5atoms3` / `m5stamps3` and smoke-test:
   - one client still works,
   - second client can connect,
   - disconnect/reconnect resumes advertising correctly,
   - commands from both clients are handled as designed.

## Notes
- The current code already points to the right place to change: `BtChannel.h` is the BLE transport boundary for the firmware.
- The biggest architectural risk is not the BLE library itself; it is the single-session assumptions in `main.cpp` and `CommandProcessor.h`.
- The approved direction is the larger one: simultaneous BLE clients with one shared command session.
