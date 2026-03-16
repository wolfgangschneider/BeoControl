using BeoControlBlazorServices;

using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

using Windows.Graphics;

using WinForms = System.Windows.Forms;

namespace BeoControlMaui.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        private WinForms.NotifyIcon? _trayIcon;
        private WinForms.ApplicationContext? _trayContext;
        private AppWindow? _appWindow;
        private Microsoft.UI.Xaml.Window? _win;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
        private bool _exitRequested;

        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            if (Application.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            {
                _win = win;
                _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                _appWindow = AppWindow.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                        WinRT.Interop.WindowNative.GetWindowHandle(win)));

                _appWindow.Resize(new SizeInt32(RemoteWindow.Width, RemoteWindow.Height));
                _appWindow.Closing += OnWindowClosing;

                StartTrayThread();

                // Start minimized to tray
                _appWindow.Hide();
            }
        }

        private void StartTrayThread()
        {
            var deviceService = MauiProgram.Services?.GetService(typeof(DeviceService)) as DeviceService;

            var thread = new Thread(() =>
            {
                WinForms.Application.EnableVisualStyles();

                _trayIcon = new WinForms.NotifyIcon
                {
                    Visible = true,
                    Text = "BeoControl",
                    Icon = LoadIcon(connected: deviceService?.IsConnected ?? false),
                };

                var menu = new WinForms.ContextMenuStrip();
                menu.Items.Add("Show", null, (_, _) =>
                    _dispatcher?.TryEnqueue(ShowWindow));
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("Exit", null, (_, _) =>
                    _dispatcher?.TryEnqueue(ExitApp));

                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.Click += (_, _) => _dispatcher?.TryEnqueue(ShowWindow);

                if (deviceService is not null)
                    deviceService.OnStatusChanged += _ => UpdateTrayIcon(deviceService.IsConnected);

                _trayContext = new WinForms.ApplicationContext();
                WinForms.Application.Run(_trayContext);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "TrayIconThread";
            thread.Start();
        }

        private void UpdateTrayIcon(bool connected)
        {
            if (_trayIcon is null) return;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Icon = LoadIcon(connected);
            _trayIcon.Text = connected ? "BeoControl — Connected" : "BeoControl — Disconnected";
        }

        private static System.Drawing.Icon? LoadIcon(bool connected)
        {
            var file = connected ? "tray_connected.ico" : "tray_disconnected.ico";
            var path = Path.Combine(AppContext.BaseDirectory, file);
            return File.Exists(path) ? new System.Drawing.Icon(path) : null;
        }

        private void ShowWindow()
        {
            _appWindow?.Show();
            _win?.Activate();
        }

        private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
        {
            if (_exitRequested) return;
            e.Cancel = true;
            _appWindow?.Hide();
        }

        private void ExitApp()
        {
            _exitRequested = true;
            _trayIcon?.Dispose();
            _trayContext?.ExitThread();
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
