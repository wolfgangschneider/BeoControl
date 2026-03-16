using Microsoft.UI.Xaml;

using Windows.Graphics;

namespace BeoControlMaui.WinUI
{
    public partial class App : MauiWinUIApplication
    {
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
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                        WinRT.Interop.WindowNative.GetWindowHandle(win)));

                appWindow.Resize(new SizeInt32(RemoteWindow.Width, RemoteWindow.Height));
            }
        }
    }
}
