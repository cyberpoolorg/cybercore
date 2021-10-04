using System.Numerics;
using Cybercore.Serialization;
using Newtonsoft.Json;

namespace Cybercore.Blockchain.Ethereum.DaemonResponses
{
    public class Transaction
    {
        public string Hash { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong Nonce { get; set; }

        public string BlockHash { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong? BlockNumber { get; set; }

        [JsonProperty("transactionIndex")]
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong? Index { get; set; }

        public string From { get; set; }
        public string To { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger Value { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger GasPrice { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger Gas { get; set; }

        public string Input { get; set; }
    }

    public class Block
    {
        [JsonProperty("number")]
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong? Height { get; set; }

        public string Hash { get; set; }
        public string ParentHash { get; set; }
        public string Nonce { get; set; }
        public string[] SealFields { get; set; }
        public string Sha3Uncles { get; set; }
        public string LogsBloom { get; set; }
        public string TransactionsRoot { get; set; }
        public string StateRoot { get; set; }
        public string ReceiptsRoot { get; set; }
        public string Miner { get; set; }
        public string Difficulty { get; set; }
        public string TotalDifficulty { get; set; }
        public string ExtraData { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong Size { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong BaseFeePerGas { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong GasLimit { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong GasUsed { get; set; }

        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong Timestamp { get; set; }

        public Transaction[] Transactions { get; set; }
        public string[] Uncles { get; set; }
    }
}