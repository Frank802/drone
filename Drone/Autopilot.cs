using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Drone
{
    public static class Autopilot
    {
        public static bool IsInControl { get; set; }
        public static bool IsLanding { get; set; }

        private static CancellationTokenSource cts;

        public static async void TakeControl()
        {
            IsInControl = true;

            if (App.isRPi)
            {
                var cmd = Encoding.ASCII.GetBytes("#autopilotON");
                Socket.SendData(cmd);
            }
            else
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var autopilotLabel = currentPage.FindName("autopilotLabel") as TextBlock;
                    autopilotLabel.Visibility = Visibility.Visible;
                });
            }
        }

        public static async void ReleaseControl()
        {
            IsInControl = false;

            if (App.isRPi)
            {
                if (cts != null)
                {
                    cts.Dispose();
                    cts = null;
                }

                var cmd = Encoding.ASCII.GetBytes("#autopilotOFF");
                Socket.SendData(cmd);
            }
            else
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var autopilotLabel = currentPage.FindName("autopilotLabel") as TextBlock;
                    autopilotLabel.Visibility = Visibility.Collapsed;
                });
            }
        }

        public static void Land()
        {
            if (App.isRPi)
            {
                cts = new CancellationTokenSource();
                var ct = cts.Token;

                Task.Run(async () =>
                {
                    if (!IsInControl)
                        TakeControl();

                    if (MultiWii.usDistance <= 200)
                    {
                        IsLanding = true;

                        while (MultiWii.usDistance > 15)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                ReleaseControl();
                                return;
                            }

                            if (MultiWii.throttle < 1000)
                                break;

                            var throttle = MultiWii.throttle - 1;

                            MultiWii.MSP_SET_RAW_RC((ushort)throttle, (ushort)MultiWii.roll, (ushort)MultiWii.pitch, (ushort)MultiWii.yaw,
                                                    (ushort)MultiWii.aux1, 1000, (ushort)MultiWii.aux3, (ushort)MultiWii.aux4); //Cleanflight - TAER1234
                            await Task.Delay(100);
                        }

                        var cmd = Encoding.ASCII.GetBytes("#land");
                        Socket.SendData(cmd);

                        IsLanding = false;
                    }

                    ReleaseControl();
                }, ct);
            }
            else
            {
                MainPage.throttle = 0;
                MainPage.throttleTakeOffRange = 0;
                MainPage.isTakeOffCompleted = false;
                MainPage.isLandCompleted = true;

                CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var takeOffLandToggle = currentPage.FindName("takeOffLandToggle") as Image;
                    var armToggle = currentPage.FindName("armToggle") as ToggleButton;
                    if (armToggle.IsChecked == true)
                        takeOffLandToggle.Opacity = 1;
                    takeOffLandToggle.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Icons/takeoff.png"));
                });
            }
        }

        public static void TakeOff()
        {
            if (App.isRPi)
            {
                //TODO: Drone area
            }
            else
            {
                MainPage.isTakeOffCompleted = true;
                MainPage.isLandCompleted = false;
            }
        }

        public static void GoTo(double latitude, double longitude)
        {
            if (!App.isRPi)
                return;
        }

        public static void Dismiss()
        {
            if (App.isRPi)
            {
                if (cts != null)
                    cts.Cancel();
                else
                    throw new Exception("Unable to dismiss Autopilot operation.");

                var cmd = Encoding.ASCII.GetBytes("#dismiss");
                Socket.SendData(cmd);
            }
            else
            {
                CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var takeOffLandToggle = currentPage.FindName("takeOffLandToggle") as Image;
                    //takeOffLandToggle.IsChecked = true;
                    takeOffLandToggle.Opacity = 1;
                });
            }
        }
    }
}