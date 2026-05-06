using BeoControlBlazor.Services;

using BeoControlBlazorServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using Velopack;

using WebKitGtk;

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
internal class Program : IHostedService
{
    private const string UpdateFeedUrl = "https://wolfgangschneider.github.io/BeoControl/updates";

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
        _app = Adw.Application.New("org.gir.core", Gio.ApplicationFlags.FlagsNone);

        _app.OnActivate += (sender, args) =>
        {
            if (_window is not null)
            {
                _window.Present();
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

            // Save window geometry before GTK destroys the window (GetWidth/Height return 0 after)
            _window.OnCloseRequest += (_, _) =>
            {
                SaveWindowState();
                return false;
            };

            // Allow opening developer tools
            webView.GetSettings().EnableDeveloperExtras = true;
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
            // Only call Quit if GTK hasn't already exited (e.g. external stop request)
            if (!_gtkExited)
                _app.Quit();

            _deviceService.Disconnect(silent: true);
        });
    }

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
            _app.Quit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Velopack update check failed: {ex}");
        }
    }

    bool _gtkExited;
    readonly IServiceProvider _serviceProvider;
    readonly DeviceService _deviceService;
    readonly Adw.Application _app;
    Gtk.ApplicationWindow? _window;

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

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
