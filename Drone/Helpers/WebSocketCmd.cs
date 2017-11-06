using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Drone.Helpers
{
	public static class WebSocketCmd
	{
		private static MessageWebSocket socket;
		public static bool socketIsConnected = false;

        public static long lastCmdSent;
		public static long lastCmdReceived;

		private static string from;
		private static string to;

		private static void Init()
		{
			try
			{
				socket = new MessageWebSocket();
				socket.MessageReceived += MessageWebSocket_MessageReceived;
				socket.Closed += MessageWebSocket_Closed;
				socket.Control.MessageType = SocketMessageType.Binary;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Error initializing web socket: " + ex.Message);
                HandleException(ex.Message);
			}
		}

		private static void MessageWebSocket_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
		{			
            Debug.WriteLine($"Socket closed: {args.Reason}");
			socketIsConnected = false;
            HandleError(args.Reason);
        }

		private static async void MessageWebSocket_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
		{
			try
			{
				var readPacket = args.GetDataReader();

				if (readPacket == null)
				{
					Debug.WriteLine("DataReader is null");
					return;
				}

				uint buffLen = readPacket.UnconsumedBufferLength;

				if (buffLen == 0)
				{
					Debug.WriteLine("Buffer is empty");
					return;
				}

                List<byte> bytes = new List<byte>();
				while (buffLen > 0)
				{
					byte b = readPacket.ReadByte();
					bytes.Add(b);
					buffLen--;
				}

				lastCmdReceived = MainPage.stopwatch.ElapsedMilliseconds;
                
                if (App.isRPi)
                {
                    if (bytes[0] == '#')
                        MultiWii.evaluateCustomCommand(bytes);
                    else
                        MultiWii.sendRequestMSP(bytes);
                }
                else
                {
                    if (bytes[0] == '#')
                        MultiWii.evaluateCustomCommand(bytes);
                    else
                        MultiWii.evaluateResponseMSP(bytes);

                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                        var droneStatus = currentPage.FindName("droneStatus") as Image;
                        droneStatus.Opacity = 1;
                    });
                }
            }
			catch (Exception ex)
			{
				Debug.WriteLine("MessageWebSocket_MessageReceived() - " + ex.Message);
                HandleException(ex.Message);
			}
		}

		public static async Task OpenSocket(string f, string t)
		{
			if (socket != null)
			{
				CloseSocket();
			}

			Init();

			from = f;
			to = t;

			try
			{
				await socket.ConnectAsync(new Uri($"ws://{App.ServiceUri}/ws/?from={from}&to={to}"));
				socketIsConnected = true;

				if (!App.isRPi)
				{
					await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
					{
						var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
						var serviceStatus = currentPage.FindName("serviceStatus") as Image;
                        serviceStatus.Opacity = 1;
                    });
				}
				else
					Debug.WriteLine("Web socket connected!");

			}
			catch (Exception ex)
			{
				socket.Dispose();
				socket = null;
				socketIsConnected = false;
				Debug.WriteLine("Error connecting to web socket: " + ex.Message);
                HandleException(ex.Message);
            }
		}

		public static void SendDataToHost(byte[] bytes)
		{
			if (socketIsConnected) PostSocketWrite(bytes);
		}

		private static async void PostSocketWrite(byte[] bytes)
		{
			if (!socketIsConnected)
			{
				Debug.WriteLine("Socket is not connected!");
				return;
			}

			try
			{
				DataWriter writer = new DataWriter(socket.OutputStream);
				writer.WriteBytes(bytes);
				await writer.StoreAsync();
				await writer.FlushAsync();
				writer.DetachStream();
				writer = null;
				lastCmdSent = MainPage.stopwatch.ElapsedMilliseconds;
			}
			catch (ObjectDisposedException ex)
			{
				Debug.WriteLine($"Error sending data: {ex.Message} Restarting connection...");
				await RestartSocket();
				Debug.WriteLine("Connection restarted successfully");
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Failed to Write - " + ex.Message);
				socketIsConnected = false;
                HandleException(ex.Message);
            }
		}

		public static async Task RestartSocket()
		{
			Init();

			try
			{
				await socket.ConnectAsync(new Uri($"ws://{App.ServiceUri}/ws/?from={from}&to={to}"));
				socketIsConnected = true;

                if (!App.isRPi)
                {
                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                        var serviceStatus = currentPage.FindName("serviceStatus") as Image;
                        serviceStatus.Opacity = 1;
                    });
                }
                else
                    Debug.WriteLine("Web socket reconnected!");
            }
			catch (Exception ex)
			{
				Debug.WriteLine("Error restarting web socket: " + ex.Message);
				socketIsConnected = false;
                HandleException(ex.Message);
            }
		}

		public static async void CloseSocket()
		{
			if (!socketIsConnected)
				return;

			try
			{
				socket.Close(1000, "Closed due to user request.");
				socket.Dispose();
				socket = null;
				socketIsConnected = false;

				if (!App.isRPi)
				{
					await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
					{
						var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                        var serviceStatus = currentPage.FindName("serviceStatus") as Image;
                        serviceStatus.Opacity = 0.2;
                        var droneStatus = currentPage.FindName("droneStatus") as Image;
                        droneStatus.Opacity = 0.2;
                    });
				}
				else
					Debug.WriteLine("Web socket closed!");
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Error closing socket: " + ex.Message);
			}
		}

		private static async void HandleError(string error) 
		{
			if (App.isRPi)
			{
				Debug.WriteLine($"Controller is no longer connected to the web app: {error}");
			}
			else
			{
				Debug.WriteLine($"Drone is no longer connected to the web app: {error}");

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var droneStatus = currentPage.FindName("droneStatus") as Image;
                    droneStatus.Opacity = 0.2;
                });
            }

            await RestartSocket();
		}

        private static async void HandleException(string error)
        {
            if (App.isRPi)
            {
                Debug.WriteLine($"Drone is no longer connected to the web app: {error}");
            }
            else
            {
                Debug.WriteLine($"Controller is no longer connected to the web app: {error}");

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var serviceStatus = currentPage.FindName("serviceStatus") as Image;
                    serviceStatus.Opacity = 0.2;
                    var droneStatus = currentPage.FindName("droneStatus") as Image;
                    droneStatus.Opacity = 0.2;
                });
            }

            if(App.internetAccess)
                await RestartSocket();
        }
    }
}
