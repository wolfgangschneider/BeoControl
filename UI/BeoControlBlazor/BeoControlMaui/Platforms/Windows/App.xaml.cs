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
        private WinForms.ContextMenuStrip? _trayMenu;
        private AppWindow? _appWindow;
        private Microsoft.UI.Xaml.Window? _win;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
        private IntPtr _windowHandle;
        private bool _exitRequested;
        private readonly List<WinForms.ToolStripItem> _trayCommandItems = new();

        [DllImport("user32.dll")] static extern bool GetCursorPos(out System.Drawing.Point pt);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MonitorInfo mi);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);

        private const int SwShow = 5;
        private const int SwRestore = 9;

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
                _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(win);
                _appWindow = AppWindow.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                        _windowHandle));

                _settings = AppSettings.Load();
                var lastWinPosition = _settings.LastWinPosition;

                _appWindow.Resize(new SizeInt32(lastWinPosition.WindowWidth, lastWinPosition.WindowHeight));

                var pos = CalcWindowsPosition(_settings);
                _appWindow.Move(new PointInt32(pos.X, pos.Y));

                _appWindow.Changed += OnAppWindowChanged;
                _appWindow.Closing += OnWindowClosing;

                _deviceService = MauiProgram.Services!.GetRequiredService<DeviceService>();

                StartTrayThread();
                StartSingleInstanceListener();
                _ = _deviceService.AutoConnectAsync();

                if (IsSilentLaunchRequested())
                {
                    _appWindow.Hide();
                }
            }
        }

        private static bool IsSilentLaunchRequested()
        {
            return Environment.GetCommandLineArgs()
                .Any(argument => string.Equals(argument, "/silent", StringComparison.OrdinalIgnoreCase));
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
                _trayMenu = menu;
                menu.Items.Add("Show", null, (_, _) =>
                {
                    GetCursorPos(out var pt);
                    _dispatcher?.TryEnqueue(() => ShowWindowAboveTray(pt));
                });
                menu.Items.Add(new WinForms.ToolStripSeparator());

                var muteItem = new WinForms.ToolStripMenuItem("MUTE", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                var aTapeItem = new WinForms.ToolStripMenuItem("A.TAPE", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                var radioItem = new WinForms.ToolStripMenuItem("RADIO", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                var phonoItem = new WinForms.ToolStripMenuItem("PHONO", null, (_, _) =>
                {
                    _deviceService?.SendCommand("mute");
                });
                _trayCommandItems.Clear();
                _trayCommandItems.AddRange([muteItem, aTapeItem, radioItem, phonoItem]);
                menu.Items.AddRange([muteItem, aTapeItem, radioItem, phonoItem]);

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

                UpdateTrayConnectionState(deviceService?.IsConnected ?? false);

                if (deviceService is not null)
                    deviceService.OnStatusChanged += _ => UpdateTrayConnectionState(deviceService.IsConnected);

                _trayContext = new WinForms.ApplicationContext();
                WinForms.Application.Run(_trayContext);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "TrayIconThread";
            thread.Start();
        }

        private void UpdateTrayConnectionState(bool connected)
        {
            if (_trayMenu is { IsDisposed: false, IsHandleCreated: true, InvokeRequired: true })
            {
                _trayMenu.BeginInvoke((Action)(() => UpdateTrayConnectionStateCore(connected)));
                return;
            }

            UpdateTrayConnectionStateCore(connected);
        }

        private void UpdateTrayConnectionStateCore(bool connected)
        {
            if (_trayIcon is null) return;

            _trayIcon.Icon?.Dispose();
            _trayIcon.Icon = LoadIcon(connected);
            _trayIcon.Text = connected ? "BeoControl — Connected" : "BeoControl — Disconnected";

            foreach (var item in _trayCommandItems)
                item.Enabled = connected;
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

            var pos = CalcWindowsPosition(_settings);
            _appWindow.Move(new PointInt32(pos.X, pos.Y));
            BringWindowToForeground();
        }

        private PointInt32 CalcWindowsPosition(AppSettings? settings)
        {
            if (settings is not null)
            {
                var lastWinPosition = settings.LastWinPosition;
                if (lastWinPosition.WindowX != 0 || lastWinPosition.WindowY != 0)
                    return new PointInt32(lastWinPosition.WindowX, lastWinPosition.WindowY);

                return CalcDefaultWindowsPosition(lastWinPosition.WindowWidth, lastWinPosition.WindowHeight);
            }

            return CalcDefaultWindowsPosition(
                AppSettings.WindowGeometry.DefaultWindowWidth,
                AppSettings.WindowGeometry.DefaultWindowHeight);
        }

        private PointInt32 CalcDefaultWindowsPosition(int winW, int winH)
        {

            const uint MONITOR_DEFAULTTOPRIMARY = 1;

            // Any point works; Windows ignores it when using DEFAULTTOPRIMARY
            System.Drawing.Point dummy = new System.Drawing.Point { X = 0, Y = 0 };

            IntPtr hMonitor = MonitorFromPoint(dummy, MONITOR_DEFAULTTOPRIMARY);

            var mi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(hMonitor, ref mi);
            var work = mi.rcWork;


            return new PointInt32(work.Width - winW, work.Bottom - winH);
        }

        private void BringWindowToForeground()
        {
            if (_windowHandle == IntPtr.Zero) return;

            _appWindow?.Show();
            ShowWindow(_windowHandle, IsIconic(_windowHandle) ? SwRestore : SwShow);
            BringWindowToTop(_windowHandle);
            SetForegroundWindow(_windowHandle);
            _win?.Activate();
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs e)
        {
            if (_settings is null || (!e.DidSizeChange && !e.DidPositionChange)) return;

            var lastWinPosition = _settings.LastWinPosition;

            if (e.DidSizeChange)
            {
                lastWinPosition.WindowWidth = sender.Size.Width;
                lastWinPosition.WindowHeight = sender.Size.Height;
            }

            if (e.DidPositionChange)
            {
                lastWinPosition.WindowX = sender.Position.X;
                lastWinPosition.WindowY = sender.Position.Y;
            }

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
