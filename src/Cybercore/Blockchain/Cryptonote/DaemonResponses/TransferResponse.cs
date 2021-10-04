using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonResponses
{
    public class TransferResponse
    {
        public ulong Fee { get; set; }

        [JsonProperty("tx_key")]
        public string TxKey { get; set; }

        [JsonProperty("tx_hash")]
        public string TxHash { get; set; }

        [JsonProperty("tx_blob")]
        public string TxBlob { get; set; }

        [JsonProperty("do_not_relay")]
        public string DoNotRelay { get; set; }

        public string Status { get; set; }
    }
}