using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone.Helpers
{
    public class GpsTelemetryMessage : ITelemetryMessage
    {
        public int GpsFix { get; set; }
        public int GpsNumSat { get; set; }
        public double GpsLatitude { get; set; }
        public double GpsLongitude { get; set; }
        public double GpsAltitude { get; set; }
        public double GpsSpeed { get; set; }
        public double GpsGroundCourse { get; set; }
        public DateTime Timestamp { get; }
        public TelemetryMessageType TelemetryMessageType { get; set; }


		public GpsTelemetryMessage(int gpsFix, int gpsNumSat, double gpsLatitude, double gpsLongitude, double gpsAltitude, double gpsSpeed, double gpsGroundCourse)
        {
            this.GpsFix = gpsFix;
            this.GpsNumSat = gpsNumSat;
            this.GpsLatitude = gpsLatitude;
            this.GpsLongitude = gpsLongitude;
            this.GpsAltitude = gpsAltitude;
            this.GpsSpeed = gpsSpeed;
            this.GpsGroundCourse = gpsGroundCourse;
            this.Timestamp = DateTime.UtcNow;
            this.TelemetryMessageType = TelemetryMessageType.Gps;
        }

		public string GetJSON()
		{
			var json = JsonConvert.SerializeObject(this);
			return json;
		}
	}
}
