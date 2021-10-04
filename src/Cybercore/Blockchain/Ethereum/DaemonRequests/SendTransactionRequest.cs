using System.Numerics;
using Cybercore.Serialization;
using Newtonsoft.Json;

namespace Cybercore.Blockchain.Ethereum.DaemonRequests
{
    public class SendTransactionRequest
    {
        public string From { get; set; }
        public string To { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? Gas { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? GasPrice { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public string Value { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong MaxPriorityFeePerGas { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong MaxFeePerGas { get; set; }
    }
}