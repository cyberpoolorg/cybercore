using Autofac;
using AutoMapper;
using Cybercore.Blockchain;
using Cybercore.Configuration;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Model.Projections;
using MinerStats = Cybercore.Persistence.Model.Projections.MinerStats;

namespace Cybercore
{
    public class AutoMapperProfile : Profile
    {
        public const string AutofacContextItemName = "ctx";

        public AutoMapperProfile()
        {
            CreateMap<Blockchain.Share, Persistence.Model.Share>();

            CreateMap<Blockchain.Share, Block>()
                .ForMember(dest => dest.Reward, opt => opt.MapFrom(src => src.BlockReward))
                .ForMember(dest => dest.Hash, opt => opt.MapFrom(src => src.BlockHash))
                .ForMember(dest => dest.Status, opt => opt.Ignore());

            CreateMap<BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

            CreateMap<Mining.PoolStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            CreateMap<BlockchainStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            CreateMap<CoinTemplate, Api.Responses.ApiCoinConfig>()
                .ForMember(dest => dest.Type, opt => opt.MapFrom(src => src.Symbol))
                .ForMember(dest => dest.Family, opt => opt.MapFrom(src => src.Family.ToString().ToLower()))
                .ForMember(dest => dest.Symbol, opt => opt.MapFrom(src => src.Symbol))
                .ForMember(dest => dest.Algorithm, opt => opt.MapFrom(src => src.GetAlgorithmName()));

            CreateMap<PoolConfig, Api.Responses.PoolInfo>()
                .ForMember(dest => dest.Coin, opt => opt.MapFrom(src => src.Template));

            CreateMap<PoolStats, Api.Responses.PoolInfo>();
            CreateMap<PoolStats, Api.Responses.AggregatedPoolStats>();
            CreateMap<Block, Api.Responses.Block>();
            CreateMap<MinerSettings, Api.Responses.MinerSettings>();
            CreateMap<Payment, Api.Responses.Payment>();
            CreateMap<BalanceChange, Api.Responses.BalanceChange>();
            CreateMap<PoolPaymentProcessingConfig, Api.Responses.ApiPoolPaymentProcessingConfig>();

            CreateMap<MinerStats, Api.Responses.MinerStats>()
                .ForMember(dest => dest.LastPayment, opt => opt.Ignore())
                .ForMember(dest => dest.LastPaymentLink, opt => opt.Ignore());

            CreateMap<WorkerPerformanceStats, Api.Responses.WorkerPerformanceStats>();
            CreateMap<WorkerPerformanceStatsContainer, Api.Responses.WorkerPerformanceStatsContainer>();
            CreateMap<MinerWorkerPerformanceStats, Api.Responses.MinerPerformanceStats>();
            CreateMap<Persistence.Model.Share, Persistence.Postgres.Entities.Share>();
            CreateMap<Block, Persistence.Postgres.Entities.Block>();
            CreateMap<Balance, Persistence.Postgres.Entities.Balance>();
            CreateMap<Payment, Persistence.Postgres.Entities.Payment>();
            CreateMap<MinerSettings, Persistence.Postgres.Entities.MinerSettings>();
            CreateMap<PoolStats, Persistence.Postgres.Entities.PoolStats>();

            CreateMap<MinerWorkerPerformanceStats, Persistence.Postgres.Entities.MinerWorkerPerformanceStats>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());

            CreateMap<Api.Responses.MinerSettings, MinerSettings>();

            CreateMap<Persistence.Postgres.Entities.Share, Persistence.Model.Share>();
            CreateMap<Persistence.Postgres.Entities.Block, Block>();
            CreateMap<Persistence.Postgres.Entities.Balance, Balance>();
            CreateMap<Persistence.Postgres.Entities.Payment, Payment>();
            CreateMap<Persistence.Postgres.Entities.BalanceChange, BalanceChange>();
            CreateMap<Persistence.Postgres.Entities.PoolStats, PoolStats>();
            CreateMap<Persistence.Postgres.Entities.MinerSettings, MinerSettings>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, MinerWorkerPerformanceStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, Api.Responses.MinerPerformanceStats>();

            CreateMap<PoolStats, Mining.PoolStats>();
            CreateMap<BlockchainStats, Mining.PoolStats>();

            CreateMap<PoolStats, BlockchainStats>()
                .ForMember(dest => dest.RewardType, opt => opt.Ignore())
                .ForMember(dest => dest.NetworkType, opt => opt.Ignore());
        }
    }
}