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

        // moving the borderless remote window by dragging its surface
        public const int WM_CLOSE = 0x10;
        public const int VK_LBUTTON = 0x01;
        public const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out System.Drawing.Point point);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, nint wParam, nint lParam);

        // cutting the remote window to the shape of the remote
        public const int RGN_OR = 2;

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

        [DllImport("gdi32.dll")]
        public static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point point);

        // borderless setup window: no Windows 11 border line, no rounded corners
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_DONOTROUND = 1, DWMWA_BORDER_COLOR = 34;
        public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref uint value, int size);
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
#if ANDROID
            // builder.Services.AddSingleton<AndroidSpotifyService>();
            builder.Services.AddSingleton<ISpotifyService, AndroidSpotifyService>();
#else
            // builder.Services.AddSingleton<MauiSpotifyService>();
            builder.Services.AddSingleton<ISpotifyService, MauiSpotifyService>();
#endif

#if WINDOWS
            // WebView2 needs a writable user-data folder; C:\Program Files is read-only for non-admins.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BeoControl", "WebView2Cache"));
#endif

            builder.Services.AddSingleton<DeviceService>();
            builder.Services.AddSingleton<UpdateService>();

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
