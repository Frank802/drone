using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Networking.Connectivity;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Drone
{
	public enum Mode
	{
		WiFiTCP = 1,
		WiFiUDP = 2,
		Cellular = 3
	}

    public enum DroneType
    {
        Custom = 1,
        Common = 2
    }

    sealed partial class App : Application
    {
        public static bool isRPi;
        public static bool internetAccess;
        public static string HostName;
        public static Mode Mode;
        public static string DroneName;
        public static DroneType DroneType;
        internal static string MapServiceToken = "YOUR_MAP_SERIVCE_TOKEN";
		public static string ServiceUri = "YOUR_SERVICE_URI";

        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;

            NetworkInformation.NetworkStatusChanged += NetworkStatusChanged;

            isRPi = true;
            internetAccess = IsInternetAvailable();
            HostName = "192.168.4.1"; // 192.168.4.1 for wemos - 192.168.137.1 for minwinpc
			Mode = Mode.Cellular;
            DroneName = "drone";
            DroneType = DroneType.Custom;
        }

        private static bool IsInternetAvailable()
        {
            ConnectionProfile connections = NetworkInformation.GetInternetConnectionProfile();
            return connections != null && connections.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }

        private void NetworkStatusChanged(object sender)
        {
            if (Mode == Mode.Cellular)
            { 
                if (IsInternetAvailable())
                {
                    internetAccess = true;
                }
                else
                {
                    internetAccess = false;
                }
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }

                Window.Current.Activate();
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }
    }
}
