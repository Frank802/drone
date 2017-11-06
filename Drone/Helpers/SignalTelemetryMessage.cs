using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone.Helpers
{
	public class SignalTelemetryMessage : ITelemetryMessage
	{
		public int Signal { get; set; }
        public DateTime Timestamp { get; }
        public TelemetryMessageType TelemetryMessageType { get; }


		public SignalTelemetryMessage(int signal)
		{
			this.Signal = signal;
            this.Timestamp = DateTime.UtcNow;
            this.TelemetryMessageType = TelemetryMessageType.Signal;
		}

		public string GetJSON()
		{
			var json = JsonConvert.SerializeObject(this);
			return json;
		}
	}
}
