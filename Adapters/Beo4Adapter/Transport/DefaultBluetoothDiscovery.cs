using System.Collections.Concurrent;
using BeoControl.Interfaces;
using InTheHand.Bluetooth;

namespace Beo4Adapter.Transport;

/// <summary>
/// Passive BLE discovery for platforms where scanning works without Apple's picker.
/// One advertisement scan, filtered here: the library's service filter does not filter on Windows.
/// </summary>
internal sealed class DefaultBluetoothDiscovery : IBluetoothDiscovery
{
    private static readonly TimeSpan ScanTime = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyCollection<BluetoothDevice>> DiscoverAsync(
        string namePrefix,
        CancellationToken ct,
        Action<StatusMessage>? status = null)
    {
        // default names ("Beo4Remote…") or renamed devices ("name <new>") advertising the Nordic UART service
        var found = new ConcurrentDictionary<string, BluetoothDevice>();
        void OnAdvertisement(object? sender, BluetoothAdvertisingEvent e)
        {
            var match = e.Name?.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) == true
                        || e.Uuids?.Contains(BluetoothTransport.NusService) == true;
            if (match && e.Device is { } device && !string.IsNullOrWhiteSpace(device.Id))
                found.TryAdd(device.Id, device);
        }

        Bluetooth.AdvertisementReceived += OnAdvertisement;
        BluetoothLEScan? scan = null;
        try
        {
            scan = await Bluetooth.RequestLEScanAsync(new BluetoothLEScanOptions { AcceptAllAdvertisements = true });
            if (scan is not null)
                await Task.Delay(ScanTime, ct);
        }
        catch (PlatformNotSupportedException) { }
        catch (NotImplementedException) { }
        finally
        {
            scan?.Stop();
            Bluetooth.AdvertisementReceived -= OnAdvertisement;
        }
        if (scan is not null)
            return found.Values.ToList();

        // platforms without advertisement scanning: device scan by name prefix (renamed devices are not found)
        var options = new RequestDeviceOptions { AcceptAllDevices = false };
        options.Filters.Add(new BluetoothLEScanFilter { NamePrefix = namePrefix });
        return await Bluetooth.ScanForDevicesAsync(options, ct) ?? [];
    }
}
