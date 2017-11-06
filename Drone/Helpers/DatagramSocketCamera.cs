using Drone.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace Drone.Helpers
{
    public static class DatagramSocketCamera
    {
        private static MediaCapture mediaCapture;
        private static MediaFrameReader mediaFrameReader;
        private static DatagramSocket socket;
        private static DatagramSocket listener;
        private static RemotePeer peer;
        private static bool getPhoto = false;
        private const string hostName = "192.168.0.164";
        private const string hostPort = "8047";

        private static int _threadsCount = 0;
        private static int _stoppedThreads = 0;
        private static bool _stopThreads = false;

        public static bool socketIsConnected = false;
        public static bool listenerHasStarted = false;

        // Video settings
        //private const string VIDEO_RES = "1280x720";
        private const string VIDEO_RES = "640x480";
        //private const string VIDEO_RES = "320x240";
        private const string VIDEO_SUBTYP = "YUY2";
        //private const string VIDEO_SUBTYP = "NV12";
        //private const string VIDEO_SUBTYP = "MJPG";
        private const double IMAGE_QUALITY_PERCENT = 0.8d;
        private static BitmapPropertySet imageQuality;

        public static async Task Init()
        {
            ClearPrevious();

            try
            {
                if (App.isRPi)
                {
                    await InitCamera(1, IMAGE_QUALITY_PERCENT, VIDEO_SUBTYP, VIDEO_RES);

                    socket = new DatagramSocket();
                    socket.MessageReceived += MessageReceived;
                    HostName hn = new HostName(hostName);
                    await socket.ConnectAsync(hn, hostPort).AsTask();
                    Debug.WriteLine("Connected to remote camera " + hn + ":" + hostPort);
                    socketIsConnected = true;
                }
                else
                {
                    listener = new DatagramSocket();
                    listener.MessageReceived += MessageReceived;
                    await listener.BindServiceNameAsync(hostPort).AsTask();
                    Debug.WriteLine("Remote camera listening on port " + hostPort);
                    listenerHasStarted = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error initializing camera: " + ex.Message);
            }
        }

        private static async void MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
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

                if (!App.isRPi)
                {
                    //GET FRAME
                    var buffer = readPacket.ReadBuffer(buffLen);
                    var stream = buffer.AsStream();
                    stream.Seek(0, SeekOrigin.Begin);
                    IRandomAccessStream photoStream = stream.AsRandomAccessStream();

                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                    {
                        BitmapImage frame = new BitmapImage();
                        await frame.SetSourceAsync(photoStream);
                        var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                        var captureImage = currentPage.FindName("captureFrame") as Image;
                        captureImage.Source = frame;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MessageReceived() - " + ex.Message);
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

        public static void TakePhoto()
        {
            if (!App.isRPi)
                return;

            getPhoto = true;
        }

        public static void LoadPhoto()
        {
            if (App.isRPi)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var stream = await BlobStorageService.DownloadData("dronepictures");
                    if (stream == null)
                        return;

                    stream.Seek(0, SeekOrigin.Begin);
                    IRandomAccessStream photoStream = stream.AsRandomAccessStream();


                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                    {
                        BitmapImage bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(photoStream);
                        var currentPage = ((ContentControl)Window.Current.Content).Content as Page;
                        var captureImage = currentPage.FindName("captureImage") as Image;
                        captureImage.Source = bitmap;
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during taking photo: {ex.Message}");
                }
            });
        }

        public static async Task InitCamera(int usedThreads, double videoQuality, string videoSubtype, string videoResolution)
        {
            if (!videoResolution.Contains('x'))
                throw new ArgumentException("Resolution is not in valid format.");

            await Task.Run(async () =>
            {
                try
                {
                    _threadsCount = usedThreads;
                    _stoppedThreads = usedThreads;

                    imageQuality = new BitmapPropertySet();
                    var imageQualityValue = new BitmapTypedValue(videoQuality, Windows.Foundation.PropertyType.Single);
                    imageQuality.Add("ImageQuality", imageQualityValue);

                    mediaCapture = new MediaCapture();

                    var frameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();

                    var settings = new MediaCaptureInitializationSettings()
                    {
                        SharingMode = MediaCaptureSharingMode.ExclusiveControl,

                        //With CPU the results contain always SoftwareBitmaps, otherwise with GPU
                        //they preferring D3DSurface
                        MemoryPreference = MediaCaptureMemoryPreference.Cpu,

                        //Capture only video, no audio
                        StreamingCaptureMode = StreamingCaptureMode.Video
                    };

                    await mediaCapture.InitializeAsync(settings);

                    var mediaFrameSource = mediaCapture.FrameSources.First().Value;
                    var videoDeviceController = mediaFrameSource.Controller.VideoDeviceController;

                    videoDeviceController.DesiredOptimization = Windows.Media.Devices.MediaCaptureOptimization.Quality;
                    videoDeviceController.PrimaryUse = Windows.Media.Devices.CaptureUse.Video;

                    //Set exposure (auto light adjustment)
                    if (mediaCapture.VideoDeviceController.Exposure.Capabilities.Supported
                        && mediaCapture.VideoDeviceController.Exposure.Capabilities.AutoModeSupported)
                    {
                        mediaCapture.VideoDeviceController.Exposure.TrySetAuto(true);
                    }

                    var videoResolutionWidth = uint.Parse(videoResolution.Split('x').FirstOrDefault());
                    var videoResolutionHeight = uint.Parse(videoResolution.Split('x').LastOrDefault());
                    var videoSubType = videoSubtype;

                    //Set resolution, frame rate and video subtyp
                    var videoFormat = mediaFrameSource.SupportedFormats.Where(sf => sf.VideoFormat.Width == videoResolutionWidth
                                                                                    && sf.VideoFormat.Height == videoResolutionHeight
                                                                                    && sf.Subtype == videoSubType)
                                                                        .OrderByDescending(m => m.FrameRate.Numerator / m.FrameRate.Denominator)
                                                                        .First();

                    await mediaFrameSource.SetFormatAsync(videoFormat);

                    mediaFrameReader = await mediaCapture.CreateFrameReaderAsync(mediaFrameSource);
                    await mediaFrameReader.StartAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during camera initialization: {ex.Message}");
                }
            });
        }

        private static void ProcessFrames()
        {
            if (socket == null)
                return;

            _stoppedThreads--;

            while (_stopThreads == false)
            {
                try
                {
                    //GarbageCollectorCanWorkHere();

                    var frame = mediaFrameReader.TryAcquireLatestFrame();

                    var frameDuration = new Stopwatch();
                    frameDuration.Start();

                    if (frame == null
                        || frame.VideoMediaFrame == null
                        || frame.VideoMediaFrame.SoftwareBitmap == null)
                        continue;

                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        using (var bitmap = SoftwareBitmap.Convert(frame.VideoMediaFrame.SoftwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore))
                        {
                            var imageTask = BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, imageQuality).AsTask();
                            imageTask.Wait();
                            var encoder = imageTask.Result;
                            encoder.SetSoftwareBitmap(bitmap);

                            var flushTask = encoder.FlushAsync().AsTask();
                            flushTask.Wait();

                            using (var asStream = stream.AsStream())
                            {
                                asStream.Position = 0;

                                var bytes = new byte[asStream.Length];
                                asStream.Read(bytes, 0, bytes.Length);

                                if (getPhoto)
                                {
                                    getPhoto = false;
                                    Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await BlobStorageService.UploadData("dronepictures", bytes);
                                            Debug.WriteLine($"Photo uploaded successfully.");

                                            var cmd = Encoding.ASCII.GetBytes("#load_photo");
                                            Socket.SendData(cmd);
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Error during photo upload: {ex.Message}");
                                        }
                                    });
                                }

                                socket.OutputStream.WriteAsync(bytes.AsBuffer());
                                encoder = null;
                            }
                        }
                    }
                }
                catch (ObjectDisposedException) { }
            }

            _stoppedThreads++;
        }

        public static void StartCapture()
        {
            for (int workerNumber = 0; workerNumber < _threadsCount; workerNumber++)
            {
                Task.Factory.StartNew(() =>
                {
                    ProcessFrames();

                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .AsAsyncAction()
                .AsTask();
            }
        }

        public static async Task StopCapture()
        {
            _stopThreads = true;

            SpinWait.SpinUntil(() => { return _threadsCount == _stoppedThreads; });

            await mediaFrameReader.StopAsync();

            _stopThreads = false;
        }
    }
}
