using BeoControlBlazor.Services;

using BeoControlBlazorServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using WebKitGtk;

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
internal class Program : IHostedService
{
    private static async Task Main(string[] args)
    {
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
        builder.Services.AddSingleton<IAutostartRegistrationService, UnsupportedAutostartRegistrationService>();

        using var host = builder.Build();

        await host.RunAsync();
    }

    public Program(IHostApplicationLifetime lifetime, IServiceProvider serviceProvider)
    {
        WebKit.Module.Initialize();

        _serviceProvider = serviceProvider;
        _deviceService = serviceProvider.GetRequiredService<DeviceService>();
        _settings = AppSettings.Load();
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

            var lastWinPosition = _settings.LastWinPosition;
            _window.SetDefaultSize(lastWinPosition.WindowWidth, lastWinPosition.WindowHeight);

            var webView = new BlazorWebView(_serviceProvider);
            webView.Hexpand = true;
            webView.Vexpand = true;
            _window.SetChild(webView);
            _window.Show();

            // Allow opening developer tools
            webView.GetSettings().EnableDeveloperExtras = true;
        };

        _app.OnShutdown += (sender, args) =>
        {
            SaveWindowState();
            _gtkExited = true;
            lifetime.StopApplication();
        };

        lifetime.ApplicationStarted.Register(() =>
        {
            Task.Run(() =>
            {
                _ = _deviceService.AutoConnectAsync();
                Environment.ExitCode = _app.Run(0, []);
            });
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            SaveWindowState();

            // Only call Quit if GTK hasn't already exited (e.g. external stop request)
            if (!_gtkExited)
                _app.Quit();

            _deviceService.Disconnect(silent: true);
        });
    }

    bool _gtkExited;
    readonly IServiceProvider _serviceProvider;
    readonly DeviceService _deviceService;
    readonly AppSettings _settings;
    readonly Adw.Application _app;
    Gtk.ApplicationWindow? _window;

    private void SaveWindowState()
    {
        if (_window is null)
            return;

        var lastWinPosition = _settings.LastWinPosition;
        lastWinPosition.WindowWidth = _window.GetWidth();
        lastWinPosition.WindowHeight = _window.GetHeight();
        _settings.Save();
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
