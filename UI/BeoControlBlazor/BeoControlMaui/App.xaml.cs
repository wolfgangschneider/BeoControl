using BeoControlBlazorServices;

namespace BeoControlMaui
{
    public partial class App : Application
    {
        private readonly DeviceService _deviceService;

        public App(DeviceService deviceService)
        {
            _deviceService = deviceService;
            InitializeComponent();
        }

        protected override void OnStart()
        {
            base.OnStart();
            if (!OperatingSystem.IsWindows())
                _ = _deviceService.AutoConnectAsync();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "BC" };
        }
    }
}
