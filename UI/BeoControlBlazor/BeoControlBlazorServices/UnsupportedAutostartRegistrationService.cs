namespace BeoControlBlazorServices;

public sealed class UnsupportedAutostartRegistrationService : IAutostartRegistrationService
{
    public bool IsSupported => false;

    public bool IsEnabled() => false;

    public void SetEnabled(bool isEnabled)
    {
        throw new PlatformNotSupportedException("Windows autostart is only supported in the Windows MAUI app.");
    }
}
