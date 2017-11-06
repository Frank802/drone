using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drone.Helpers
{
	public enum TelemetryMessageType
	{
		Gps = 1,
		Signal = 2,
		Attitude = 3,
        Distance = 4,
        Generic = 5
	}

	public interface ITelemetryMessage
	{
        DateTime Timestamp { get; }

		TelemetryMessageType TelemetryMessageType { get; }

		string GetJSON();
	}
}
