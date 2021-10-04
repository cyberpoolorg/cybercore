namespace Cybercore.Blockchain.Ethereum.Configuration
{
    public class EthereumPoolPaymentProcessingConfigExtra
    {
        public bool KeepTransactionFees { get; set; }
        public bool KeepUncles { get; set; }
        public ulong Gas { get; set; }
        public ulong MaxFeePerGas { get; set; }
    }
}