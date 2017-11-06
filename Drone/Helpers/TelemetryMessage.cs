using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone.Helpers
{
    public class TelemetryMessage : ITelemetryMessage
    {
        public int GpsFix { get; set; }
        public int GpsNumSat { get; set; }
        public double GpsLatitude { get; set; }
        public double GpsLongitude { get; set; }
        public double GpsAltitude { get; set; }
        public double GpsSpeed { get; set; }
        public double GpsGroundCourse { get; set; }
        public float Angx { get; set; }
        public float Angy { get; set; }
        public float Head { get; set; }
        public float Headfree { get; set; }
        public int Signal { get; set; }
        public double Distance { get; set; }
        public double BatteryVoltage { get; set; }
        public int BatteryPercentage { get; set; }
        public DateTime Timestamp { get; }
        public TelemetryMessageType TelemetryMessageType { get; }

        public TelemetryMessage(int gpsFix, int gpsNumSat, double gpsLatitude, double gpsLongitude, double gpsAltitude, double gpsSpeed, double gpsGroundCourse, float angx, float angy, float head, float headfree, int signal, double distance, double batteryVoltage, int batteryPercentage)
        {
            this.GpsFix = gpsFix;
            this.GpsNumSat = gpsNumSat;
            this.GpsLatitude = gpsLatitude;
            this.GpsLongitude = gpsLongitude;
            this.GpsAltitude = gpsAltitude;
            this.GpsSpeed = gpsSpeed;
            this.GpsGroundCourse = gpsGroundCourse;
            this.Angx = angx;
            this.Angy = angy;
            this.Head = head;
            this.Headfree = headfree;
            this.Signal = signal;
            this.Distance = distance;
            this.BatteryVoltage = batteryVoltage;
            this.BatteryPercentage = batteryPercentage;
            this.Timestamp = DateTime.UtcNow;
            this.TelemetryMessageType = TelemetryMessageType.Generic;
        }

        public string GetJSON()
        {
            var json = JsonConvert.SerializeObject(this);
            return json;
        }
    }
}
