using Drone.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Gpio;

namespace Drone
{
	public class UltrasonicSensor
	{
        private const int TIMEOUT_PULSEIN = 500;

        private readonly GpioPin triggerPin;
        private readonly GpioPin echoPin;

        private double prevTime = 0d;

        public UltrasonicSensor(int triggerPinNumber, int echoPinNumber)
        {
            var gpio = GpioController.GetDefault();

            triggerPin = gpio.OpenPin(triggerPinNumber);
            triggerPin.SetDriveMode(GpioPinDriveMode.Output);

            echoPin = gpio.OpenPin(echoPinNumber);
            echoPin.SetDriveMode(GpioPinDriveMode.Input);
        }

        public void GetDistance()
        {
            var mre = new ManualResetEventSlim(false);

            //Send a 10µs pulse to start the measurement
            triggerPin.Write(GpioPinValue.High);
            mre.Wait(TimeSpan.FromMilliseconds(0.01));
            triggerPin.Write(GpioPinValue.Low);

            var time = PulseIn(echoPin, GpioPinValue.High, TIMEOUT_PULSEIN);

            /* Calculating distance.
                If you take 340 m/sec (approximate speed of sound through air) and convert to cm/sec you get 34000 cm/sec.
                For pulse-echo, the sound travels twice the measured distance so you need to divide the conversion factor 
                by 2 so you get 17000 cm/sec. When you multiply by the measured time, you get distance from the transducer to the object in cm.
            */
            var distance = time * 17000;
            MultiWii.usDistance = distance;

            //Debug.WriteLine("Milliseconds: " + time);
            //Debug.WriteLine("Distance: " + distance);

            prevTime = time;
            //return distance;
        }

        private double PulseIn(GpioPin pin, GpioPinValue value, ushort timeout)
        {
            var sw = new Stopwatch();
            var swTimeout = new Stopwatch();

            swTimeout.Start();

            // Waits for pulse.
            while (pin.Read() != value)
            {
                if (swTimeout.ElapsedMilliseconds > timeout)
                    return prevTime;
                        //return 3.5;
            }

            sw.Start();

            // Waits for pulse end.
            while (pin.Read() == value)
            {
                if (swTimeout.ElapsedMilliseconds > timeout)
                    return prevTime;
                        //return 3.4;
            }

            sw.Stop();

            return sw.Elapsed.TotalSeconds;
        }
    }
}
