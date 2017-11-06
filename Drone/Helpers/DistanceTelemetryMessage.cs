using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone.Helpers
{
    class DistanceTelemetryMessage : ITelemetryMessage
    {
        public double Distance { get; set; }
        public DateTime Timestamp { get; }
        public TelemetryMessageType TelemetryMessageType { get; }

        public DistanceTelemetryMessage(double distance)
        {
            this.Distance = distance;
            this.Timestamp = DateTime.UtcNow;
            this.TelemetryMessageType = TelemetryMessageType.Distance;
        }

        public string GetJSON()
        {
            var json = JsonConvert.SerializeObject(this);
            return json;
        }
    }
}
