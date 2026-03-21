namespace BeoControlMaui
{
    public partial class MainPage : ContentPage
    {
        private bool _permissionsChecked;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_permissionsChecked)
                return;

            _permissionsChecked = true;

            var platform = DeviceInfo.Current.Platform;

            if (platform == DevicePlatform.Android)
            {
                var bluetoothStatus = await EnsurePermissionAsync<Permissions.Bluetooth>();
                var locationStatus = await EnsurePermissionAsync<Permissions.LocationWhenInUse>();

                if (bluetoothStatus == PermissionStatus.Granted && locationStatus == PermissionStatus.Granted)
                {
                    System.Diagnostics.Debug.WriteLine("Bluetooth permissions granted.");
                    return;
                }

                await DisplayAlertAsync("Hinweis", "Ohne Bluetooth- und Standortberechtigung finden wir keine Geraete.", "OK");
                return;
            }

            if (platform == DevicePlatform.iOS || platform == DevicePlatform.MacCatalyst)
                System.Diagnostics.Debug.WriteLine("Apple Bluetooth permission is handled by the OS when BLE is first accessed.");
        }

        private static async Task<PermissionStatus> EnsurePermissionAsync<TPermission>()
            where TPermission : Permissions.BasePermission, new()
        {
            var status = await Permissions.CheckStatusAsync<TPermission>();
            if (status == PermissionStatus.Granted)
                return status;

            return await Permissions.RequestAsync<TPermission>();
        }
    }
}
