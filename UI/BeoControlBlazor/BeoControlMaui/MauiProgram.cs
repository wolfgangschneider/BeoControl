using BeoControlBlazorServices;

using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

using System.Runtime.InteropServices;

namespace BeoControlMaui
{
    internal static class Win32
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
    public static class MauiProgram
    {
        public static IServiceProvider? Services { get; private set; }

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            builder.ConfigureLifecycleEvents(events =>
            {
#if WINDOWS
                events.AddWindows(w =>
                {
                    w.OnWindowCreated(window =>
                    {
                        // Get native HWND
                        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

                        // Hide the window immediately
                        Win32.ShowWindow(hWnd, Win32.SW_HIDE);

                        // You can later show it with SW_SHOW
                    });
                });
#endif
            });


#if WINDOWS
            builder.Services.AddSingleton<IAutostartRegistrationService, WindowsAutostartRegistrationService>();

#else
            builder.Services.AddSingleton<IAutostartRegistrationService, UnsupportedAutostartRegistrationService>();
#endif
            builder.Services.AddSingleton<ILaunchSpotifyService, LaunchSpotifyService>();

#if WINDOWS
            // WebView2 needs a writable user-data folder; C:\Program Files is read-only for non-admins.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BeoControl", "WebView2Cache"));
#endif

            builder.Services.AddSingleton<DeviceService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            Services = app.Services;
            return app;
        }
    }
}
