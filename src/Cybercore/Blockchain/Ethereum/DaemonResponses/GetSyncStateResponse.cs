using Cybercore.Serialization;
using Newtonsoft.Json;

namespace Cybercore.Blockchain.Ethereum.DaemonResponses
{
    public class SyncState
    {
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong StartingBlock { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong CurrentBlock { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong HighestBlock { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong WarpChunksAmount { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong WarpChunksProcessed { get; set; }
    }
}