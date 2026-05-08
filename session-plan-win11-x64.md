# Session plan / handoff for Win11 x64

## Goal

Replace the confusing flat status model (`StatusType` + `StatusKind` + optional source fields) with a single typed status stream that keeps one `OnStatusChanged` subscription but separates device, source, and audio setup concerns.

## Implemented

- Kept one `OnStatusChanged` event.
- Removed `StatusType` and `StatusKind`.
- Introduced typed status messages:
  - `DeviceStatusMessage`
  - `SourceStatusMessage`
  - `AudioSetupMessage`
- Updated all main producers and consumers:
  - `Interfaces\ITransport.cs`
  - `Adapters\Beo4Adapter\Transport\SerialTransport.cs`
  - `Adapters\Beo4Adapter\Transport\BluetoothTransport.cs`
  - `Adapters\Beo4Adapter\Transport\DefaultBluetoothDiscovery.cs`
  - `Adapters\Beo4Adapter\Transport\ApplePickerBluetoothDiscovery.cs`
  - `Adapters\Pc2Adapter\Pc2Device.cs`
  - `UI\BeoControlBlazor\BeoControlBlazorServices\DeviceService.cs`
  - `UI\BeoControlBlazor\BeoControlBlazorCL\Pages\Remote.razor`
  - `UI\BeoControlBlazor\BeoControlBlazorCL\Pages\Setup.razor`
  - `UI\BeoControlBlazor\BeoControlBlazorCL\Components\PC2Setup.razor`
  - `UI\BeoControlTUI\Components\App.razor`
  - `UI\PC2ControlExample\Program.cs`

## Important behavior changes

- Source updates now flow as `SourceStatusMessage(string Command, int? Index = null)`.
- PC2 audio setup now flows as structured `AudioSetupMessage(...)`.
- `DeviceService` no longer reparses formatted audio status text.
- Scan/progress/info messages still expose `Text` through the base `StatusMessage`.

## Current validation state

- Bluetooth path looked good in manual testing on the current machine.
- PC2 path is **not runtime-tested yet** here because Windows ARM does not support the required WinUSB setup.
- Build checks already passed for:
  - `UI\BeoControlBlazor\BeoControlBlazorCL\BeoControlBlazorCL.csproj`
  - `UI\BeoControlTUI\BeoControlTUI.csproj`
  - `UI\PC2ControlExample\PC2ControlExample.csproj`

## What to do next on Win11 x64

1. Connect real PC2 hardware on the Win11 x64 machine.
2. Verify PC2 connect/disconnect status in the Blazor UI.
3. Verify source updates, especially numbered sources like `CD 2` / `RADIO 1`.
4. Verify PC2 audio setup updates in `PC2Setup`.
5. Verify Spotify source detection still works when PC2 trigger/source interactions are involved.

## Suggested quick checks

```powershell
dotnet build .\UI\BeoControlBlazor\BeoControlBlazorCL\BeoControlBlazorCL.csproj --nologo
dotnet build .\UI\BeoControlTUI\BeoControlTUI.csproj --nologo
dotnet build .\UI\PC2ControlExample\PC2ControlExample.csproj --nologo
```

## Notes

- The main architectural decision is now: **one event stays, but with typed variants instead of `Type`/`Kind` flags**.
- If PC2 runtime behavior shows gaps, start by checking pattern matches in:
  - `DeviceService.OnDeviceStatusChanged`
  - `Remote.razor`
  - `PC2Setup.razor`
  - `App.razor`
