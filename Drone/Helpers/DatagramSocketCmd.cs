using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Drone.Helpers
{
    public static class DatagramSocketCmd
    {
        private static string hostName = "";
        private const string hostPort = "8027";
        private static DatagramSocket socket;
        private static DatagramSocket listener;
        private static RemotePeer peer;

        public static bool socketIsConnected = false;
        public static bool listenerHasStarted = false;

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
                await ConnectToHost(hostName, hostPort);
            }
            else
            {
                if (listener == null)
                    await StartListener(hostPort);
            }
        }

        public static void SendDataToHost(string stringToSend)
        {
            if (socketIsConnected) PostSocketWrite(stringToSend);
        }

        private static async void PostSocketWrite(string writeStr)
        {
            if (!App.isRPi && socket == null || !socketIsConnected)
            {
                Debug.WriteLine("Wr: Socket not connected yet.");
                return;
            }

            if (App.isRPi && peer == null || !socketIsConnected)
            {
                Debug.WriteLine("Wr: Socket not connected yet.");
                return;
            }

            try
            {
                DataWriter writer;
                if (App.isRPi)
                    writer = new DataWriter(peer.OutputStream);
                else
                    writer = new DataWriter(socket.OutputStream);

                writer.WriteString(writeStr);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
                writer = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to Write - " + ex.Message);
                socketIsConnected = false;
            }
        }

        public static void SendDataToHost(byte[] bytes)
        {
            if (socketIsConnected) PostSocketWrite(bytes);
        }

        private static async void PostSocketWrite(byte[] bytes)
        {
            if (!App.isRPi && socket == null || !socketIsConnected)
            {
                Debug.WriteLine("Wr: Socket not connected yet.");
                return;
            }

            if (App.isRPi && peer == null || !socketIsConnected)
            {
                Debug.WriteLine("Wr: Socket not connected yet.");
                return;
            }

            try
            {
                DataWriter writer;
                if (App.isRPi)
                    writer = new DataWriter(peer.OutputStream);
                else
                    writer = new DataWriter(socket.OutputStream);

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

        private static void ClearPrevious()
        {
            if (socket != null)
            {
                socket.Dispose();
                socket = null;
                socketIsConnected = false;
            }
        }

        private static async Task StartListener(string port)
        {
            try
            {
                listener = new DatagramSocket();
                listener.MessageReceived += MessageReceived;
                await listener.BindServiceNameAsync(port).AsTask();
                Debug.WriteLine("Listening on port " + port);
                listenerHasStarted = true;
            }
            catch (Exception e)
            {
                Debug.WriteLine("StartListener() - Unable to bind listener. " + e.Message);
                if (App.isRPi)
                    MainPage.BlinkLED(250, 2);
            }
        }

        private static async Task ConnectToHost(string host, string port)
        {
            try
            {
                ClearPrevious();
                socket = new DatagramSocket();
                socket.MessageReceived += MessageReceived;

                HostName hostNameObj = new HostName(host);
                await socket.ConnectAsync(hostNameObj, port).AsTask();
                Debug.WriteLine("Connected to " + hostNameObj + ":" + port);
                socketIsConnected = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("InitConnectionToHost() - " + ex.Message);
                if (!App.isRPi)
                    await new MessageDialog("InitConnectionToHost() - " + ex.Message).ShowAsync();
            }
        }

        private static async void MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {
                if (App.isRPi && peer == null)
                {
                    IOutputStream outputStream = await sender.GetOutputStreamAsync(args.RemoteAddress, args.RemotePort);

                    // It might happen that the OnMessage was invoked more than once before the GetOutputStreamAsync call
                    // completed. In this case we will end up with multiple streams - just keep one of them.
                    object syncRoot = new object();
                    lock (syncRoot)
                    {
                        peer = new RemotePeer(outputStream, args.RemoteAddress, args.RemotePort);
                    }

                    socketIsConnected = true;
                }

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
                Debug.WriteLine("OnConnection() - " + ex.Message);
            }
        }
    }
}
