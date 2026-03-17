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
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceService>());

        using var host = builder.Build();

        await host.RunAsync();
    }

    public Program(IHostApplicationLifetime lifetime, IServiceProvider serviceProvider)
    {
        WebKit.Module.Initialize();

        _serviceProvider = serviceProvider;
        _app = Adw.Application.New("org.gir.core", Gio.ApplicationFlags.FlagsNone);

        _app.OnActivate += (sender, args) =>
        {
            var window = Gtk.ApplicationWindow.New((Adw.Application)sender);
            window.Title = "BeoControl";
            window.SetDefaultSize(250, 950);

            var webView = new BlazorWebView(_serviceProvider);
            webView.Hexpand = true;
            webView.Vexpand = true;
            window.SetChild(webView);
            window.Show();

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
                Environment.ExitCode = _app.Run(0, []);
            });
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            // Only call Quit if GTK hasn't already exited (e.g. external stop request)
            if (!_gtkExited)
                _app.Quit();
        });
    }

    bool _gtkExited;
    readonly IServiceProvider _serviceProvider;
    readonly Adw.Application _app;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}