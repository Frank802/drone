using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone.Helpers
{
	public class AttitudeTelemetryMessage : ITelemetryMessage
	{
		public float Angx { get; set; }
		public float Angy { get; set; }
		public float Head { get; set; }
		public float Headfree { get; set; }
        public DateTime Timestamp { get; }
		public TelemetryMessageType TelemetryMessageType { get; }


		public AttitudeTelemetryMessage(float angx, float angy, float head, float headfree)
		{
			this.Angx = angx;
			this.Angy = angy;
			this.Head = head;
			this.Headfree = headfree;
            this.Timestamp = DateTime.UtcNow;
			this.TelemetryMessageType = TelemetryMessageType.Attitude;
		}

		public string GetJSON()
		{
			var json = JsonConvert.SerializeObject(this);
			return json;
		}
	}
}
