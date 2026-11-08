using Microsoft.AspNetCore.Components.WebView;

namespace BeoControlMaui
{
    public partial class MainPage : ContentPage
    {
        private bool _permissionsChecked;
        private Window? _setupWindow;

        [System.Diagnostics.CodeAnalysis.DynamicDependency(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All,
            typeof(Microsoft.AspNetCore.Components.Web.HeadOutlet))]   // only referenced via x:Type in XAML; keep ctor and [Inject] setters in trimmed Release builds
        public MainPage()
        {
            InitializeComponent();
        }

        private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        {
#if WINDOWS
            // Gear posts 'beocontrol:open-setup' in WebView2 (window.open would go to the external browser)
            e.WebView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (args.TryGetWebMessageAsString() == "beocontrol:open-setup")
                    Dispatcher.Dispatch(OpenSetupWindow);
            };
#endif
        }

#if WINDOWS
        private void OpenSetupWindow()
        {
            if (_setupWindow is not null)
            {
                Application.Current?.ActivateWindow(_setupWindow);
                return;
            }

            _setupWindow = new Window(new SetupPage()) { Title = "Setup", Width = 700, Height = 460 };
            // MauiProgram hides every new window (silent start), so show this one explicitly
            _setupWindow.Created += (_, _) => Dispatcher.Dispatch(() =>
            {
                if (_setupWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                {
                    if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window main)
                        PlaceNextTo(native.AppWindow, main.AppWindow);
                    Win32.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(native), Win32.SW_SHOW);
                }
            });
            _setupWindow.Destroying += (_, _) => _setupWindow = null;
            Application.Current?.OpenWindow(_setupWindow);
        }

        // Left of the main window (it sits at the right screen edge), else right of it; bottom aligned, kept inside the work area
        private static void PlaceNextTo(Microsoft.UI.Windowing.AppWindow setup, Microsoft.UI.Windowing.AppWindow main)
        {
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(main.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
            var x = main.Position.X - setup.Size.Width;
            if (x < work.X) x = main.Position.X + main.Size.Width;
            var y = Math.Clamp(main.Position.Y + main.Size.Height - setup.Size.Height, work.Y, work.Y + work.Height - setup.Size.Height);
            setup.Move(new Windows.Graphics.PointInt32(x, y));
        }
#endif

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
                var locationStatus = OperatingSystem.IsAndroidVersionAtLeast(31)
                    ? PermissionStatus.Granted   // Android 12+: BLUETOOTH_SCAN with neverForLocation, no location needed
                    : await EnsurePermissionAsync<Permissions.LocationWhenInUse>();

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
