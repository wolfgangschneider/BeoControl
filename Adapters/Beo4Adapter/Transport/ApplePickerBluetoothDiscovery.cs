using BeoControl.Interfaces;
using InTheHand.Bluetooth;

namespace Beo4Adapter.Transport;

/// <summary>
/// Uses Apple's system chooser instead of passive scanning because picker-based discovery works reliably on macOS/iOS here.
/// The picker UI itself is owned by Apple; we only narrow what it can show.
/// </summary>
internal sealed class ApplePickerBluetoothDiscovery : IBluetoothDiscovery
{
    public async Task<IReadOnlyCollection<BluetoothDevice>> DiscoverAsync(
        string namePrefix,
        CancellationToken ct,
        Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, "○ macOS is opening Bluetooth device selection…", StatusKind.Discovery));
        ct.ThrowIfCancellationRequested();

        try
        {
            var device = await Bluetooth.RequestDeviceAsync(BuildOptions(namePrefix));
            ct.ThrowIfCancellationRequested();
            return device is null ? [] : [device];
        }
        catch (NullReferenceException)
        {
            status?.Invoke(new StatusMessage(StatusType.Working, "○ Apple Bluetooth device selection failed before discovery started. Check Bluetooth permission for the host app and try again.", StatusKind.Discovery));
            return [];
        }
    }

    private static RequestDeviceOptions BuildOptions(string namePrefix)
    {
        // Constrain the Apple picker to Beo4Remote devices advertising the Nordic UART service.
        var filter = new BluetoothLEScanFilter
        {
            NamePrefix = namePrefix
        };
        filter.Services.Add(BluetoothTransport.NusService);

        var options = new RequestDeviceOptions();
        options.Filters.Add(filter);
        options.AcceptAllDevices = false;
        return options;
    }
}
