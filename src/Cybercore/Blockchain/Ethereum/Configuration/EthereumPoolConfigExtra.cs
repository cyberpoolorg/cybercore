using Cybercore.Configuration;

namespace Cybercore.Blockchain.Ethereum.Configuration
{
    public class EthereumPoolConfigExtra
    {
        public string DagDir { get; set; }
        public bool? EnableDaemonWebsocketStreaming { get; set; }
        public string ChainTypeOverride { get; set; }
        public ZmqPubSubEndpointConfig BtStream { get; set; }
    }
}