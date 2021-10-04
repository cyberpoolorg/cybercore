using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonResponses
{
    public class GetBalanceResponse
    {
        public decimal Balance { get; set; }

        [JsonProperty("unlocked_balance")]
        public decimal UnlockedBalance { get; set; }
    }
}