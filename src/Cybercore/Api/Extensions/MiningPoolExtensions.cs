using AutoMapper;
using System.Linq;
using Cybercore.Api.Responses;
using Cybercore.Blockchain;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.Mining;

namespace Cybercore.Api.Extensions
{
    public static class MiningPoolExtensions
    {
        public static PoolInfo ToPoolInfo(this PoolConfig poolConfig, IMapper mapper, Persistence.Model.PoolStats stats, IMiningPool pool)
        {
            var poolInfo = mapper.Map<PoolInfo>(poolConfig);

            poolInfo.PoolStats = mapper.Map<PoolStats>(stats);
            poolInfo.NetworkStats = pool?.NetworkStats ?? mapper.Map<BlockchainStats>(stats);

            var addressInfobaseUrl = poolConfig.Template.ExplorerAccountLink;
            if (!string.IsNullOrEmpty(addressInfobaseUrl))
                poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

            poolInfo.PoolFeePercent = poolConfig.RewardRecipients != null ? (float)poolConfig.RewardRecipients.Sum(x => x.Percentage) : 0;

            return poolInfo;
        }
    }
}