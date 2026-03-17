using BeoControlBlazor.Services;

using BeoControlBlazorServices;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

using System.Runtime.InteropServices;

using Windows.Graphics;

using WinForms = System.Windows.Forms;

namespace BeoControlMaui.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        private const string MutexName = "BeoControl_SingleInstance_Mutex";
        private const string EventName = "BeoControl_SingleInstance_Event";
        private static Mutex? _instanceMutex;
        private EventWaitHandle? _showEvent;

        private WinForms.NotifyIcon? _trayIcon;
        private WinForms.ApplicationContext? _trayContext;
        private AppWindow? _appWindow;
        private Microsoft.UI.Xaml.Window? _win;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
        private bool _exitRequested;

        [DllImport("user32.dll")] static extern bool GetCursorPos(out System.Drawing.Point pt);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MonitorInfo mi);

        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public int cbSize;
            public System.Drawing.Rectangle rcMonitor;
            public System.Drawing.Rectangle rcWork;
            public uint dwFlags;
        }

        public App()
        {
            _instanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Signal the running instance to come to the foreground, then exit
                try { EventWaitHandle.OpenExisting(EventName).Set(); } catch { }
                Environment.Exit(0);
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        private AppSettings? _settings;
        private DeviceService? _deviceService;
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

                _settings = AppSettings.Load();
                int w = _settings.WindowWidth > 0 ? _settings.WindowWidth : RemoteWindow.Width;
                int h = _settings.WindowHeight > 0 ? _settings.WindowHeight : RemoteWindow.Height;
                _appWindow.Resize(new SizeInt32(w, h));
                _appWindow.Changed += OnAppWindowChanged;
                _appWindow.Closing += OnWindowClosing;

                _deviceService = MauiProgram.Services!.GetRequiredService<DeviceService>();

                StartTrayThread();
                StartSingleInstanceListener();

                // Start minimized to tray
                _appWindow.Hide();
            }
        }

        private void StartSingleInstanceListener()
        {
            var thread = new Thread(() =>
            {
                while (_showEvent?.WaitOne() == true)
                {
                    _dispatcher?.TryEnqueue(() =>
                    {
                        GetCursorPos(out var pt);
                        ShowWindowAboveTray(pt);
                    });
                }
            });
            thread.IsBackground = true;
            thread.Name = "SingleInstanceListener";
            thread.Start();
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
                {
                    GetCursorPos(out var pt);
                    _dispatcher?.TryEnqueue(() => ShowWindowAboveTray(pt));
                });
                menu.Items.Add(new WinForms.ToolStripSeparator());

                menu.Items.Add("MUTE", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                menu.Items.Add("A.TAPE", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                menu.Items.Add("RADIO", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                menu.Items.Add("PHONO", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });

                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("Exit", null, (_, _) =>
                    _dispatcher?.TryEnqueue(ExitApp));

                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.MouseClick += (_, e) =>
                {
                    if (e.Button != WinForms.MouseButtons.Left) return;
                    GetCursorPos(out var pt);
                    _dispatcher?.TryEnqueue(() => ShowWindowAboveTray(pt));
                };

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

        private void ShowWindowAboveTray(System.Drawing.Point cursor)
        {
            if (_appWindow is null) return;

            var hMon = MonitorFromPoint(cursor, 2 /*MONITOR_DEFAULTTONEAREST*/);
            var mi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(hMon, ref mi);
            var work = mi.rcWork;

            int winW = RemoteWindow.Width;
            int winH = RemoteWindow.Height;

            // Center on cursor horizontally, sit just above the taskbar
            int x = Math.Clamp(cursor.X - winW / 2, work.Left, work.Right - winW);
            int y = work.Bottom - winH;

            _appWindow.Move(new PointInt32(x, y));
            _appWindow.Show();
            _win?.Activate();
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs e)
        {
            if (!e.DidSizeChange || _settings is null) return;
            _settings.WindowWidth = sender.Size.Width;
            _settings.WindowHeight = sender.Size.Height;
            _settings.Save();
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
            _showEvent?.Dispose();
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
