using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonRequests
{
    public class GetBlockTemplateRequest
    {
        [JsonProperty("wallet_address")]
        public string WalletAddress { get; set; }

        [JsonProperty("reserve_size")]
        public uint ReserveSize { get; set; }
    }
}