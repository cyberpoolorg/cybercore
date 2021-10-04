using Cybercore.Api.Responses;

namespace Cybercore.Api.Requests
{
    public class UpdateMinerSettingsRequest
    {
        public string IpAddress { get; set; }
        public MinerSettings Settings { get; set; }
    }
}