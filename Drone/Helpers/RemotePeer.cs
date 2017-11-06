using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Storage.Streams;

namespace Drone.Helpers
{
    public class RemotePeer
    {
        IOutputStream outputStream;
        HostName hostName;
        String port;

        public RemotePeer(IOutputStream outputStream, HostName hostName, String port)
        {
            this.outputStream = outputStream;
            this.hostName = hostName;
            this.port = port;
        }

        public bool IsMatching(HostName hostName, String port)
        {
            return (this.hostName == hostName && this.port == port);
        }

        public IOutputStream OutputStream
        {
            get { return outputStream; }
        }

        public override String ToString()
        {
            return hostName + port;
        }
    }
}
