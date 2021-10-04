using Cybercore.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cybercore.Blockchain.Equihash.DaemonResponses
{
    public class ZCashShieldingResponse
    {
        [JsonProperty("opid")]
        public string OperationId { get; set; }
    }
}