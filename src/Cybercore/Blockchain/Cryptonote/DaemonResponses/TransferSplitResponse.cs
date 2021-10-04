using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonResponses
{
    public class TransferSplitResponse
    {
        [JsonProperty("fee_list")]
        public ulong[] FeeList { get; set; }

        [JsonProperty("tx_hash_list")]
        public string[] TxHashList { get; set; }

        public string Status { get; set; }
    }
}