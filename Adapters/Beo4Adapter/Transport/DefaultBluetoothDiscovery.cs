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
        var filteredDevices = await ScanWithNamePrefixAsync(namePrefix, ct);
        var matchingDevices = EnumerateMatchingDevices(filteredDevices, namePrefix).ToList();
        if (matchingDevices.Count > 0)
            return matchingDevices;

        // Some peripherals omit or delay the device name, so retry by advertised service UUID.
        status?.Invoke(new StatusMessage(StatusType.Working, "○ No Beo4Remote name match found; retrying with Nordic UART service UUID…", StatusKind.Discovery));
        var serviceDevices = await ScanWithServiceFilterAsync(ct);
        matchingDevices = EnumerateMatchingDevices(serviceDevices, namePrefix).ToList();
        if (matchingDevices.Count > 0)
            return matchingDevices;

        return serviceDevices
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.Id))
            .ToList();
    }

    private static async Task<IReadOnlyCollection<BluetoothDevice>> ScanWithNamePrefixAsync(string namePrefix, CancellationToken ct)
    {
        var filter = new BluetoothLEScanFilter { NamePrefix = namePrefix };
        var options = BuildOptions(filter);
        return await Bluetooth.ScanForDevicesAsync(options, ct) ?? [];
    }

    private static async Task<IReadOnlyCollection<BluetoothDevice>> ScanWithServiceFilterAsync(CancellationToken ct)
    {
        var filter = new BluetoothLEScanFilter();
        filter.Services.Add(BluetoothTransport.NusService);
        var options = BuildOptions(filter);
        return await Bluetooth.ScanForDevicesAsync(options, ct) ?? [];
    }

    private static RequestDeviceOptions BuildOptions(BluetoothLEScanFilter filter)
    {
        var options = new RequestDeviceOptions();
        options.Filters.Add(filter);
        options.AcceptAllDevices = false;
        return options;
    }

    private static IEnumerable<BluetoothDevice> EnumerateMatchingDevices(IEnumerable<BluetoothDevice> devices, string namePrefix)
    {
        foreach (var device in devices)
        {
            if (device is null)
                continue;

            if (device.Name?.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) == true)
                yield return device;
        }
    }
}
