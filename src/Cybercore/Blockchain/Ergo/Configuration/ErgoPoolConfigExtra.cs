using Cybercore.Configuration;

namespace Cybercore.Blockchain.Ergo.Configuration
{
    public class ErgoPoolConfigExtra
    {
        public int? MaxActiveJobs { get; set; }
        public ZmqPubSubEndpointConfig BtStream { get; set; }
        public int? ExtraNonce1Size { get; set; }
    }
}
