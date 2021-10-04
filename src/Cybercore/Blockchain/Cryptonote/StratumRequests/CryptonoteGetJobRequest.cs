using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.StratumRequests
{
    public class CryptonoteGetJobRequest
    {
        [JsonProperty("id")]
        public string WorkerId { get; set; }
    }
}