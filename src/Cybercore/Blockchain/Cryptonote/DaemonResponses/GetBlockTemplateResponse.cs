using Newtonsoft.Json;

namespace Cybercore.Blockchain.Cryptonote.DaemonResponses
{
    public class GetBlockTemplateResponse
    {
        [JsonProperty("blocktemplate_blob")]
        public string Blob { get; set; }

        public long Difficulty { get; set; }
        public uint Height { get; set; }

        [JsonProperty("expected_reward")]
        public long ExpectedReward { get; set; }

        [JsonProperty("prev_hash")]
        public string PreviousBlockhash { get; set; }

        [JsonProperty("seed_hash")]
        public string SeedHash { get; set; }

        [JsonProperty("reserved_offset")]
        public int ReservedOffset { get; set; }

        public string Status { get; set; }
    }
}