using BeoControl.Interfaces;
using InTheHand.Bluetooth;

namespace Beo4Adapter.Transport;

/// <summary>
/// Keeps platform-specific device discovery out of BluetoothTransport so shared GATT/NUS logic stays unchanged.
/// Apple needs picker-based discovery, while other platforms can keep the passive scan flow.
/// </summary>
internal interface IBluetoothDiscovery
{
    Task<IReadOnlyCollection<BluetoothDevice>> DiscoverAsync(
        string namePrefix,
        CancellationToken ct,
        Action<StatusMessage>? status = null);
}
