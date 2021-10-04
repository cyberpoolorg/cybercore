using System;
using System.Collections.Generic;
using System.Text;

namespace Cybercore.Blockchain.Ethereum.Configuration
{
    public class EthereumDaemonEndpointConfigExtra
    {
        public int? PortWs { get; set; }
        public string HttpPathWs { get; set; }
        public bool SslWs { get; set; }
    }
}