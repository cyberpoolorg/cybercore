namespace Cybercore.Blockchain.Bitcoin
{
    public class BitcoinStratumMethods
    {
        public const string Subscribe = "mining.subscribe";
        public const string Authorize = "mining.authorize";
        public const string SuggestDifficulty = "mining.suggest_difficulty";
        public const string MiningNotify = "mining.notify";
        public const string SubmitShare = "mining.submit";
        public const string SetDifficulty = "mining.set_difficulty";
        public const string GetTransactions = "mining.get_transactions";
        public const string ExtraNonceSubscribe = "mining.extranonce.subscribe";
        public const string MiningMultiVersion = "mining.multi_version";
        public const string MiningConfigure = "mining.configure";
    }
}