using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cybercore.Blockchain.Equihash.Configuration
{
    public class EquihashPoolConfigExtra
    {
        [JsonProperty("z-address")]
        public string ZAddress { get; set; }

        public JToken GBTArgs { get; set; }
    }
}