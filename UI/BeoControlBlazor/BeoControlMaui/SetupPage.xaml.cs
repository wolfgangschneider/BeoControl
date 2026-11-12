using Microsoft.AspNetCore.Components.WebView;

namespace BeoControlMaui
{
    public partial class SetupPage : ContentPage
    {
        public SetupPage()
        {
            InitializeComponent();
        }

        private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        {
#if WINDOWS
            // Red close dot calls window.close(): close this MAUI window
            e.WebView.CoreWebView2.WindowCloseRequested += (_, _) =>
                Dispatcher.Dispatch(() =>
                {
                    if (Window is { } window)
                        Application.Current?.CloseWindow(window);
                });
            // borderless window: mouse down on the display or the nameplate moves it
            e.WebView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (args.TryGetWebMessageAsString() == "beocontrol:drag" && Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                    MainPage.StartMove(Dispatcher, WinRT.Interop.WindowNative.GetWindowHandle(native));
            };
#endif
        }
    }
}
