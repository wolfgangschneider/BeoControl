using Microsoft.Win32;

namespace BeoControlBlazorServices;

public sealed class WindowsAutostartRegistrationService : IAutostartRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BeoControl";
    private const string SilentArgument = "/silent";

    public bool IsSupported => true;

    public bool IsEnabled()
    {
        using RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false)
            ?? throw new InvalidOperationException($"Unable to open registry key '{RunKeyPath}'.");

        string? currentValue = runKey.GetValue(ValueName) as string;
        return string.Equals(currentValue, BuildAutostartCommand(GetExecutablePath()), StringComparison.Ordinal);
    }

    public void SetEnabled(bool isEnabled)
    {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open registry key '{RunKeyPath}'.");

        if (isEnabled)
        {
            runKey.SetValue(ValueName, BuildAutostartCommand(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        runKey.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("The current process path is unavailable.");
    }

    private static string BuildAutostartCommand(string executablePath)
    {
        return $"\"{executablePath}\" {SilentArgument}";
    }
}
