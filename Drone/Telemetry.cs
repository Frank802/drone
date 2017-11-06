using Drone.Helpers;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace Drone
{
    public class Telemetry
    {
        private static DeviceClient deviceClient;
        private static string iotHubUri = "DroneRemote.azure-devices.net";
        private static string deviceKey = "whzlXZRp/qw8aI6gIaWBLrSJi5Bs1R2sezpi/9YIcS8=";
        private static string deviceId = "Drone";
		private static bool iotHubIsConnected = false;

		public static void Init()
        {
            try
            {
                deviceClient = DeviceClient.Create(iotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey(deviceId, deviceKey), TransportType.Mqtt);
				Debug.WriteLine("Connected to IoTHub");
				iotHubIsConnected = true;
			}
            catch (Exception ex)
            {
                Debug.WriteLine("Error during telemetry init: " + ex.Message);
            }
        }

        public static void SendMessagesAsync(ITelemetryMessage message)
        {
			if (!iotHubIsConnected)
				return;

            var data = new Message(Encoding.ASCII.GetBytes(message.GetJSON()));

            try
            {
                deviceClient.SendEventAsync(data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error sending telemetry message to Azure: " + ex.Message);
            }
        }

		public static int GetSignalStrength() 
		{
			var connectionProfile = NetworkInformation.GetInternetConnectionProfile();
			var signal = connectionProfile.GetSignalBars();

			if (signal.HasValue)
				return (int)signal;
			else
				return 0;
		}
	}
}
