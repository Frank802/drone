using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Drone
{
    public static class MultiWii
    { 
        private static SerialDevice serialPort = null;
        private static List<DeviceInformation> listOfDevices = new List<DeviceInformation>();
		public static bool deviceIsConnected = false;

        public static long lastCmdSent;
		public static long lastCmdReceived;

		private const string MSP_HEADER = "$M<";

        private const int BATTERY_CELLS = 3;
        private const double MIN_CELL_BATTERY_VOLTAGE = 3.3;
        private const double MAX_CELL_BATTERY_VOLTAGE = 4.2;

        public static int version, multiType, mspVersion, capability, signalStrength, batteryLoad, batteryPercentage;
        public static int roll, pitch, yaw, throttle, aux1, aux2, aux3, aux4;
        public static int gpsFix, gpsNumSat, gpsSpeed, gpsGroundCourse;
        public static double gpsLatitude, gpsLongitude, gpsAltitude, usDistance, batteryVoltage, batteryCurrent;
        public static float angx, angy, head, headfree, alt;

        private enum MSPConst
        {
            MSP_IDENT = 100,
            MSP_STATUS = 101,
            MSP_RAW_IMU = 102,
            MSP_SERVO = 103,
            MSP_MOTOR = 104,
            MSP_RC = 105,
            MSP_RAW_GPS = 106,
            MSP_COMP_GPS = 107,
            MSP_ATTITUDE = 108,
            MSP_ALTITUDE = 109,
            MSP_ANALOG = 110,
            MSP_RC_TUNING = 111,
            MSP_PID = 112,
            MSP_BOX = 113,
            MSP_MISC = 114,
            MSP_MOTOR_PINS = 115,
            MSP_BOXNAMES = 116,
            MSP_PIDNAMES = 117,
            MSP_SUPERRAW_IMU = 119,

            MSP_SET_RAW_SENSORS = 199,
            MSP_SET_RAW_RC = 200,
            MSP_SET_RAW_GPS = 201,
            MSP_SET_PID = 202,
            MSP_SET_BOX = 203,
            MSP_SET_RC_TUNING = 204,
            MSP_ACC_CALIBRATION = 205,
            MSP_MAG_CALIBRATION = 206,
            MSP_SET_MISC = 207,
            MSP_RESET_CONF = 208,

            MSP_VOLTAGE_METERS = 128,
            MSP_CURRENT_METERS = 129,
            MSP_BATTERY_STATE = 130,    

            MSP_EEPROM_WRITE = 250,

            MSP_DEBUG = 254,

            // Custom messages
            MSP_SIGNAL_STRENGTH = 150,
            MSP_ULTRASONIC_SENSOR = 151
        }

        private const int
          IDLE = 0,
          HEADER_START = 1,
          HEADER_M = 2,
          HEADER_ARROW = 3,
          HEADER_SIZE = 4,
          HEADER_CMD = 5,
          HEADER_ERR = 6;

        private static byte[] inBuf = new byte[256];
        private static int c_state = IDLE;
        private static bool err_rcvd = false;

        private static byte checksum = 0;
        private static byte cmd;
        private static int offset = 0, dataSize = 0;

        public static async Task Init()
        {
            try
            {
                await ListAvailablePorts();
                var deviceName = "CP2104 USB to UART Bridge Controller";
                //var deviceName = "Silicon Labs CP210x USB to UART Bridge (COM3)";
                var device = listOfDevices.Where(x => x.Name == deviceName).FirstOrDefault();
				if (device != null)
				{
					await OpenConnection(device.Id);
					deviceIsConnected = true;
				}
				else
				{
					deviceIsConnected = false;
					throw new Exception($"Device {deviceName} not found!");
				}

                Listen();
            }
            catch(Exception ex)
            {
                MainPage.BlinkLED(250, 4);
                Debug.WriteLine(ex.Message);
            }
        }

        private static async Task ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();

                var dis = await DeviceInformation.FindAllAsync(aqs);

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

				if (listOfDevices.Count < 1)
					throw new Exception("Error finding usb connected devices");
            }
            catch (Exception ex)
            {
                MainPage.BlinkLED(250, 3);
                Debug.WriteLine(ex.Message);
			}
        }

        private static async Task OpenConnection(string deviceID)
        {
            CloseConnection();
            serialPort = await SerialDevice.FromIdAsync(deviceID);
            serialPort.BaudRate = 115200;
            serialPort.WriteTimeout = TimeSpan.FromMilliseconds(10);
            serialPort.ReadTimeout = TimeSpan.FromMilliseconds(10);
            serialPort.Parity = SerialParity.None;
            serialPort.StopBits = SerialStopBitCount.One;
            serialPort.DataBits = 8;
            serialPort.IsRequestToSendEnabled = false;
            serialPort.IsDataTerminalReadyEnabled = false;
        }

        private static void CloseConnection()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
            }

            serialPort = null;
        }

        private static int p;

        private static int read32() { return (Int32)((inBuf[p++] & 0xff) + ((inBuf[p++] & 0xff) << 8) + ((inBuf[p++] & 0xff) << 16) + ((inBuf[p++] & 0xff) << 24)); }
        private static int read16() { return (Int16)((inBuf[p++] & 0xff) + ((inBuf[p++]) << 8)); }
        private static int read8() { return inBuf[p++] & 0xff; }

        //send msp without payload
        private static List<Byte> requestMSP(int msp)
        {
            return requestMSP(msp, null);
        }

        //send multiple msp without payload
        private static List<Byte> requestMSP(int[] msps)
        {
            List<Byte> s = new List<Byte>();
            foreach (int m in msps)
            {
                s.AddRange(requestMSP(m, null));
            }
            return s;
        }

        //send msp with payload
        private static List<Byte> requestMSP(int msp, byte[] payload)
        {
            if (msp < 0)
            {
                return null;
            }
            List<Byte> bf = new List<Byte>();
            foreach (byte c in MSP_HEADER.ToCharArray())
            {
                bf.Add(c);
            }

            byte checksum = 0;
            byte pl_size = (byte)((payload != null ? (int)(payload.Length) : 0) & 0xFF);
            bf.Add(pl_size);
            checksum ^= (byte)(pl_size & 0xFF);

            bf.Add((byte)(msp & 0xFF));
            checksum ^= (byte)(msp & 0xFF);

            if (payload != null)
            {
                foreach (byte b in payload)
                {
                    bf.Add((byte)(b & 0xFF));
                    checksum ^= (byte)(b & 0xFF);
                }
            }
            bf.Add(checksum);
            return (bf);
        }

        private static List<Byte> requestMSP(string header, int msp, byte[] payload)
        {
            if (msp < 0)
            {
                return null;
            }
            List<Byte> bf = new List<Byte>();
            foreach (byte c in header.ToCharArray())
            {
                bf.Add(c);
            }

            byte checksum = 0;
            byte pl_size = (byte)((payload != null ? (int)(payload.Length) : 0) & 0xFF);
            bf.Add(pl_size);
            checksum ^= (byte)(pl_size & 0xFF);

            bf.Add((byte)(msp & 0xFF));
            checksum ^= (byte)(msp & 0xFF);

            if (payload != null)
            {
                foreach (byte b in payload)
                {
                    bf.Add((byte)(b & 0xFF));
                    checksum ^= (byte)(b & 0xFF);
                }
            }
            bf.Add(checksum);
            return (bf);
        }

        public static void sendRequestMSP(List<Byte> msp)
        {
            byte[] arr = new byte[msp.Count];
            int i = 0;
            foreach (byte b in msp)
            {
                arr[i++] = b;
            }

            Write(arr); // send the complete byte sequence in one go
        }

        public static void parseByte(byte c) {
            if (c_state == IDLE)
            {
                c_state = (c == '$') ? HEADER_START : IDLE;
            }
            else if (c_state == HEADER_START)
            {
                c_state = (c == 'M') ? HEADER_M : IDLE;
            }
            else if (c_state == HEADER_M)
            {
                if (c == '>')
                {
                    c_state = HEADER_ARROW;
                }
                else if (c == '!')
                {
                    c_state = HEADER_ERR;
                }
                else
                {
                    c_state = IDLE;
                }
            }
            else if (c_state == HEADER_ARROW || c_state == HEADER_ERR)
            {
                /* is this an error message? */
                err_rcvd = (c_state == HEADER_ERR);        /* now we are expecting the payload size */
                dataSize = (c & 0xFF);
                /* reset index variables */
                p = 0;
                offset = 0;
                checksum = 0;
                checksum ^= (byte)(c & 0xFF);
                /* the command is to follow */
                c_state = HEADER_SIZE;
            }
            else if (c_state == HEADER_SIZE)
            {
                cmd = (byte)(c & 0xFF);
                checksum ^= (byte)(c & 0xFF);
                c_state = HEADER_CMD;
            }
            else if (c_state == HEADER_CMD && offset < dataSize)
            {
                checksum ^= (byte)(c & 0xFF);
                inBuf[offset++] = (byte)(c & 0xFF);
            }
            else if (c_state == HEADER_CMD && offset >= dataSize)
            {
                /* compare calculated and transferred checksum */
                if ((checksum & 0xFF) == (c & 0xFF))
                {
                    if (err_rcvd)
                    {
                        Debug.WriteLine("Drone did not understand request type " + c);
                    }
                    else
                    {
                        /* we got a valid response packet, evaluate it */
                        evaluateCommand(cmd, dataSize);
                    }
                }
                else
                {
                    Debug.WriteLine("invalid checksum for command " + ((int)(cmd & 0xFF)) + ": " + (checksum & 0xFF) + " expected, got " + (int)(c & 0xFF));
                }
                c_state = IDLE;
            }
        }

        private static async void Listen()
        {
            try
            {
                if (serialPort != null)
                {
                    // Create the DataReader object and attach to InputStream 
                    DataReader reader = new DataReader(serialPort.InputStream);

                    while (true)
                    {
                        //Launch the ReadBytesAsync task to perform the read
                        await ReadBytesAsync(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private static async void Read()
        {
            try
            {
                if (serialPort != null)
                {
                    DataReader reader = new DataReader(serialPort.InputStream);
                    await ReadBytesAsync(reader);
                    reader.DetachStream();
                    reader = null;
				}
            }
            catch (Exception ex)
            {              
                Debug.WriteLine(ex.Message);
            }
        }

        private static async Task ReadBytesAsync(DataReader reader)
        {
            uint ReadBufferLength = (uint)inBuf.Length;

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            reader.InputStreamOptions = InputStreamOptions.Partial;

            var data = new List<byte>();

            // Launch the task and wait
            var bytesRead = await reader.LoadAsync(ReadBufferLength);         
            while (bytesRead > 0)
            {
                var b = reader.ReadByte();
                data.Add(b);
                parseByte(b);
                bytesRead--;
            }

			lastCmdReceived = MainPage.stopwatch.ElapsedMilliseconds;

			if (App.isRPi)
                Socket.SendData(data.ToArray());
        }

        private static async void Write(byte[] value)
        {
            try
            {
                if (serialPort != null)
                {
                    DataWriter writer = new DataWriter(serialPort.OutputStream);
                    await WriteBytesAsync(writer, value);
                    writer.DetachStream();
                    writer = null;
				}
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private static async Task WriteBytesAsync(DataWriter writer, byte[] value)
        {
            writer.WriteBytes(value);
            await writer.StoreAsync();
			lastCmdSent = MainPage.stopwatch.ElapsedMilliseconds;
		}

        public static void evaluateResponseMSP(List<byte> data)
        {
            foreach (byte b in data)
            {
                parseByte(b);
            }
        }

        private static void evaluateCommand(byte cmd, int dataSize)
        {
            int icmd = (int)(cmd & 0xFF);
            switch (icmd)
            {
                case (int)MSPConst.MSP_IDENT:
                    version = read8();
                    multiType = read8();
                    mspVersion = read8();
                    capability = read32();
                    Debug.WriteLine($"Version: {version} Type: {multiType} MspVersion: {mspVersion} Capability: {capability}");
                    break;

                case (int)MSPConst.MSP_ATTITUDE:
                    angx = read16() / 10f;
                    angy = read16() / 10f;
                    head = read16();
                    headfree = read16();
                    //Debug.WriteLine($"angx: {angx} angy: {angy} head: {head} headfree: {headfree}");
                    break;

                case (int)MSPConst.MSP_ALTITUDE:
                    alt = read32();
                    //Debug.WriteLine($"altitude: {alt}");
                    break;

                case (int)MSPConst.MSP_RC:
                    roll = read16();
                    pitch = read16();
                    yaw = read16();
                    throttle = read16();
                    aux1 = read16();
                    aux2 = read16();
                    aux3 = read16();
                    aux4 = read16();
                    //Debug.WriteLine($"MSP_RC: [{roll},{pitch},{yaw},{throttle},{aux1},{aux2},{aux3},{aux4}]");
                    break;

                case (int)MSPConst.MSP_SET_RAW_RC:
                    if (!App.isRPi)
                        CheckSystem();
                    /*else
                        GetRC();*/
					//Debug.WriteLine("MSP_RC: Received!");
					break;

                case (int)MSPConst.MSP_RAW_GPS:
                    gpsFix = read8();
                    gpsNumSat = read8();
                    gpsLatitude = read32() / 10000000d;
                    gpsLongitude = read32() / 10000000d;
                    gpsAltitude = read16();
                    gpsSpeed = read16();
                    gpsGroundCourse = read16();
                    //Debug.WriteLine($"GPS Signal: {gpsFix} GPS num sat: {gpsNumSat} Latitude: {gpsLatitude} Longitude: {gpsLongitude} Altitude: {gpsAltitude} Speed: {gpsSpeed} GroundCourse: {gpsGroundCourse}");
                    break;

                case (int)MSPConst.MSP_BATTERY_STATE:
                    batteryLoad = read8();
                    batteryVoltage = read16() / 10d;
                    batteryCurrent = read16() / 1000d;
                    var voltage = batteryVoltage - (MIN_CELL_BATTERY_VOLTAGE * BATTERY_CELLS);
                    var delta = (MAX_CELL_BATTERY_VOLTAGE * BATTERY_CELLS) - (MIN_CELL_BATTERY_VOLTAGE * BATTERY_CELLS);
                    batteryPercentage = (int)((voltage / delta) * 100);
                    //Debug.WriteLine($"Battery Load: {batteryLoad} Battery Voltage: {batteryVoltage} Battery Current: {batteryCurrent}");
                    break;

                case (int)MSPConst.MSP_ANALOG:
                    var asd3 = read8();
                    var asd4 = read16();
                    var asd5 = read16();
                    var asd6 = read16();
                    //Debug.WriteLine($"Analog: {asd3} {asd4} {asd5} {asd6}");
                    break;

                case (int)MSPConst.MSP_SIGNAL_STRENGTH:
                    signalStrength = read8();
                    //Debug.WriteLine($"4G/LTE Signal: {signalStrength}");
                    break;

                case (int)MSPConst.MSP_ULTRASONIC_SENSOR:
                    usDistance = read8();
                    //Debug.WriteLine($"Distance: {usDistance} cm");
                    break;

                default:
                    Debug.WriteLine("Command not implemented: " + icmd);
                    break;
            }
        }

        public static void evaluateCustomCommand(List<byte> bytes)
        {
            string cmd = Encoding.ASCII.GetString(bytes.ToArray());
            switch (cmd)
            {
                case "#take_photo":
                    Camera.TakePhoto();
                    break;

                case "#load_photo":
                    Camera.LoadPhoto();
                    break;

                case "#autopilotON":
                    Autopilot.TakeControl();
                    break;

                case "#autopilotOFF":
                    Autopilot.ReleaseControl();
                    break;

                case "#dismiss":
                    Autopilot.Dismiss();
                    break;

                case "#land":
                    Autopilot.Land();
                    break;

                default:
                    Debug.WriteLine("Command not implemented: " + cmd);
                    break;
            }
        }

        // ROLL/PITCH/YAW/THROTTLE/AUX1/AUX2/AUX3/AUX4
        // Range [1000;2000]
        // This request is used to inject RC channel via MSP. Each chan overrides legacy RX as long as it is refreshed at least every second. See UART radio projects for more details.
        // Command Code = 200
        public static void MSP_SET_RAW_RC(UInt16 ch1, UInt16 ch2, UInt16 ch3, UInt16 ch4, UInt16 ch5, UInt16 ch6, UInt16 ch7, UInt16 ch8)
        {
			if (!deviceIsConnected)
				return;

            // Send
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(ch1));
            data.AddRange(BitConverter.GetBytes(ch2));
            data.AddRange(BitConverter.GetBytes(ch3));
            data.AddRange(BitConverter.GetBytes(ch4));
            data.AddRange(BitConverter.GetBytes(ch5));
            data.AddRange(BitConverter.GetBytes(ch6));
            data.AddRange(BitConverter.GetBytes(ch7));
            data.AddRange(BitConverter.GetBytes(ch8));

            sendRequestMSP(requestMSP((int)MSPConst.MSP_SET_RAW_RC, data.ToArray()));
        }

        public static byte[] Calculate_MSP_SET_RAW_RC(UInt16 ch1, UInt16 ch2, UInt16 ch3, UInt16 ch4, UInt16 ch5, UInt16 ch6, UInt16 ch7, UInt16 ch8)
        {
            // Send
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(ch1));
            data.AddRange(BitConverter.GetBytes(ch2));
            data.AddRange(BitConverter.GetBytes(ch3));
            data.AddRange(BitConverter.GetBytes(ch4));
            data.AddRange(BitConverter.GetBytes(ch5));
            data.AddRange(BitConverter.GetBytes(ch6));
            data.AddRange(BitConverter.GetBytes(ch7));
            data.AddRange(BitConverter.GetBytes(ch8));

            return requestMSP((int)MSPConst.MSP_SET_RAW_RC, data.ToArray()).ToArray();
        }

        public static byte[] MSP_SIGNAL_STRENGTH(int signal)
        {
            // Send
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(signal));

            return requestMSP("$M>", (int)MSPConst.MSP_SIGNAL_STRENGTH, data.ToArray()).ToArray();
        }

        public static byte[] MSP_ULTRASONIC_SENSOR(int distance)
        {
            // Send
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(distance));

            return requestMSP("$M>", (int)MSPConst.MSP_ULTRASONIC_SENSOR, data.ToArray()).ToArray();
        }

        public static void GetRC()
        {
			if (!deviceIsConnected)
				return;

			sendRequestMSP(requestMSP((int)MSPConst.MSP_RC));
        }

        public static void GetGPSData()
        {
			if (!deviceIsConnected)
				return;

			sendRequestMSP(requestMSP((int)MSPConst.MSP_RAW_GPS));
        }

        public static void GetBoardVersion()
        {
			if (!deviceIsConnected)
				return;

			sendRequestMSP(requestMSP((int)MSPConst.MSP_IDENT));
        }

        public static void GetAltitude()
        {
			if (!deviceIsConnected)
				return;

			sendRequestMSP(requestMSP((int)MSPConst.MSP_ALTITUDE));
        }

        public static void GetAttitude()
        {
			if (!deviceIsConnected)
				return;

			sendRequestMSP(requestMSP((int)MSPConst.MSP_ATTITUDE));
        }

        public static void GetBatteryState()
        {
            if (!deviceIsConnected)
                return;

            sendRequestMSP(requestMSP((int)MSPConst.MSP_BATTERY_STATE));
        }

        public static async void CheckSystem()
        {
            long msCurTime = MainPage.stopwatch.ElapsedMilliseconds;
            var cmdDelay = msCurTime - MainPage.lastCmdSent;

            if (cmdDelay > 0 && cmdDelay <= 1000)
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                    var latencyLabel = currentPage.FindName("latencyLabel") as TextBlock;
                    latencyLabel.Text = $"{cmdDelay}";
                });
            }
        }
    }
}