using Cybercore.Configuration;
using Newtonsoft.Json.Linq;

namespace Cybercore.Blockchain.Bitcoin.Configuration
{
    public class BitcoinPoolConfigExtra
    {
        public BitcoinAddressType AddressType { get; set; } = BitcoinAddressType.Legacy;
        public string BechPrefix { get; set; } = "bc";
        public int? MaxActiveJobs { get; set; }
        public bool? HasLegacyDaemon { get; set; }
        public string CoinbaseTxComment { get; set; }
        public ZmqPubSubEndpointConfig BtStream { get; set; }
        public JToken GBTArgs { get; set; }
    }
}