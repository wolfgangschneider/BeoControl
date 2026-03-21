namespace BeoControlBlazorServices;

public interface IAutostartRegistrationService
{
    bool IsSupported { get; }

    bool IsEnabled();

    void SetEnabled(bool isEnabled);
}
