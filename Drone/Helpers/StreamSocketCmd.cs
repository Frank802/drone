using System;
using System.Diagnostics;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Networking;
using Windows.UI.Popups;
using System.Collections.Generic;
using Windows.Foundation;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;
using System.IO;

namespace Drone.Helpers
{
	public static class StreamSocketCmd
	{
		private static string hostName = "";
		private const string hostPort = "8027";
        private static StreamSocketListener listener;
        private static DataReader readPacket;
        private static int readBuff = 64;

        public static bool socketIsConnected = false;

		public static long lastCmdSent;
		public static long lastCmdReceived;

		public static async Task NetworkInit(string host)
		{
			ClearPrevious();

			hostName = host;
			Debug.WriteLine("NetworkInit() host = " + hostName + " port = " + hostPort);

            // if no host, be client, otherwise be a host
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                await InitConnectionToHost(hostName, hostPort);
            }
            else
            {
                if (listener == null)
                    await StartListener(hostPort);
            }
		}

        #region ----- helpers -----	
        public static void SendDataToHost(string stringToSend)
        {
            if (socketIsConnected) PostSocketWrite(stringToSend);
        }

        static async void PostSocketWrite(string writeStr)
        {
            if (socket == null || !socketIsConnected)
            {
                Debug.WriteLine("Wr: Socket not connected yet.");
                return;
            }

            try
            {
                DataWriter writer = new DataWriter(socket.OutputStream);
                writer.WriteString(writeStr);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
                writer = null;
			}
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to Write - " + ex.Message);
            }
        }

        public static void SendDataToHost(byte[] bytes)
        {
            if (socketIsConnected) PostSocketWrite(bytes);
        }

        static async void PostSocketWrite(byte[] bytes)
        {
            if (socket == null || !socketIsConnected)
            {
                Debug.WriteLine("Wr: Socket not connected yet.");
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
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to Write - " + ex.Message);
                socketIsConnected = false;

                if (App.isRPi)
                {
                    Debug.WriteLine("Controller signal lost!");
                    //MainPage.Safe();
                }

                if (!App.isRPi)
                {
                    Debug.WriteLine("Drone signal lost!");

                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                        var droneStatus = currentPage.FindName("droneStatus") as Image;
                        droneStatus.Opacity = 0.2;
                    });
                }
            }
        }

        static void PostSocketRead(int length)
        {
            if (socket == null || !socketIsConnected)
            {
                Debug.WriteLine("Rd: Socket not connected yet.");
                return;
            }

            try
            {
                var readBuf = new Windows.Storage.Streams.Buffer((uint)length);
                var readOp = socket.InputStream.ReadAsync(readBuf, (uint)length, InputStreamOptions.Partial);
                readOp.Completed = (IAsyncOperationWithProgress<IBuffer, uint> asyncAction, AsyncStatus asyncStatus) =>
                {
                    switch (asyncStatus)
                    {
                        case AsyncStatus.Completed:
                        case AsyncStatus.Error:
                            try
                            {
                                IBuffer localBuf = asyncAction.GetResults();
                                uint bytesRead = localBuf.Length;
                                readPacket = DataReader.FromBuffer(localBuf);
                                OnDataReadCompletion(bytesRead, readPacket);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("Read operation failed:  " + ex.Message);
                            }
                            break;
                        case AsyncStatus.Canceled:
                            Debug.WriteLine("Read operation cancelled");
                            break;
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to post a Read - " + ex.Message);
            }
        }

        public static void OnDataReadCompletion(uint bytesRead, DataReader readPacket)
        {
            try
            {
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

                Debug.WriteLine($"Network Received (b={bytesRead},l={buffLen})");

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
                    MultiWii.sendRequestMSP(bytes);
                    PostSocketRead(readBuff);
                }
                else
                {
                    MultiWii.evaluateResponseMSP(bytes);
                    PostSocketRead(readBuff);
                }
			}
            catch(Exception ex)
            {
                Debug.WriteLine("OnDataReadCompletion() - " + ex.Message);
            }
        }

        private static void ClearPrevious()
		{
			if (socket != null)
			{
				socket.Dispose();
				socket = null;
				socketIsConnected = false;
			}
		}
		#endregion

		#region ----- host connection ----
		public static bool listenerHasStarted;
		private static async Task StartListener(string port)
		{
			try
			{
				listener = new StreamSocketListener();
				listener.ConnectionReceived += OnConnection;
				await listener.BindServiceNameAsync(port).AsTask();
				Debug.WriteLine("Listening on port " + port);
				listenerHasStarted = true;
			}
			catch (Exception e)
			{
				Debug.WriteLine("StartListener() - Unable to bind listener. " + e.Message);
                MainPage.BlinkLED(250, 2);
            }
		}

		private static void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
		{
			try
			{
				if (App.isRPi)
				{
                    MainPage.BlinkLED(250, 1);
                    
                    socket = args.Socket;
                    if (socket != null)
                    {
                        socketIsConnected = true;

						// Start reading controller commands
						PostSocketRead(readBuff);
                    }
                }
            }
            catch (Exception ex)
			{
				Debug.WriteLine("OnConnection() - " + ex.Message);
			}
		}
		#endregion

		#region ----- client connection -----
		static StreamSocket socket;
		private static async Task InitConnectionToHost(string host, string port)
		{
			try
			{
                ClearPrevious();
                socket = new StreamSocket();

                HostName hostNameObj = new HostName(host);
				await socket.ConnectAsync(hostNameObj, port).AsTask();
				Debug.WriteLine("Connected to " + hostNameObj + ":" + port);
				socketIsConnected = true;

                // Start listening feedback from drone
                if (!App.isRPi)
                    PostSocketRead(readBuff);

                if (!App.isRPi)
                {
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
				Debug.WriteLine("InitConnectionToHost() - " + ex.Message);
				if (!App.isRPi)
					await new MessageDialog("InitConnectionToHost() - " + ex.Message).ShowAsync();
			}
		}		
		#endregion		
	}
}
