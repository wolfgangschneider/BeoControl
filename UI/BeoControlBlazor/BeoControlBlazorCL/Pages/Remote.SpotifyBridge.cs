using BeoControl.Interfaces;
using BeoControlBlazorServices;

namespace BeoControlBlazorCL.Pages;

public partial class Remote
{
    private sealed class SpotifyBridge(Remote owner)
    {
        public string Error { get; private set; } = string.Empty;
        public string Song { get; private set; } = string.Empty;
        public string Interpret { get; private set; } = string.Empty;
        public string? ConnectedDeviceName { get; private set; }

        public bool IsSourceSelected() =>
            string.Equals(owner._currentSource?.AddIn, SpotifyAddInName, StringComparison.Ordinal);


        public async Task<bool> HandleCommandAsync2(BeoCommand command)
        {

            if (command.Cmd == owner.DeviceService.Settings.SpotifyTriggerCommand)
                _ = ExecuteAsync("Play");


            if (!owner.DeviceService.Settings.SpotifyEnabled || !IsSourceSelected())
                return false;


            if (command.Id == CommandId.Menu)
            {
                await OpenAsync();
                return true;
            }

            if (command.Id == CommandId.AllOff)
                _ = ExecuteAsync("Pause");

            if (command.Category == CommandCategory.Source)
            {
                if (command.Cmd != owner.DeviceService.Settings.SpotifyTriggerCommand)
                    _ = ExecuteAsync("Pause");
            }

            var spotifyCommand = TryMapCommand(command);
            if (spotifyCommand is not null)
            {
                var executed = await ExecuteAsync(spotifyCommand);
                if (executed)
                    await RefreshPlaybackAsync();

                return true;
            }

            await RefreshPlaybackAsync();
            return false;



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
            owner.LaunchSpotifyService.ExecuteSpotifyCommandAsync(command, owner.DeviceService.Settings.SpotifyPreferredDeviceName);

        private Task StopPlaybackPollingAsync() =>
            owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(null);

        public void SubscribeNowPlayingChanged()
        {
            if (!owner.LaunchSpotifyService.SupportsSpotifyNowPlayingNotifications)
                return;

            owner.LaunchSpotifyService.NowPlayingChanged += OnNowPlayingChanged;
        }

        public void UnsubscribeNowPlayingChanged()
        {
            if (!owner.LaunchSpotifyService.SupportsSpotifyNowPlayingNotifications)
                return;

            owner.LaunchSpotifyService.NowPlayingChanged -= OnNowPlayingChanged;
        }

        public async Task RefreshPlaybackAsync()
        {
            if (!IsSourceSelected())
            {
                await StopPlaybackPollingAsync();
                ConnectedDeviceName = null;
                Song = string.Empty;
                Interpret = string.Empty;
                return;
            }

            ConnectedDeviceName = await owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(
                owner.DeviceService.Settings.SpotifyPreferredDeviceName);
            var nowPlaying = await owner.LaunchSpotifyService.GetSpotifyNowPlayingTextAsync(
                owner.DeviceService.Settings.SpotifyPreferredDeviceName);
            ApplyNowPlaying(nowPlaying);
        }

        public string SpotifyConnectionLabel()
        {
            if (!owner.DeviceService.Settings.SpotifyEnabled)
                return "SPOTIFY DISABLED";

            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            if (!string.IsNullOrWhiteSpace(ConnectedDeviceName))
                return $"{ConnectedDeviceName}  |  SPOTIFY";

            if (!string.IsNullOrWhiteSpace(owner.DeviceService.Settings.SpotifyPreferredDeviceName))
                return $"{owner.DeviceService.Settings.SpotifyPreferredDeviceName}  |  SPOTIFY";

            return "SPOTIFY DISCONNECTED";
        }

        public bool IsSpotifyConnected() =>
            owner.DeviceService.Settings.SpotifyEnabled &&
            (!string.IsNullOrWhiteSpace(ConnectedDeviceName)
                || (!IsSourceSelected()
                    && !string.IsNullOrWhiteSpace(owner.DeviceService.Settings.SpotifyPreferredDeviceName)));

        public async Task EnsureConnectionStateAsync()
        {
            Error = string.Empty;
            try
            {
                if (!owner.DeviceService.Settings.SpotifyEnabled || !IsSourceSelected())
                {
                    await StopPlaybackPollingAsync();
                    ConnectedDeviceName = null;
                    Song = string.Empty;
                    Interpret = string.Empty;
                    await owner.InvokeAsync(owner.StateHasChanged);
                    return;
                }

                ConnectedDeviceName = await owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(
                    owner.DeviceService.Settings.SpotifyPreferredDeviceName);
                await owner.InvokeAsync(owner.StateHasChanged);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                // ConnectedDeviceName = ex.Message;
                await owner.InvokeAsync(owner.StateHasChanged);
            }
        }

        public void ApplySourceAddIn()
        {
            if (!owner.DeviceService.Settings.SpotifyEnabled)
                return;

            var triggerCommand = BeoCommands.Get(owner.DeviceService.Settings.SpotifyTriggerCommand);
            if (triggerCommand?.Category == CommandCategory.Source)
                triggerCommand.AddIn = SpotifyAddInName;
        }

        private async void OnNowPlayingChanged(object? sender, SpotifyNowPlayingChangedEventArgs e)
        {
            if (!owner.DeviceService.Settings.SpotifyEnabled || !IsSourceSelected())
                return;

            ApplyNowPlaying(e.NowPlayingText);
            await owner.InvokeAsync(owner.StateHasChanged);
        }

        private void ApplyNowPlaying((string Song, string Interpret)? nowPlaying)
        {
            if (nowPlaying is null)
            {
                Song = string.Empty;
                Interpret = string.Empty;
                return;
            }

            Song = nowPlaying.Value.Song;
            Interpret = nowPlaying.Value.Interpret;
        }
    }
}
