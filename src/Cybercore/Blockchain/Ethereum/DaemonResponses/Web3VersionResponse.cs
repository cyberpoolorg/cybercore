using System.Numerics;
using Cybercore.Serialization;
using Newtonsoft.Json;

namespace Cybercore.Blockchain.Ethereum.DaemonResponses
{
    public class Web3Version
    {
        public string Api { get; set; }
        public uint Ethereum { get; set; }
        public uint Network { get; set; }
        public string Node { get; set; }
    }
}