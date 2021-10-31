namespace Cybercore.Blockchain.Bitcoin.DaemonResponses
{
    public class MiningInfo
    {
        public int Blocks { get; set; }
        public int CurrentBlockSize { get; set; }
        public int CurrentBlockWeight { get; set; }
        public double NetworkHashps { get; set; }
        public double NetmHashps { get; set; }
        public string Chain { get; set; }
    }
}