using Drone.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone
{
    public static class Camera
    {
        public static Mode CurrentMode;

        public static bool socketIsConnected
        {
            get
            {
				if (CurrentMode == Mode.WiFiUDP)
					return DatagramSocketCamera.socketIsConnected;
				if (CurrentMode == Mode.Cellular)
					return WebSocketCamera.socketIsConnected;

				return false;
			}
		}

		public static async Task Init()
		{
			CurrentMode = App.Mode;

			if (CurrentMode == Mode.WiFiUDP)
			{
				await DatagramSocketCamera.Init();
			}

			if (CurrentMode == Mode.Cellular)
			{
				await WebSocketCamera.Init();
			}
		}

		public static void StartCapture()
		{
			if (CurrentMode == Mode.WiFiUDP)
			{
				DatagramSocketCamera.StartCapture();
			}

			if (CurrentMode == Mode.Cellular)
			{
				WebSocketCamera.StartCapture();
			}
		}

		public static void TakePhoto()
		{
			if (CurrentMode == Mode.WiFiUDP)
			{
				DatagramSocketCamera.TakePhoto();
			}

			if (CurrentMode == Mode.Cellular)
			{
				WebSocketCamera.TakePhoto();
			}
		}

        public static void LoadPhoto()
        {
			if (CurrentMode == Mode.WiFiUDP)
			{
				DatagramSocketCamera.LoadPhoto();
			}

			if (CurrentMode == Mode.Cellular)
			{
				WebSocketCamera.LoadPhoto();
			}
		}
    }
}
