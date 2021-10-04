using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonResponses
{
    public class GetInfoResponse
    {
        public uint Target { get; set; }

        [JsonProperty("target_height")]
        public uint TargetHeight { get; set; }

        [JsonProperty("testnet")]
        public bool IsTestnet { get; set; }

        [JsonProperty("nettype")]
        public string NetType { get; set; }

        [JsonProperty("top_block_hash")]
        public string TopBlockHash { get; set; }

        [JsonProperty("tx_count")]
        public uint TransactionCount { get; set; }

        [JsonProperty("tx_pool_size")]
        public uint TransactionPoolSize { get; set; }

        public ulong Difficulty { get; set; }
        public uint Height { get; set; }
        public string Status { get; set; }

        [JsonProperty("alt_blocks_count")]
        public int AltBlocksCount { get; set; }

        [JsonProperty("grey_peerlist_size")]
        public int GreyPeerlistSize { get; set; }

        [JsonProperty("white_peerlist_size")]
        public uint WhitePeerlistSize { get; set; }

        [JsonProperty("incoming_connections_count")]
        public int IncomingConnectionsCount { get; set; }

        [JsonProperty("outgoing_connections_count")]
        public int OutgoingConnectionsCount { get; set; }
    }
}