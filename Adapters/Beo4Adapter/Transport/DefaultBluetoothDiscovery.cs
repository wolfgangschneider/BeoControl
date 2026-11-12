using BeoControl.Interfaces;
using InTheHand.Bluetooth;

namespace Beo4Adapter.Transport;

/// <summary>
/// Keeps the existing passive BLE discovery behavior for platforms where scanning works without Apple's picker.
/// </summary>
internal sealed class DefaultBluetoothDiscovery : IBluetoothDiscovery
{
    public async Task<IReadOnlyCollection<BluetoothDevice>> DiscoverAsync(
        string namePrefix,
        CancellationToken ct,
        Action<StatusMessage>? status = null)
    {
        // one scan, filters are OR-ed: default-named devices, and renamed ones ("name <new>") by the Nordic UART service
        var options = new RequestDeviceOptions { AcceptAllDevices = false };
        options.Filters.Add(new BluetoothLEScanFilter { NamePrefix = namePrefix });
        var service = new BluetoothLEScanFilter();
        service.Services.Add(BluetoothTransport.NusService);
        options.Filters.Add(service);
        var devices = await Bluetooth.ScanForDevicesAsync(options, ct) ?? [];
        return devices.Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id)).DistinctBy(d => d.Id).ToList();
    }
}
