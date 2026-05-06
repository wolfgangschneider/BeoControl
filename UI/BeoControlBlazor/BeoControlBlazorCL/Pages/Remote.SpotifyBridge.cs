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


        private async Task<bool> ExecuteAsync(string command)
        {
            try
            {
                Error = string.Empty;
                return await owner.LaunchSpotifyService.ExecuteSpotifyCommandAsync(
                    command,
                    owner.DeviceService.Settings.SpotifyPreferredDeviceName);
            }
            catch (Exception ex)
            {
                await SetErrorStateAsync(ex.Message);
                return false;
            }
        }

        private Task StopPlaybackPollingAsync() =>
            owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(null);

        public void SubscribeNowPlayingChanged()
        {
            owner.LaunchSpotifyService.NowPlayingChanged += OnNowPlayingChanged;
        }

        public void UnsubscribeNowPlayingChanged()
        {
            owner.LaunchSpotifyService.NowPlayingChanged -= OnNowPlayingChanged;
        }

        public async Task RefreshPlaybackAsync()
        {
            Error = string.Empty;
            if (!IsSourceSelected())
            {
                await StopPlaybackPollingAsync();
                ConnectedDeviceName = null;
                Song = string.Empty;
                Interpret = string.Empty;
                return;
            }

            try
            {
                ConnectedDeviceName = await owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(
                    owner.DeviceService.Settings.SpotifyPreferredDeviceName);
                var nowPlaying = await owner.LaunchSpotifyService.GetSpotifyNowPlayingTextAsync(
                    owner.DeviceService.Settings.SpotifyPreferredDeviceName);
                ApplyNowPlaying(nowPlaying);
            }
            catch (Exception ex)
            {
                await SetErrorStateAsync(ex.Message);
            }
        }

        public string SpotifyConnectionLabel()
        {
            if (!owner.DeviceService.Settings.SpotifyEnabled)
                return "SPOTIFY DISABLED";

            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            if (!string.IsNullOrWhiteSpace(ConnectedDeviceName))
                return $"{ConnectedDeviceName}  |  SPOTIFY";

            return "SPOTIFY DISCONNECTED";
        }

        public bool IsSpotifyConnected() =>
            owner.DeviceService.Settings.SpotifyEnabled &&
            string.IsNullOrWhiteSpace(Error) &&
            !string.IsNullOrWhiteSpace(ConnectedDeviceName);

        public async Task EnsureConnectionStateAsync()
        {
            Error = string.Empty;
            if (!owner.DeviceService.Settings.SpotifyEnabled)
            {
                await StopPlaybackPollingAsync();
                ConnectedDeviceName = null;
                Song = string.Empty;
                Interpret = string.Empty;
                await owner.InvokeAsync(owner.StateHasChanged);
                return;
            }

            if (!IsSourceSelected())
            {
                await StopPlaybackPollingAsync();
                Song = string.Empty;
                Interpret = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(owner.DeviceService.Settings.SpotifyPreferredDeviceName))
            {
                ConnectedDeviceName = null;
                await owner.InvokeAsync(owner.StateHasChanged);
                return;
            }

            try
            {
                ConnectedDeviceName = await owner.LaunchSpotifyService.GetSpotifyConnectedDeviceNameAsync(
                    owner.DeviceService.Settings.SpotifyPreferredDeviceName);
                await owner.InvokeAsync(owner.StateHasChanged);
            }
            catch (Exception ex)
            {
                await SetErrorStateAsync(ex.Message);
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

        private async Task SetErrorStateAsync(string message)
        {
            await StopPlaybackPollingAsync();
            ConnectedDeviceName = null;
            Song = string.Empty;
            Interpret = string.Empty;
            Error = message;
        }
    }
}
