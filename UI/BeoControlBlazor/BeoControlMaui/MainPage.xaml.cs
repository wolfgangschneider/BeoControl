using Microsoft.AspNetCore.Components.WebView;

namespace BeoControlMaui
{
    public partial class MainPage : ContentPage
    {
        private bool _permissionsChecked;
        private Window? _setupWindow;
        private long _lastDragTick;

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
                var message = args.TryGetWebMessageAsString();
                if (message == "beocontrol:open-setup")
                    Dispatcher.Dispatch(OpenSetupWindow);
                // mouse down on the silver remote surface: move the window while the button is held
                else if (message == "beocontrol:drag" && Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                {
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                    // a second press within the double-click time hides the window to the tray
                    // (WM_CLOSE -> App hides instead of closing, like the X)
                    var now = Environment.TickCount64;
                    var doubleClick = now - _lastDragTick <= Win32.GetDoubleClickTime();
                    _lastDragTick = doubleClick ? 0 : now;
                    if (doubleClick)
                    {
                        Win32.SendMessage(hWnd, Win32.WM_CLOSE, 0, 0);
                        return;
                    }
                    StartMove(Dispatcher, hWnd);
                }
                else if (message?.StartsWith("beocontrol:shape:") == true && Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window shaped)
                    ApplyShape(WinRT.Interop.WindowNative.GetWindowHandle(shaped), message["beocontrol:shape:".Length..]);
            };
#endif
        }

#if WINDOWS
        /// <summary>
        /// Moves the window with the cursor until the left button is released. Own loop instead of the
        /// title bar move (WM_NCLBUTTONDOWN): that one snaps to screen edges and shows the snap layouts.
        /// </summary>
        internal static void StartMove(IDispatcher dispatcher, IntPtr hWnd)
        {
            Win32.GetCursorPos(out var start);
            Win32.GetWindowRect(hWnd, out var window);
            var timer = dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(10);
            timer.Tick += (_, _) =>
            {
                if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) == 0)
                {
                    timer.Stop();
                    return;
                }
                Win32.GetCursorPos(out var now);
                Win32.SetWindowPos(hWnd, IntPtr.Zero, window.Left + now.X - start.X, window.Top + now.Y - start.Y, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            };
            timer.Start();
        }

        private sealed record ShapeRect(double X, double Y, double W, double H, double R);
        private sealed record Shape(double Dpr, ShapeRect[] Rects);

        /// <summary>Cuts the borderless window to the remote (+ status bar): no window background around it.</summary>
        private static void ApplyShape(IntPtr hWnd, string json)
        {
            var shape = System.Text.Json.JsonSerializer.Deserialize<Shape>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (shape is null || shape.Rects.Length == 0)
                return;

            // the region is in window coordinates, the page measures from the client area: add the
            // (invisible) frame between window corner and client origin
            Win32.GetWindowRect(hWnd, out var window);
            var client = new System.Drawing.Point(0, 0);
            Win32.ClientToScreen(hWnd, ref client);
            int dx = client.X - window.Left, dy = client.Y - window.Top;

            IntPtr region = IntPtr.Zero;
            foreach (var r in shape.Rects)
            {
                int Px(double v) => (int)Math.Round(v * shape.Dpr);
                int diameter = Px(r.R * 2);
                var part = Win32.CreateRoundRectRgn(dx + Px(r.X), dy + Px(r.Y), dx + Px(r.X + r.W) + 1, dy + Px(r.Y + r.H) + 1, diameter, diameter);
                if (region == IntPtr.Zero)
                {
                    region = part;
                    continue;
                }
                Win32.CombineRgn(region, region, part, Win32.RGN_OR);
                Win32.DeleteObject(part);
            }
            Win32.SetWindowRgn(hWnd, region, true);   // the window owns the region from here on, no DeleteObject
        }
#endif

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
                    // borderless like the remote: no title bar, no Windows 11 border line or rounded corners;
                    // the red dot closes it, dragging the display or the nameplate moves it (SetupPage)
                    if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                    {
                        presenter.SetBorderAndTitleBar(false, false);
                        presenter.IsResizable = false;
                        presenter.IsMaximizable = false;
                    }
                    var setupHandle = WinRT.Interop.WindowNative.GetWindowHandle(native);
                    uint corner = Win32.DWMWCP_DONOTROUND, border = Win32.DWMWA_COLOR_NONE;
                    Win32.DwmSetWindowAttribute(setupHandle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
                    Win32.DwmSetWindowAttribute(setupHandle, Win32.DWMWA_BORDER_COLOR, ref border, sizeof(uint));

                    if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window main)
                        PlaceNextTo(native.AppWindow, main.AppWindow);
                    Win32.ShowWindow(setupHandle, Win32.SW_SHOW);

                    // WinUI keeps a thin frame line around borderless windows: cut the window to its client area
                    Win32.GetWindowRect(setupHandle, out var frame);
                    var client = new System.Drawing.Point(0, 0);
                    Win32.ClientToScreen(setupHandle, ref client);
                    int dx = client.X - frame.Left, dy = client.Y - frame.Top;
                    var size = native.AppWindow.ClientSize;
                    Win32.SetWindowRgn(setupHandle, Win32.CreateRoundRectRgn(dx, dy, dx + size.Width + 1, dy + size.Height + 1, 0, 0), true);
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
