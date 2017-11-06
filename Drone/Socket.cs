using Drone.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone
{
    public static class Socket
    {
        public static Mode CurrentMode;
        public static bool socketIsConnected
        {
            get
            {
                if (CurrentMode == Mode.WiFiTCP)
					return StreamSocketCmd.socketIsConnected;
				if (CurrentMode == Mode.WiFiUDP)
					return DatagramSocketCmd.socketIsConnected;
                if (CurrentMode == Mode.Cellular)
                    return WebSocketCmd.socketIsConnected;

                return false;
            }
        }

        public static async Task Init()
        {
            CurrentMode = App.Mode;

            if (CurrentMode == Mode.Cellular)
            {
                if (App.isRPi)
                    await WebSocketCmd.OpenSocket("drone", "controller");
                else
                    await WebSocketCmd.OpenSocket("controller", App.DroneName);
            }
			else
			{
				var hostName = string.Empty;

				if (!App.isRPi)
					hostName = App.HostName;

				if (CurrentMode == Mode.WiFiTCP)
					await StreamSocketCmd.NetworkInit(hostName);
				if (CurrentMode == Mode.WiFiUDP)
					await DatagramSocketCmd.NetworkInit(hostName);
			}
        }

        public static void SendData(byte[] bytes)
        {
            if(CurrentMode == Mode.WiFiTCP)
				StreamSocketCmd.SendDataToHost(bytes);
			if (CurrentMode == Mode.WiFiUDP)
				DatagramSocketCmd.SendDataToHost(bytes);
            if (CurrentMode == Mode.Cellular) 
                WebSocketCmd.SendDataToHost(bytes);          
        }
    }
}
