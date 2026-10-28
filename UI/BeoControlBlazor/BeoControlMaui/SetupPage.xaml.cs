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
#endif
        }
    }
}
