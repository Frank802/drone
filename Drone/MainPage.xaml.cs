using System;
using System.Diagnostics;
using Windows.Devices.Gpio;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Gaming.Input;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.Devices.Geolocation;
using Windows.UI.Xaml.Controls.Maps;
using Windows.Foundation;
using System.Text;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml.Media.Imaging;
using Drone.Helpers;
using Windows.UI.Popups;

namespace Drone
{
    public sealed partial class MainPage : Page
    {
		public static Stopwatch stopwatch;
        private static Gamepad controller;
        public static bool isGamepadConnected;
        public static bool isLandCompleted;
        public static bool isTakeOffCompleted;
        public static long lastCmdSent;
        private static Geopoint dronePosition;
        private static Image droneIcon, homeIcon;

        private const int LED_PIN = 26;
        private static GpioPin ledPin;
        private static GpioPinValue ledPinValue;

        private const int TRIG_PIN = 21;
        private const int ECHO_PIN = 20;
        private static UltrasonicSensor ultrasonicSensor;

        private static ushort aux1, aux2, aux3, aux4;
        public static double throttle, yaw, roll, pitch;
        public static int throttleTakeOffRange;
        private const int MAX_THROTTLE_TAKEOFF_FACTOR = 500;
        private const int THROTTLE_TAKEOFF_FACTOR = 0;
        private const int THROTTLE_SAFE_FACTOR = 50; //this + THROTTLE_TAKEOFF_FACTOR
        private const int LANDING_DISTANCE = 50;
        private const double STICKS_DEAD_ZONE = 0.1;
        private static int flightModeCount;

        private static bool takeOffLandToggleTapped;
        private static bool returnToHomeToggleTapped;

        public MainPage()
        {
            this.InitializeComponent();

            stopwatch = new Stopwatch();
            stopwatch.Start();

            isGamepadConnected = false;
            isTakeOffCompleted = false;
            isLandCompleted = false;

            throttle = 0;
            roll = 1500;
            pitch = 1500;
            yaw = 1500;
            aux1 = 1500;
            aux2 = 1500;
            aux3 = 1500;
            aux4 = 1500;
            throttleTakeOffRange = 0;
            flightModeCount = 0;

            armToggle.IsEnabled = false;
            armToggle.IsTabStop = false;
            horizonToggle.IsEnabled = false;
            horizonToggle.IsTabStop = false;
            //takeOffLandToggle.IsEnabled = false;
            //takeOffLandToggle.IsTabStop = false;
            plusButton.IsEnabled = false;
            plusButton.IsTabStop = false;
            minusButton.IsEnabled = false;
            minusButton.IsTabStop = false;
			gpsHoldToggle.IsEnabled = false;
			gpsHoldToggle.IsTabStop = false;
            //returnToHomeToggle.IsEnabled = false;
            //returnToHomeToggle.IsTabStop = false;
            //autoToggle.IsEnabled = false;
            //autoToggle.IsTabStop = false;

            takeOffLandToggleTapped = false;
            takeOffLandToggle.IsTapEnabled = false;
            takeOffLandToggle.Opacity = 0.2;
            returnToHomeToggleTapped = false;
            returnToHomeToggle.IsTapEnabled = false;
            returnToHomeToggle.Opacity = 0.2;

            droneIcon = new Image();
            droneIcon.Source = new BitmapImage(new Uri("ms-appx:///Assets/Icons/drone_pin.png"));
            droneIcon.Width = 30;
            droneIcon.Height = 33;
			homeIcon = new Image();
			homeIcon.Source = new BitmapImage(new Uri("ms-appx:///Assets/Icons/home_pin.png"));
			homeIcon.Width = 33;
			homeIcon.Height = 33;
			map.IsTabStop = false;
            map.IsFocusEngaged = false;
            map.ZoomLevel = 17;
            map.Children.Add(droneIcon);
			map.Children.Add(homeIcon);
			map.MapServiceToken = App.MapServiceToken;
 
            Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitGPIO();

            if (!App.isRPi)
            {
				InitGPS();
				Gamepad.GamepadAdded += Gamepad_GamepadAdded;
				Gamepad.GamepadRemoved += Gamepad_GamepadRemoved;
				Window.Current.CoreWindow.KeyDown += GamePadKeyDownMap;

				ContentDialog startupDialog = new ContentDialog()
                {
                    Title = "Starup drone",
                    Content = "Which drone do you want to pilot?",
                    PrimaryButtonText = "Custom",
                    SecondaryButtonText = "DJI"
                };

                ContentDialogResult result = await startupDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    App.DroneName = "drone";
                    App.DroneType = DroneType.Custom;
                }

                if (result == ContentDialogResult.Secondary)
                {
                    App.DroneName = "dji";
                    App.DroneType = DroneType.Common;
                }

                startupDialog = new ContentDialog()
                {
                    Title = "Select mode",
                    Content = "Which network do you want to use?",
                    PrimaryButtonText = "Cellular",
                    SecondaryButtonText = "WiFi"
                };

                result = await startupDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    App.Mode = Mode.Cellular;
                }

                if (result == ContentDialogResult.Secondary)
                {
                    App.Mode = Mode.WiFiTCP;
                }
            }

            await Socket.Init();
            await Camera.Init();

            if (App.isRPi)
            {
                await InitNaze32();
                InitTelemetry();
                InitUltrasonicSensor();
                Camera.StartCapture();
                TurnOnLED();
            }

            OnTimer();
        }

        private void OnTimer()
        {
            Task.Run(() =>
            {
                long currentTimeMilliseconds = -1, previousTimeMilliseconds = -1;
                long currentTelemetryMs = -1, previousTelemetryMs = -1;

                while (true)
                {
                    currentTimeMilliseconds = currentTelemetryMs =  stopwatch.ElapsedMilliseconds;

                    // Check for new cmd every 20 ms
                    if ((currentTimeMilliseconds - previousTimeMilliseconds) > 20 || previousTimeMilliseconds == -1)
                    {
                        var socketIsConnected = Socket.socketIsConnected;

                        if (!App.isRPi && isGamepadConnected && socketIsConnected)
                        {
                            // Read data from controller
                            var reading = controller.GetCurrentReading();

                            // Set RC data
                            SetThrottle(reading.RightTrigger, reading.LeftTrigger, reading.LeftThumbstickY);
                            SetYaw(reading.LeftThumbstickX);
                            SetPitch(reading.RightThumbstickY);
                            SetRoll(reading.RightThumbstickX);

                            Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                pitchLabel.Text = $"P:{(ushort)pitch}";
                                rollLabel.Text = $"R:{(ushort)roll}";
                                yawLabel.Text = $"Y:{(ushort)yaw}";
                                throttleLabel.Text = $"T:{(ushort)throttle}";
                                takeOffRangeLabel.Text = $"TOR:{throttleTakeOffRange}";
                            });

                            //Send data to RPI
                            var bytes = MultiWii.Calculate_MSP_SET_RAW_RC((ushort)throttle, (ushort)roll, (ushort)pitch, (ushort)yaw, aux1, aux2, aux3, aux4); //Cleanflight - TAER1234
                            Socket.SendData(bytes);

                            lastCmdSent = stopwatch.ElapsedMilliseconds;
                        }

                        previousTimeMilliseconds = currentTimeMilliseconds;
                    }

                    if ((currentTelemetryMs - previousTelemetryMs) > 150 || previousTelemetryMs == -1)
                    {               
                        if (App.isRPi)
                        {
                            MultiWii.GetGPSData();
                            MultiWii.GetAttitude();
                            MultiWii.GetBatteryState();
                            ultrasonicSensor.GetDistance();
                        }

                        SetGpsData();
                        SetAttitudeData();
                        SetBatteryData();
                        SetSignalStrengthData();
                        SetUltrasonicSensorData();

                        SendTelemetryData();

                        previousTelemetryMs = currentTelemetryMs;
                    }
                }
            });
        }

        private async void Gamepad_GamepadRemoved(object sender, Gamepad e)
        {
            Debug.WriteLine("--------- CONTROLLER DISCONNECTED ---------");
            controller = null;
            isGamepadConnected = false;
            
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
			{
                gamepadStatus.Opacity = 0.2;
                armToggle.IsEnabled = false;
                horizonToggle.IsEnabled = false;
                //takeOffLandToggle.IsTapEnabled = false;
                //takeOffLandToggle.Opacity = 0.2;
            });
        }

        private async void Gamepad_GamepadAdded(object sender, Gamepad e)
        {
            Debug.WriteLine("--------- CONTROLLER CONNECTED ---------");
            controller = e;
            isGamepadConnected = true;

			await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
			{
				gamepadStatus.Opacity = 1;
                armToggle.IsEnabled = true;
                horizonToggle.IsEnabled = true;
                //takeOffLandToggle.IsTapEnabled = true;
                //takeOffLandToggle.Opacity = 1;
            });
        }

        private void GamePadKeyDownMap(CoreWindow sender, KeyEventArgs args)
        {
            if (args.Handled)
                return;

            switch (args.VirtualKey)
            {
                case VirtualKey.GamepadView:
                    if (armToggle.IsChecked == true)
                        armToggle.IsChecked = false;
                    else
                        armToggle.IsChecked = true;
                    break;
                case VirtualKey.GamepadMenu:
                    if (horizonToggle.IsChecked == true)
                    {
                        switch (flightModeCount)
                        {
                            case 0:
                                horizonToggle.Content = "Angle";
                                aux2 = 1000;
                                flightModeCount++;
                                break;
                            case 1:
                                flightModeCount = 0;
                                horizonToggle.IsChecked = false;
                                break;
                        }
                    }
                    else
                        horizonToggle.IsChecked = true;
                    break;
                case VirtualKey.GamepadX:
                    if (armToggle.IsChecked == true)
                    {
                        var value = (int)throttle - THROTTLE_TAKEOFF_FACTOR - 1000;
                        if (isTakeOffCompleted && value < MAX_THROTTLE_TAKEOFF_FACTOR)
                            throttleTakeOffRange = value;
                    }
                    break;
                case VirtualKey.GamepadY:
                    var cmdY = Encoding.ASCII.GetBytes("#take_photo");
                    Socket.SendData(cmdY);
                    break;
                case VirtualKey.GamepadB:
                    if (armToggle.IsChecked == true)
                    {
                        if (App.DroneType == DroneType.Custom)
                        {
                            if (isTakeOffCompleted && !Autopilot.IsInControl)
                                throttleTakeOffRange = THROTTLE_SAFE_FACTOR;

                            if (Autopilot.IsInControl)
                            {
                                var cmdB = Encoding.ASCII.GetBytes("#dismiss");
                                Socket.SendData(cmdB);
                            }
                        }

                        if (App.DroneType == DroneType.Common)
                        {
                            if (isTakeOffCompleted)
                                throttleTakeOffRange = THROTTLE_SAFE_FACTOR;
                        }
                    }
                    break;
                case VirtualKey.GamepadA:
                    //if (armToggle.IsChecked == true && autoToggle.Visibility == Visibility.Visible)
                    //{
                    //    if (autoToggle.IsChecked == true)
                    //        autoToggle.IsChecked = false;
                    //    else
                    //        autoToggle.IsChecked = true;
                    //}
                    break;
                case VirtualKey.GamepadDPadDown:
                    if (armToggle.IsChecked == true)
                    {
                        /*if (takeOffLandToggle.IsChecked == true)
                            takeOffLandToggle.IsChecked = false;*/
                        takeOffLandToggle_Unchecked();
                    }
                    break;
                case VirtualKey.GamepadDPadUp:
                    if (armToggle.IsChecked == true)
                    {
                        /*if (takeOffLandToggle.IsChecked == false)
                            takeOffLandToggle.IsChecked = true;*/
                        takeOffLandToggle_Checked();
                    }
                    break;
                case VirtualKey.GamepadDPadRight:
                    if (armToggle.IsChecked == true)
                    {
                        /*if (gpsHoldToggle.IsChecked == false && MultiWii.gpsFix == 2)
                            gpsHoldToggle.IsChecked = true;
                        else
                            gpsHoldToggle.IsChecked = false;*/
                    }
                    break;
                case VirtualKey.GamepadDPadLeft:
                    if (armToggle.IsChecked == true)
                    {
                        /*if (returnToHomeToggle.IsChecked == false && MultiWii.gpsFix == 2)
                            returnToHomeToggle.IsChecked = true;
                        else
                            returnToHomeToggle.IsChecked = false;*/
                        if (MultiWii.gpsFix == 2)
                            returnToHomeToggle_Tapped(this, null);
                    }
                    break;
                case VirtualKey.GamepadLeftShoulder:
                    if (armToggle.IsChecked == true)
                    {
                        if (isTakeOffCompleted && (THROTTLE_TAKEOFF_FACTOR + throttleTakeOffRange) > THROTTLE_TAKEOFF_FACTOR)
                        {
                            if (throttleTakeOffRange < 5)
                                throttleTakeOffRange = 0;
                            else
                                throttleTakeOffRange -= 5;
                        }
                    }
                    break;
                case VirtualKey.GamepadRightShoulder:
                    if (armToggle.IsChecked == true)
                    {
                        if (isTakeOffCompleted && (THROTTLE_TAKEOFF_FACTOR + throttleTakeOffRange) < MAX_THROTTLE_TAKEOFF_FACTOR)
                            throttleTakeOffRange += 5;
                    }
                    break;
            }
        }

        private void droneStatus_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (App.Mode == Mode.Cellular)
                return;

            if (droneStatus.Opacity < 0.3)
                FlyoutBase.ShowAttachedFlyout(sender as FrameworkElement);
        }

        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.Mode == Mode.Cellular)
                return;

            WiFiFlyout.Hide();

            var hostName = App.HostName;

            if (!string.IsNullOrWhiteSpace(deviceName.Text))
                hostName = deviceName.Text;

			if (App.Mode == Mode.WiFiTCP)
				await StreamSocketCmd.NetworkInit(hostName);
			if (App.Mode == Mode.WiFiUDP)
				await DatagramSocketCmd.NetworkInit(hostName);
		}

        private void InitGPIO()
        {
            var gpio = GpioController.GetDefault();

            if (gpio == null)
            {
                App.isRPi = false;
                return;
            }

            // Set led
            ledPin = gpio.OpenPin(LED_PIN);
            ledPinValue = GpioPinValue.Low;
            ledPin.Write(ledPinValue);
            ledPin.SetDriveMode(GpioPinDriveMode.Output);
        }

        private async Task InitNaze32()
        {
            await MultiWii.Init();
        }

        private void InitTelemetry()
        {
            Telemetry.Init();         
        }

        private void InitUltrasonicSensor()
        {
            ultrasonicSensor = new UltrasonicSensor(TRIG_PIN, ECHO_PIN);
        }

		private async void InitGPS() 
		{
			try
			{
				// Request permission to access location
				var accessStatus = await Geolocator.RequestAccessAsync();

				switch (accessStatus)
				{
					case GeolocationAccessStatus.Allowed:

						// If DesiredAccuracy or DesiredAccuracyInMeters are not set (or value is 0), DesiredAccuracy.Default is used.
						Geolocator geolocator = new Geolocator 
						{ 
							DesiredAccuracy = PositionAccuracy.High, 
							MovementThreshold = 2, 
							ReportInterval = 1000 
						};

						// Carry out the operation
						Geoposition pos = await geolocator.GetGeopositionAsync();

						// Update the map control with the pilot position
						map.Center = pos.Coordinate.Point;
						map.ZoomLevel = 17;

						MapControl.SetLocation(homeIcon, pos.Coordinate.Point);
						MapControl.SetNormalizedAnchorPoint(homeIcon, new Point(0.5, 1));
						break;

					case GeolocationAccessStatus.Denied:
						await new MessageDialog("Access to location is denied.").ShowAsync();
						break;

					case GeolocationAccessStatus.Unspecified:
						await new MessageDialog("Unspecified error.").ShowAsync();
						break;
				}
			}
			catch (Exception ex)
			{
				await new MessageDialog(ex.Message).ShowAsync();
			}
		}

        private void SetThrottle(double rightTrigger, double leftTrigger, double leftThumbstickY)
        {
            // Incremental split of throttle on left and right trigger
            if (isTakeOffCompleted)
            {
                if (leftThumbstickY > STICKS_DEAD_ZONE || leftThumbstickY < -STICKS_DEAD_ZONE)
                {
                    if((THROTTLE_TAKEOFF_FACTOR + throttleTakeOffRange) <= MAX_THROTTLE_TAKEOFF_FACTOR && (THROTTLE_TAKEOFF_FACTOR + throttleTakeOffRange) >= THROTTLE_TAKEOFF_FACTOR)
                        throttleTakeOffRange += (int)(leftThumbstickY * 2);

                    if (throttleTakeOffRange > MAX_THROTTLE_TAKEOFF_FACTOR)
                        throttleTakeOffRange = MAX_THROTTLE_TAKEOFF_FACTOR;

                    if (throttleTakeOffRange < THROTTLE_TAKEOFF_FACTOR)
                        throttleTakeOffRange = 0;
                }

                if (rightTrigger == 1)
                {
                    throttle = throttleTakeOffRange + THROTTLE_TAKEOFF_FACTOR + (rightTrigger * (500 - THROTTLE_TAKEOFF_FACTOR - throttleTakeOffRange)) + (leftTrigger * 500) + 1000;
                }
                else
                {
                    throttle = throttleTakeOffRange + THROTTLE_TAKEOFF_FACTOR + (rightTrigger * (500 - THROTTLE_TAKEOFF_FACTOR - throttleTakeOffRange)) + 1000;
                }
            }
        }

        private void SetYaw(double leftThumbstickX)
        {
            if (leftThumbstickX > STICKS_DEAD_ZONE || leftThumbstickX < -STICKS_DEAD_ZONE)
            {
                yaw = (leftThumbstickX * 500) + 1500;
            }
            else
            {
                yaw = 1500;
            }
        }

        private void SetPitch(double rightThumbstickY)
        {
            if(rightThumbstickY > STICKS_DEAD_ZONE || rightThumbstickY < -STICKS_DEAD_ZONE)
            {
                pitch = (rightThumbstickY * 500) + 1500;
            }
            else
            {
                pitch = 1500;
            }
        }

        private void SetRoll(double rightThumbstickX)
        {
            if (rightThumbstickX > STICKS_DEAD_ZONE || rightThumbstickX < -STICKS_DEAD_ZONE)
            {
                roll = (rightThumbstickX * 500) + 1500;
            }
            else
            {
                roll = 1500;
            }
        }

        private void arm_Checked(object sender, RoutedEventArgs e)
        {
            aux1 = 2000;
            Debug.WriteLine("--------- ARMED ---------");

            takeOffLandToggle.IsTapEnabled = true;
            takeOffLandToggle.Opacity = 1;
            //takeOffLandToggle.IsEnabled = true;
            //autoToggle.IsEnabled = true;
        }

        private void arm_Unchecked(object sender, RoutedEventArgs e)
        {
            aux1 = 1000;
            Debug.WriteLine("--------- DISARMED ---------");

            takeOffLandToggle.IsTapEnabled = false;
            if(takeOffLandToggleTapped)
                takeOffLandToggle_Tapped(this, null);
            takeOffLandToggle.Opacity = 0.2;
            //takeOffLandToggle.IsChecked = false;
            //takeOffLandToggle.IsEnabled = false;
            //autoToggle.IsChecked = false;
            //autoToggle.IsEnabled = false;
        }

        private void horizonToggle_Checked(object sender, RoutedEventArgs e)
        {
            flightModeCount = 0;
            aux2 = 1200;
            Debug.WriteLine("--------- HORIZON: ON ---------");
        }

        private void horizonToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            horizonToggle.Content = "Horizon";
            aux2 = 1500;
            Debug.WriteLine("--------- HORIZON: OFF ---------");
        }

        //private void takeOffLandToggle_Checked(object sender, RoutedEventArgs e)
        //{
        //    Autopilot.TakeOff();

        //    plusButton.IsEnabled = true;
        //    minusButton.IsEnabled = true;
        //}

        //private void takeOffLandToggle_Unchecked(object sender, RoutedEventArgs e)
        //{
        //    if (App.DroneType == DroneType.Custom && armToggle.IsChecked == true)
        //    {
        //        var cmd = Encoding.ASCII.GetBytes("#land");
        //        Socket.SendData(cmd);
        //    }
        //    else
        //    {
        //        Autopilot.Land();
        //    }

        //    plusButton.IsEnabled = false;
        //    minusButton.IsEnabled = false;
        //}

        //private void autoToggle_Checked(object sender, RoutedEventArgs e)
        //{
        //    var cmd = Encoding.ASCII.GetBytes("#autopilotON");
        //    Socket.SendData(cmd);
        //}

        //private void autoToggle_Unchecked(object sender, RoutedEventArgs e)
        //{
        //    var cmd = Encoding.ASCII.GetBytes("#autopilotOFF");
        //    Socket.SendData(cmd);
        //}

        private void plusButton_Click(object sender, RoutedEventArgs e)
        {
            if (isTakeOffCompleted && (THROTTLE_TAKEOFF_FACTOR + throttleTakeOffRange) < MAX_THROTTLE_TAKEOFF_FACTOR)
                throttleTakeOffRange += 5;
        }

        private void minusButton_Click(object sender, RoutedEventArgs e)
        {
            if (isTakeOffCompleted && (THROTTLE_TAKEOFF_FACTOR + throttleTakeOffRange) > THROTTLE_TAKEOFF_FACTOR)
                throttleTakeOffRange -= 5;
        }

		private void gpsHoldToggle_Checked(object sender, RoutedEventArgs e)
		{
            /*if(returnToHomeToggle.IsChecked == true)
                returnToHomeToggle.IsChecked = false;*/

            returnToHomeToggle_Tapped(this, null);

			aux3 = 2000;
			Debug.WriteLine("--------- GPS HOLD: ON ---------");
		}

		private void gpsHoldToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			aux3 = 1500;
			Debug.WriteLine("--------- GPS HOLD: OFF ---------");
		}

        //private void returnToHomeToggle_Checked(object sender, RoutedEventArgs e)
        //{
        //    /*if(gpsHoldToggle.IsChecked == true)
        //        gpsHoldToggle.IsChecked = false;*/
        //    aux3 = 1000;
        //    Debug.WriteLine("--------- RETURN TO HOME: ON ---------");
        //}

        //private void returnToHomeToggle_Unchecked(object sender, RoutedEventArgs e)
        //{
        //    aux3 = 1500;
        //    Debug.WriteLine("--------- RETURN TO HOME: OFF ---------");
        //}

        private void takeOffLandToggle_Checked()
        {
            if (takeOffLandToggleTapped)
                return;

            Autopilot.TakeOff();

            plusButton.IsEnabled = true;
            minusButton.IsEnabled = true;

            takeOffLandToggle.Source = new BitmapImage(new Uri("ms-appx:///Assets/Icons/land.png"));
            takeOffLandToggleTapped = true;
        }

        private void takeOffLandToggle_Unchecked()
        {
            if (!takeOffLandToggleTapped)
                return;

            if (App.DroneType == DroneType.Custom && armToggle.IsChecked == true)
            {
                var cmd = Encoding.ASCII.GetBytes("#land");
                Socket.SendData(cmd);

                takeOffLandToggle.Opacity = 0.2;
            }
            else
            {
                Autopilot.Land();
            }

            plusButton.IsEnabled = false;
            minusButton.IsEnabled = false;
            takeOffLandToggleTapped = false;
        }

        private void takeOffLandToggle_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Autopilot.IsInControl)
                return;

            if (!takeOffLandToggleTapped)
            {
                takeOffLandToggle_Checked();
            }
            else
            {
                takeOffLandToggle_Unchecked();
            }
        }

        private void returnToHomeToggle_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!returnToHomeToggleTapped)
            {
                /*if(gpsHoldToggle.IsChecked == true)
                    gpsHoldToggle.IsChecked = false;*/
                aux3 = 1000;
                Debug.WriteLine("--------- RETURN TO HOME: ON ---------");

                returnToHomeToggle.StrokeThickness = 5;
            }
            else
            {
                aux3 = 1500;
                Debug.WriteLine("--------- RETURN TO HOME: OFF ---------");

                returnToHomeToggle.StrokeThickness = 0;
            }

            returnToHomeToggleTapped = !returnToHomeToggleTapped;
        }

        private void SetGpsData()
        {
            if (!App.isRPi)
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // UI update
                    if (MultiWii.gpsFix == 2)
                    {
                        gpsStatus.Opacity = 1;
                        //gpsHoldToggle.IsEnabled = true;
                        //returnToHomeToggle.IsEnabled = true;
                        returnToHomeToggle.IsTapEnabled = true;
                        returnToHomeToggle.Opacity = 1;
                    }
                    else
                    {
                        gpsStatus.Opacity = 0.2;
                        //gpsHoldToggle.IsEnabled = false;
                        //returnToHomeToggle.IsEnabled = false;
                        returnToHomeToggle.IsTapEnabled = false;
                        returnToHomeToggle.Opacity = 0.2;
                    }

                    var speed = MultiWii.gpsSpeed * 0.036; // convert cm/s to km/h
                    speedLabel.Text = $"S:{speed} Km/h";
                    altitudeLabel.Text = $"A:{MultiWii.gpsAltitude} m";
                    numSatLabel.Text = $"{MultiWii.gpsNumSat}";

                    dronePosition = new Geopoint(new BasicGeoposition() { Latitude = MultiWii.gpsLatitude, Longitude = MultiWii.gpsLongitude, Altitude = MultiWii.gpsAltitude });
                    map.Center = dronePosition;
                    map.ZoomLevel = 17;

                    MapControl.SetLocation(droneIcon, dronePosition);
                    MapControl.SetNormalizedAnchorPoint(droneIcon, new Point(0.5, 1));
                });
            }
        }

        private void SendTelemetryData()
        {
            if (App.isRPi)
            {
                var speed = MultiWii.gpsSpeed * 0.036; // convert cm/s to km/h
                var message = new TelemetryMessage(MultiWii.gpsFix, MultiWii.gpsNumSat, MultiWii.gpsLatitude, MultiWii.gpsLongitude, MultiWii.gpsAltitude, speed, MultiWii.gpsGroundCourse, MultiWii.angx, MultiWii.angy, MultiWii.head, MultiWii.headfree, MultiWii.signalStrength, MultiWii.usDistance, MultiWii.batteryVoltage, MultiWii.batteryPercentage);
                Telemetry.SendMessagesAsync(message);
            }
        }

        private void SetAttitudeData() 
		{
			if(!App.isRPi) 
			{
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // UI update
                    angxLabel.Text = $"ANGX: {MultiWii.angx}";
                    angyLabel.Text = $"ANGY: {MultiWii.angy}";
                    headLabel.Text = $"HEAD: {MultiWii.head}";
                });
			}
		}

        private void SetBatteryData()
        {
            if (!App.isRPi)
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // UI update
                    battLoadLabel.Text = $"L: {MultiWii.batteryLoad}";
                    battVoltageLabel.Text = $"V: {MultiWii.batteryVoltage}";
                    battCurrentLabel.Text = $"A: {MultiWii.batteryCurrent}";
                    battPercentageLabel.Text = $"%: {MultiWii.batteryPercentage}";
                });
            }
        }

        private void SetSignalStrengthData() 
		{
			if (App.isRPi)
			{
                var signal = Telemetry.GetSignalStrength();
                MultiWii.signalStrength = signal;

                var bytes = MultiWii.MSP_SIGNAL_STRENGTH(signal);
                Socket.SendData(bytes);
            }
			else
			{
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // UI update
                    signalStrengthLabel.Text = $"SS:{MultiWii.signalStrength}";
                });
			}
		}

        private void SetUltrasonicSensorData()
        {
            if (App.isRPi)
            {
                var bytes = MultiWii.MSP_ULTRASONIC_SENSOR((int)MultiWii.usDistance);
                Socket.SendData(bytes);
            }
            else
            {
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // UI update
                    var distance = MultiWii.usDistance;
                    distanceLabel.Text = $"D:{distance}cm";
                });
            }
        }

        private static void TurnOnLED()
        {
            if (!App.isRPi)
                return;

            if (ledPinValue == GpioPinValue.Low)
            {
                ledPinValue = GpioPinValue.High;
                ledPin.Write(ledPinValue);
            }
        }

        private static void TurnOffLED()
        {
            if (!App.isRPi)
                return;

            if (ledPinValue == GpioPinValue.High)
            {
                ledPinValue = GpioPinValue.Low;
                ledPin.Write(ledPinValue);
            }
        }

        public static void FlipLED()
        {
            if (!App.isRPi)
                return;

            if (ledPinValue == GpioPinValue.Low)
            {
                ledPinValue = GpioPinValue.High;
                ledPin.Write(ledPinValue);
            }
            else
            {
                ledPinValue = GpioPinValue.Low;
                ledPin.Write(ledPinValue);
            }
        }

        public static async void BlinkLED(int ms, int times)
        {
            if (!App.isRPi)
                return;

            times = times * 2;

            if (ledPinValue == GpioPinValue.Low)
            {
                while (times != 0)
                {
                    FlipLED();
                    await Task.Delay(ms);
                    times--;
                }

                TurnOffLED();
            }
            else
            {
                while (times != 0)
                {
                    FlipLED();
                    await Task.Delay(ms);
                    times--;
                }

                TurnOnLED();
            }
        }
    }
}
