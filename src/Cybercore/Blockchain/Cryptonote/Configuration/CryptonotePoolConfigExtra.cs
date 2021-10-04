using Cybercore.Configuration;
using Newtonsoft.Json.Linq;

namespace Cybercore.Blockchain.Cryptonote.Configuration
{
    public class CryptonotePoolConfigExtra
    {
        public ZmqPubSubEndpointConfig BtStream { get; set; }
        public string RandomXRealm { get; set; }
        public JToken RandomXFlagsOverride { get; set; }
        public JToken RandomXFlagsAdd { get; set; }

        // ReSharper disable once InconsistentNaming
        public int RandomXVMCount { get; set; } = 1;
    }
}