using BeoControlBlazor.Services;

using BeoControlBlazorServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using Tmds.DBus;

using Velopack;

using WebKitGtk;

[assembly: InternalsVisibleTo(Connection.DynamicAssemblyName)]

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
internal sealed class Program : IHostedService
{
    private const string UpdateFeedUrl = "https://wolfgangschneider.github.io/BeoControl/updates/";

    [DllImport("libglib-2.0.so.0")]
    private static extern uint g_idle_add(IdleCallback callback, IntPtr data);

    private static readonly IdleCallback GtkIdleCallback = DispatchGtkIdle;

    private static async Task Main(string[] args)
    {
        VelopackApp.Build().Run();

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddSimpleConsole(
            options =>
            {
                options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
                options.IncludeScopes = false;
                options.SingleLine = true;
                options.TimestampFormat = "hh:mm:ss ";
            })
        .SetMinimumLevel(LogLevel.Information);

        builder.Services.Configure<HostOptions>(opts =>
            opts.ShutdownTimeout = TimeSpan.FromSeconds(5));

        builder.Services.AddBlazorWebViewOptions(
            new BlazorWebViewOptions()
            {
                RootComponent = typeof(BeoControlWebKit.App),
                HostPath = "wwwroot/index.html"
            }
        )
        .AddHostedService<Program>();

        builder.Services.AddSingleton<DeviceService>();
        builder.Services.AddSingleton<WebKitSpotifyService>();
        builder.Services.AddSingleton<ISpotifyService>(sp => sp.GetRequiredService<WebKitSpotifyService>());
        builder.Services.AddSingleton<IAutostartRegistrationService, UnsupportedAutostartRegistrationService>();

        using var host = builder.Build();

        await host.RunAsync();
    }

    public Program(IHostApplicationLifetime lifetime, IServiceProvider serviceProvider)
    {
        WebKit.Module.Initialize();

        _serviceProvider = serviceProvider;
        _deviceService = serviceProvider.GetRequiredService<DeviceService>();
        _lifetime = lifetime;
        _app = Adw.Application.New("org.gir.core", Gio.ApplicationFlags.FlagsNone);

        _app.OnActivate += (sender, args) =>
        {
            if (_window is not null)
            {
                ShowWindowCore();
                return;
            }

            _window = Gtk.ApplicationWindow.New((Adw.Application)sender);
            _window.Title = "BC";

            var lastWinPosition = _deviceService.Settings.LastWinPosition;
            _window.SetDefaultSize(lastWinPosition.WindowWidth, lastWinPosition.WindowHeight);

            var webView = new BlazorWebView(_serviceProvider);
            webView.Hexpand = true;
            webView.Vexpand = true;
            _window.SetChild(webView);
            _window.Show();
            _isWindowVisible = true;

            // Save window geometry before GTK destroys the window (GetWidth/Height return 0 after)
            _window.OnCloseRequest += (_, _) =>
            {
                SaveWindowState();
                if (_trayHost?.IsRegistered == true)
                {
                    HideWindowCore();
                    return true;
                }

                return false;
            };

            // Allow opening developer tools
            webView.GetSettings().EnableDeveloperExtras = true;

            EnsureTrayHost();
        };

        _app.OnShutdown += (sender, args) =>
        {
            _gtkExited = true;
            lifetime.StopApplication();
        };

        lifetime.ApplicationStarted.Register(() =>
        {
            Task.Run(() =>
            {
                _ = CheckForUpdatesAsync();
                _ = _deviceService.AutoConnectAsync();
                Environment.ExitCode = _app.Run(0, []);
            });
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            _trayHost?.Dispose();

            // Only call Quit if GTK hasn't already exited (e.g. external stop request)
            if (!_gtkExited)
                _app.Quit();

            _deviceService.Disconnect(silent: true);
        });
    }

    private void EnsureTrayHost()
    {
        if (_trayHost is not null || _trayHostTask is not null)
            return;

        _trayHostTask = TrayHost.TryCreateAsync(
            _deviceService,
            IsWindowVisible,
            ToggleWindowVisibility,
            ExitApplication,
            RunOnGtkThread);

        _ = _trayHostTask.ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion)
                _trayHost = task.Result;
        }, TaskScheduler.Default);
    }

    private void ToggleWindowVisibility()
    {
        RunOnGtkThread(() =>
        {
            if (_isWindowVisible)
                HideWindowCore();
            else
                ShowWindowCore();
        });
    }

    private void ShowWindowCore()
    {
        if (_window is null)
            return;

        _window.Present();
        _isWindowVisible = true;
    }

    private void HideWindowCore()
    {
        if (_window is null)
            return;

        _window.Hide();
        _isWindowVisible = false;
    }

    private void ExitApplication()
    {
        RunOnGtkThread(() =>
        {
            _trayHost?.Dispose();
            _lifetime.StopApplication();
            _app.Quit();
        });
    }

    private void RunOnGtkThread(Action action)
    {
        var handle = GCHandle.Alloc(action);
        g_idle_add(GtkIdleCallback, GCHandle.ToIntPtr(handle));
    }

    private static bool DispatchGtkIdle(IntPtr data)
    {
        var handle = GCHandle.FromIntPtr(data);
        try
        {
            ((Action)handle.Target!).Invoke();
        }
        finally
        {
            handle.Free();
        }

        return false;
    }

    private bool IsWindowVisible() => _isWindowVisible;

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            Console.WriteLine("Velopack: starting update check");
            Console.WriteLine($"Velopack: AppContext.BaseDirectory = {AppContext.BaseDirectory}");
            Console.WriteLine($"Velopack: CurrentDirectory = {Environment.CurrentDirectory}");
            Console.WriteLine($"Velopack: ProcessPath = {Environment.ProcessPath}");

            Console.WriteLine($"Velopack: UpdateFeedUrl = {UpdateFeedUrl}");
            var manager = new UpdateManager(UpdateFeedUrl);
            Console.WriteLine($"Velopack: IsInstalled = {manager.IsInstalled}");
            if (!manager.IsInstalled)
            {
                Console.WriteLine("Velopack: skipping update check because app is not running from an installed Velopack location");
                return;
            }

            Console.WriteLine("Velopack: checking update feed");
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Console.WriteLine("Velopack: no update available");
                return;
            }

            Console.WriteLine($"Velopack: update found -> {update.TargetFullRelease.Version}");
            Console.WriteLine("Velopack: downloading update package");
            await manager.DownloadUpdatesAsync(update);
            Console.WriteLine("Velopack: download finished");

            Console.WriteLine("Velopack: scheduling apply on exit");
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease);
            Console.WriteLine("Velopack: quitting app so update can be applied");
            ExitApplication();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Velopack update check failed: {ex}");
        }
    }

    private void SaveWindowState()
    {
        Console.WriteLine("Call SaveWindowState");
        if (_window is null)
            return;

        var lastWinPosition = _deviceService.Settings.LastWinPosition;
        lastWinPosition.WindowWidth = _window.GetWidth();
        lastWinPosition.WindowHeight = _window.GetHeight();
        Console.WriteLine($"Save W{lastWinPosition.WindowWidth} H:{lastWinPosition.WindowHeight}");
        _deviceService.Settings.Save();
    }

    private bool _gtkExited;
    private bool _isWindowVisible;
    private readonly IServiceProvider _serviceProvider;
    private readonly DeviceService _deviceService;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Adw.Application _app;
    private Gtk.ApplicationWindow? _window;
    private TrayHost? _trayHost;
    private Task<TrayHost?>? _trayHostTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool IdleCallback(IntPtr data);

    private sealed class TrayHost : IDisposable
    {
        private const string WatcherService = "org.kde.StatusNotifierWatcher";
        private static readonly ObjectPath WatcherPath = new("/StatusNotifierWatcher");
        internal static readonly ObjectPath MenuPath = new("/org/beocontrol/TrayMenu");
        internal static readonly ObjectPath ItemPath = new("/org/beocontrol/StatusNotifierItem");

        private readonly Connection _connection;

        private TrayHost(Connection connection, string serviceName)
        {
            _connection = connection;
            _serviceName = serviceName;
        }

        public bool IsRegistered { get; private set; }

        public static async Task<TrayHost?> TryCreateAsync(
            DeviceService deviceService,
            Func<bool> isWindowVisible,
            Action toggleWindowVisibility,
            Action exitApplication,
            Action<Action> gtkDispatcher)
        {
            var connection = new Connection(Address.Session);

            try
            {
                await connection.ConnectAsync();
                if (!await connection.IsServiceActiveAsync(WatcherService))
                {
                    connection.Dispose();
                    return null;
                }

                var serviceName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
                var trayHost = new TrayHost(connection, serviceName);

                trayHost._menu = new TrayMenuObject(deviceService, isWindowVisible, toggleWindowVisibility, exitApplication);
                trayHost._item = new StatusNotifierItemObject(deviceService, MenuPath, toggleWindowVisibility);

                await connection.RegisterServiceAsync(serviceName);
                await connection.RegisterObjectAsync(trayHost._menu);
                await connection.RegisterObjectAsync(trayHost._item);

                var watcher = connection.CreateProxy<IStatusNotifierWatcher>(WatcherService, WatcherPath);
                await watcher.RegisterStatusNotifierItemAsync(serviceName);

                trayHost.IsRegistered = true;
                return trayHost;
            }
            catch (Exception ex)
            {
                connection.Dispose();
                Console.WriteLine($"Tray disabled: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_item is not null)
                _connection.UnregisterObject(_item);

            if (_menu is not null)
                _connection.UnregisterObject(_menu);

            _connection.Dispose();
            IsRegistered = false;
        }

        private readonly string _serviceName;
        private bool _disposed;
        private StatusNotifierItemObject? _item;
        private TrayMenuObject? _menu;
    }

    [DBusInterface("org.kde.StatusNotifierWatcher")]
    private interface IStatusNotifierWatcher : IDBusObject
    {
        Task RegisterStatusNotifierItemAsync(string service);
    }

    [DBusInterface("org.kde.StatusNotifierItem")]
    private interface IStatusNotifierItem : IDBusObject
    {
        Task ContextMenuAsync(int x, int y);
        Task ActivateAsync(int x, int y);
        Task SecondaryActivateAsync(int x, int y);
        Task ScrollAsync(int delta, string orientation);
        Task<object> GetAsync(string prop);
        Task<StatusNotifierItemProperties> GetAllAsync();
        Task SetAsync(string prop, object val);
        Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    }

    [Dictionary]
    private sealed class StatusNotifierItemProperties
    {
        [Property(Access = PropertyAccess.Read)] public string Category = "ApplicationStatus";
        [Property(Access = PropertyAccess.Read)] public string Id = "BeoControl";
        [Property(Access = PropertyAccess.Read)] public string Title = "BeoControl";
        [Property(Access = PropertyAccess.Read)] public string Status = "Active";
        [Property(Access = PropertyAccess.Read)] public ObjectPath Menu;
        [Property(Access = PropertyAccess.Read)] public bool ItemIsMenu;
        [Property(Access = PropertyAccess.Read)] public int WindowId;
        [Property(Access = PropertyAccess.Read)] public (int Width, int Height, byte[] Bytes)[] IconPixmap = [];
        [Property(Access = PropertyAccess.Read)] public (string IconName, (int Width, int Height, byte[] Bytes)[] IconPixmap, string Title, string Description) ToolTip;
    }

    private sealed class StatusNotifierItemObject : IStatusNotifierItem
    {
        public StatusNotifierItemObject(DeviceService deviceService, ObjectPath menuPath, Action toggleWindowVisibility)
        {
            _deviceService = deviceService;
            _menuPath = menuPath;
            _toggleWindowVisibility = toggleWindowVisibility;
        }

        public ObjectPath ObjectPath => TrayHost.ItemPath;

        public event Action<PropertyChanges>? OnPropertiesChanged;

        public Task ContextMenuAsync(int x, int y) => Task.CompletedTask;

        public Task ActivateAsync(int x, int y)
        {
            _toggleWindowVisibility();
            return Task.CompletedTask;
        }

        public Task SecondaryActivateAsync(int x, int y)
        {
            _toggleWindowVisibility();
            return Task.CompletedTask;
        }

        public Task ScrollAsync(int delta, string orientation) => Task.CompletedTask;

        public Task<object> GetAsync(string prop)
        {
            var properties = BuildProperties();
            return Task.FromResult(prop switch
            {
                nameof(StatusNotifierItemProperties.Category) => (object)properties.Category,
                nameof(StatusNotifierItemProperties.Id) => properties.Id,
                nameof(StatusNotifierItemProperties.Title) => properties.Title,
                nameof(StatusNotifierItemProperties.Status) => properties.Status,
                nameof(StatusNotifierItemProperties.Menu) => properties.Menu,
                nameof(StatusNotifierItemProperties.ItemIsMenu) => properties.ItemIsMenu,
                nameof(StatusNotifierItemProperties.WindowId) => properties.WindowId,
                nameof(StatusNotifierItemProperties.IconPixmap) => properties.IconPixmap,
                nameof(StatusNotifierItemProperties.ToolTip) => properties.ToolTip,
                _ => throw new ArgumentOutOfRangeException(nameof(prop), prop, "Unknown tray property.")
            });
        }

        public Task<StatusNotifierItemProperties> GetAllAsync() => Task.FromResult(BuildProperties());

        public Task SetAsync(string prop, object val)
        {
            return Task.FromException(new InvalidOperationException("StatusNotifierItem properties are read-only."));
        }

        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
        {
            return SignalWatcher.AddAsync(this, nameof(OnPropertiesChanged), handler);
        }

        private StatusNotifierItemProperties BuildProperties()
        {
            var connected = _deviceService.IsConnected;
            var iconPixmap = new[] { CreateTrayPixmap(connected) };
            var description = connected ? "Connected" : "Disconnected";

            return new StatusNotifierItemProperties
            {
                Menu = _menuPath,
                ItemIsMenu = false,
                WindowId = 0,
                IconPixmap = iconPixmap,
                ToolTip = ("", iconPixmap, "BeoControl", description),
            };
        }

        private static (int Width, int Height, byte[] Bytes) CreateTrayPixmap(bool connected)
        {
            const int size = 16;
            var data = new byte[size * size * 4];

            var fillRed = connected ? (byte)48 : (byte)210;
            var fillGreen = connected ? (byte)180 : (byte)70;
            var fillBlue = connected ? (byte)72 : (byte)70;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - 7.5;
                    var dy = y - 7.5;
                    var distance = Math.Sqrt((dx * dx) + (dy * dy));
                    var offset = ((y * size) + x) * 4;

                    if (distance > 7)
                    {
                        data[offset] = 0;
                        data[offset + 1] = 0;
                        data[offset + 2] = 0;
                        data[offset + 3] = 0;
                        continue;
                    }

                    var border = distance > 5.8;
                    data[offset] = 255;
                    data[offset + 1] = border ? (byte)32 : fillRed;
                    data[offset + 2] = border ? (byte)32 : fillGreen;
                    data[offset + 3] = border ? (byte)32 : fillBlue;
                }
            }

            return (size, size, data);
        }

        private readonly DeviceService _deviceService;
        private readonly ObjectPath _menuPath;
        private readonly Action _toggleWindowVisibility;
    }

    [DBusInterface("com.canonical.dbusmenu")]
    private interface ITrayMenu : IDBusObject
    {
        Task<(uint Revision, DbusMenuLayoutItem Layout)> GetLayoutAsync(int parentId, int recursionDepth, string[] propertyNames);
        Task<DbusMenuPropertySet[]> GetGroupPropertiesAsync(int[] ids, string[] propertyNames);
        Task<object> GetPropertyAsync(int id, string name);
        Task EventAsync(int id, string eventId, object data, uint timestamp);
        Task<int[]> EventGroupAsync(DbusMenuEventRequest[] events);
        Task<bool> AboutToShowAsync(int id);
        Task<(int[] UpdatesNeeded, int[] IdErrors)> AboutToShowGroupAsync(int[] ids);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DbusMenuLayoutItem(int id, KeyValuePair<string, object>[] properties, DbusMenuLayoutItem[] children)
    {
        public readonly int Id = id;
        public readonly KeyValuePair<string, object>[] Properties = properties;
        public readonly DbusMenuLayoutItem[] Children = children;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DbusMenuPropertySet(int id, KeyValuePair<string, object>[] properties)
    {
        public readonly int Id = id;
        public readonly KeyValuePair<string, object>[] Properties = properties;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DbusMenuEventRequest(int id, string eventId, object data, uint timestamp)
    {
        public readonly int Id = id;
        public readonly string EventId = eventId;
        public readonly object Data = data;
        public readonly uint Timestamp = timestamp;
    }

    private sealed class TrayMenuObject : ITrayMenu
    {
        private const int RootId = 0;
        private const int ShowHideId = 1;
        private const int SeparatorOneId = 2;
        private const int MuteId = 3;
        private const int ATapeId = 4;
        private const int RadioId = 5;
        private const int PhonoId = 6;
        private const int PcId = 7;
        private const int SeparatorTwoId = 8;
        private const int ExitId = 9;

        public TrayMenuObject(
            DeviceService deviceService,
            Func<bool> isWindowVisible,
            Action toggleWindowVisibility,
            Action exitApplication)
        {
            _deviceService = deviceService;
            _isWindowVisible = isWindowVisible;
            _toggleWindowVisibility = toggleWindowVisibility;
            _exitApplication = exitApplication;
        }

        public ObjectPath ObjectPath => TrayHost.MenuPath;

        public Task<(uint Revision, DbusMenuLayoutItem Layout)> GetLayoutAsync(int parentId, int recursionDepth, string[] propertyNames)
        {
            var layout = BuildLayout(parentId, recursionDepth, propertyNames);
            return Task.FromResult((_revision, layout));
        }

        public Task<DbusMenuPropertySet[]> GetGroupPropertiesAsync(int[] ids, string[] propertyNames)
        {
            var result = new List<DbusMenuPropertySet>(ids.Length);
            foreach (var id in ids)
            {
                result.Add(new DbusMenuPropertySet(id, BuildProperties(id, propertyNames)));
            }

            return Task.FromResult(result.ToArray());
        }

        public Task<object> GetPropertyAsync(int id, string name)
        {
            foreach (var property in BuildProperties(id, [name]))
            {
                if (property.Key == name)
                    return Task.FromResult(property.Value);
            }

            return Task.FromResult<object>(0);
        }

        public Task EventAsync(int id, string eventId, object data, uint timestamp)
        {
            if (string.Equals(eventId, "clicked", StringComparison.Ordinal))
                HandleClick(id);

            return Task.CompletedTask;
        }

        public Task<int[]> EventGroupAsync(DbusMenuEventRequest[] events)
        {
            foreach (var entry in events)
            {
                if (string.Equals(entry.EventId, "clicked", StringComparison.Ordinal))
                    HandleClick(entry.Id);
            }

            return Task.FromResult(Array.Empty<int>());
        }

        public Task<bool> AboutToShowAsync(int id)
        {
            _revision++;
            return Task.FromResult(true);
        }

        public Task<(int[] UpdatesNeeded, int[] IdErrors)> AboutToShowGroupAsync(int[] ids)
        {
            _revision++;
            return Task.FromResult((ids, Array.Empty<int>()));
        }

        private DbusMenuLayoutItem BuildLayout(int parentId, int recursionDepth, string[] propertyNames)
        {
            if (parentId != RootId)
                return new DbusMenuLayoutItem(parentId, BuildProperties(parentId, propertyNames), Array.Empty<DbusMenuLayoutItem>());

            if (recursionDepth == 0)
                return new DbusMenuLayoutItem(RootId, Array.Empty<KeyValuePair<string, object>>(), Array.Empty<DbusMenuLayoutItem>());

            var children = new[]
            {
                new DbusMenuLayoutItem(ShowHideId, BuildProperties(ShowHideId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(SeparatorOneId, BuildProperties(SeparatorOneId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(MuteId, BuildProperties(MuteId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(ATapeId, BuildProperties(ATapeId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(RadioId, BuildProperties(RadioId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(PhonoId, BuildProperties(PhonoId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(PcId, BuildProperties(PcId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(SeparatorTwoId, BuildProperties(SeparatorTwoId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
                new DbusMenuLayoutItem(ExitId, BuildProperties(ExitId, propertyNames), Array.Empty<DbusMenuLayoutItem>()),
            };

            return new DbusMenuLayoutItem(RootId, Array.Empty<KeyValuePair<string, object>>(), children);
        }

        private KeyValuePair<string, object>[] BuildProperties(int id, string[] propertyNames)
        {
            var names = propertyNames.Length == 0
                ? new[] { "type", "label", "enabled", "visible" }
                : propertyNames;

            var values = new List<KeyValuePair<string, object>>(names.Length);
            foreach (var name in names)
            {
                if (TryGetPropertyValue(id, name, out var value))
                    values.Add(new KeyValuePair<string, object>(name, value));
            }

            return values.ToArray();
        }

        private bool TryGetPropertyValue(int id, string name, out object value)
        {
            value = default!;

            if (id == SeparatorOneId || id == SeparatorTwoId)
            {
                if (string.Equals(name, "type", StringComparison.Ordinal))
                {
                    value = "separator";
                    return true;
                }

                if (string.Equals(name, "visible", StringComparison.Ordinal))
                {
                    value = true;
                    return true;
                }

                return false;
            }

            if (string.Equals(name, "visible", StringComparison.Ordinal))
            {
                value = true;
                return true;
            }

            if (string.Equals(name, "label", StringComparison.Ordinal))
            {
                value = id switch
                {
                    ShowHideId => _isWindowVisible() ? "Hide" : "Show",
                    MuteId => "MUTE",
                    ATapeId => "A.TAPE",
                    RadioId => "RADIO",
                    PhonoId => "PHONO",
                    PcId => "PC",
                    ExitId => "Exit",
                    _ => string.Empty,
                };
                return !string.IsNullOrEmpty((string)value);
            }

            if (string.Equals(name, "enabled", StringComparison.Ordinal))
            {
                value = id switch
                {
                    ShowHideId => true,
                    ExitId => true,
                    MuteId or ATapeId or RadioId or PhonoId or PcId => _deviceService.IsConnected,
                    _ => true,
                };
                return true;
            }

            return false;
        }

        private void HandleClick(int id)
        {
            switch (id)
            {
                case ShowHideId:
                    _toggleWindowVisibility();
                    break;
                case MuteId:
                    _deviceService.SendCommand("mute");
                    break;
                case ATapeId:
                    _deviceService.SendCommand("atape");
                    break;
                case RadioId:
                    _deviceService.SendCommand("radio");
                    break;
                case PhonoId:
                    _deviceService.SendCommand("phono");
                    break;
                case PcId:
                    _deviceService.SendCommand("pc");
                    break;
                case ExitId:
                    _exitApplication();
                    break;
            }
        }

        private uint _revision = 1;
        private readonly DeviceService _deviceService;
        private readonly Func<bool> _isWindowVisible;
        private readonly Action _toggleWindowVisibility;
        private readonly Action _exitApplication;
    }
}
