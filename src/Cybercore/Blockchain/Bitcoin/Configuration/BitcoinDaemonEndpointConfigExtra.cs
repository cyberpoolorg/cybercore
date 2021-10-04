namespace Cybercore.Blockchain.Bitcoin.Configuration
{
    public class BitcoinDaemonEndpointConfigExtra
    {
        public int? MinimumConfirmations { get; set; }
        public string ZmqBlockNotifySocket { get; set; }
        public string ZmqBlockNotifyTopic { get; set; }
    }
}