namespace Beo4Adapter.Transport;

internal static class BluetoothDiscoveryFactory
{
    public static IBluetoothDiscovery Create()
    {
        // On Apple platforms the system picker is the reliable path on this codebase's target hardware.
        if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS())
            return new ApplePickerBluetoothDiscovery();

        return new DefaultBluetoothDiscovery();
    }
}
