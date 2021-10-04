using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonRequests
{
    public class TransferDestination
    {
        public string Address { get; set; }
        public ulong Amount { get; set; }
    }

    public class TransferRequest
    {
        public TransferDestination[] Destinations { get; set; }
        public uint Mixin { get; set; }
        public uint Priority { get; set; }

        [JsonProperty("ring_size")]
        public uint RingSize { get; set; } = 7;

        [JsonProperty("payment_id")]
        public string PaymentId { get; set; }

        [JsonProperty("get_tx_key")]
        public bool GetTxKey { get; set; }

        [JsonProperty("get_tx_hex")]
        public bool GetTxHex { get; set; }

        [JsonProperty("unlock_time")]
        public uint UnlockTime { get; set; }
    }
}