using System.Numerics;
using Cybercore.Serialization;
using Newtonsoft.Json;

namespace Cybercore.Blockchain.Ethereum.DaemonResponses
{
    public class TransactionReceipt
    {
        public string TransactionHash { get; set; }

        [JsonProperty("transactionIndex")]
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong Index { get; set; }

        public string BlockHash { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong BlockNumber { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger CummulativeGasUsed { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger GasUsed { get; set; }

        public string ContractAddress { get; set; }
    }
}