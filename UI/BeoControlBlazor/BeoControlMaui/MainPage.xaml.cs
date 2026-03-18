namespace BeoControlMaui
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (DeviceInfo.Current.Platform != DevicePlatform.Android)
                return;
            // fast Quick & Dirty workaround to thest BT on Android
            var bluetoothStatus = await EnsurePermissionAsync<Permissions.Bluetooth>();
            var locationStatus = await EnsurePermissionAsync<Permissions.LocationWhenInUse>();

            if (bluetoothStatus == PermissionStatus.Granted && locationStatus == PermissionStatus.Granted)
            {
                System.Diagnostics.Debug.WriteLine("Bluetooth permissions granted.");
                return;
            }

            await DisplayAlertAsync("Hinweis", "Ohne Bluetooth- und Standortberechtigung finden wir keine Geraete.", "OK");
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
