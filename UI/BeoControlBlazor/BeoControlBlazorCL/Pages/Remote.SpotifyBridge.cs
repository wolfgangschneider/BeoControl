using BeoControl.Interfaces;

namespace BeoControlBlazorCL.Pages;

public partial class Remote
{
    private sealed class SpotifyBridge(Remote owner)
    {
        public string Song { get; private set; } = string.Empty;
        public string Interpret { get; private set; } = string.Empty;
        public string? ConnectedDeviceName { get; private set; }

        public bool IsSourceSelected() =>
            string.Equals(owner._currentSource?.AddIn, SpotifyAddInName, StringComparison.Ordinal);

        public async Task<bool> HandleCommandAsync(BeoCommand command)
        {
            if (!owner.DeviceService.Settings.SpotifyEnabled || !IsSourceSelected())
                return false;

            if (command.Id == CommandId.Menu)
            {
                await OpenAsync();
                return true;
            }

            var spotifyCommand = TryMapCommand(command);
            if (spotifyCommand is null)
                return false;

            var executed = await ExecuteAsync(spotifyCommand);
            if (!executed)
                return false;

            await RefreshPlaybackAsync();
            return true;
        }

        private static string? TryMapCommand(BeoCommand command) => command.Id switch
        {
            CommandId.Left => "Previous",
            CommandId.Right => "Next",
            CommandId.Stop => "Pause",
            CommandId.Play => "Play",
            CommandId.Go => "Play",
            _ => null
        };

        private Task OpenAsync() =>
            owner.LaunchSpotifyService.OpenAsync(owner.DeviceService.Settings.SpotifyLaunchMode);

        private Task<bool> ExecuteAsync(string command) =>
            owner.LaunchSpotifyService.ExecuteSpotifyCommandAsync(
                command,
                owner.DeviceService.Settings.SpotifyPreferredDeviceName);

        public async Task RefreshPlaybackAsync()
        {
            ConnectedDeviceName = await owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(
                owner.DeviceService.Settings.SpotifyPreferredDeviceName);
            var nowPlaying = await owner.LaunchSpotifyService.GetSpotifyNowPlayingTextAsync(
                owner.DeviceService.Settings.SpotifyPreferredDeviceName);
            if (nowPlaying is null)
                return;

            Song = nowPlaying.Value.Song;
            Interpret = nowPlaying.Value.Interpret;
        }

        public async Task EnsureConnectionStateAsync()
        {
            try
            {
                if (!owner.DeviceService.Settings.SpotifyEnabled)
                {
                    ConnectedDeviceName = null;
                    await owner.InvokeAsync(owner.StateHasChanged);
                    return;
                }

                ConnectedDeviceName = await owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(
                    owner.DeviceService.Settings.SpotifyPreferredDeviceName);
                await owner.InvokeAsync(owner.StateHasChanged);
            }
            catch
            {
                ConnectedDeviceName = null;
                await owner.InvokeAsync(owner.StateHasChanged);
            }
        }

        public void SyncSourceAddIn()
        {
            foreach (var sourceCommand in SourceCommands)
                sourceCommand.AddIn = null;

            if (!owner.DeviceService.Settings.SpotifyEnabled)
                return;

            var triggerCommand = BeoCommands.Find(owner.DeviceService.Settings.SpotifyTriggerCommand);
            if (triggerCommand?.Category == CommandCategory.Source)
                triggerCommand.AddIn = SpotifyAddInName;
        }
    }
}
