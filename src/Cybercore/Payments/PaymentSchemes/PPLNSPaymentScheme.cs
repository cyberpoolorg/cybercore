using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.Mining;
using Cybercore.Persistence;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Repositories;
using Cybercore.Util;
using NLog;
using Polly;
using Contract = Cybercore.Contracts.Contract;

namespace Cybercore.Payments.PaymentSchemes
{
    // ReSharper disable once InconsistentNaming

    public class PPLNSPaymentScheme : IPayoutScheme
    {
        public PPLNSPaymentScheme(IConnectionFactory cf,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));

            this.cf = cf;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IShareRepository shareRepo;
        private static readonly ILogger logger = LogManager.GetLogger("PPLNS Payment", typeof(PPLNSPaymentScheme));
        private const int RetryCount = 4;
        private IAsyncPolicy shareReadFaultPolicy;

        private class Config
        {
            public decimal Factor { get; set; }
        }

        #region IPayoutScheme

        public async Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, IPayoutHandler payoutHandler, Block block, decimal blockReward, CancellationToken ct)
        {
            var poolConfig = pool.Config;
            var payoutConfig = poolConfig.PaymentProcessing.PayoutSchemeConfig;
            var window = payoutConfig?.ToObject<Config>()?.Factor ?? 2.0m;
            var shares = new Dictionary<string, double>();
            var rewards = new Dictionary<string, decimal>();
            var shareCutOffDate = await CalculateRewardsAsync(pool, payoutHandler, window, block, blockReward, shares, rewards, ct);

            foreach (var address in rewards.Keys)
            {
                var amount = rewards[address];

                if (amount > 0)
                {
                    logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for {FormatUtil.FormatQuantity(shares[address])} ({shares[address]}) shares for block {block.BlockHeight}");
                    await balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, address, amount, $"Reward for {FormatUtil.FormatQuantity(shares[address])} shares for block {block.BlockHeight}");
                }
            }

            if (shareCutOffDate.HasValue)
            {
                var cutOffCount = await shareRepo.CountSharesBeforeCreatedAsync(con, tx, poolConfig.Id, shareCutOffDate.Value);

                if (cutOffCount > 0)
                {
                    await LogDiscardedSharesAsync(poolConfig, block, shareCutOffDate.Value);
#if !DEBUG
                    logger.Info(() => $"Deleting {cutOffCount} discarded shares before {shareCutOffDate.Value:O}");
                    await shareRepo.DeleteSharesBeforeCreatedAsync(con, tx, poolConfig.Id, shareCutOffDate.Value);
#endif
                }
            }

            var totalShareCount = shares.Values.ToList().Sum(x => new decimal(x));
            var totalRewards = rewards.Values.ToList().Sum(x => x);

            if (totalRewards > 0)
                logger.Info(() => $"{FormatUtil.FormatQuantity((double)totalShareCount)} ({Math.Round(totalShareCount, 2)}) shares contributed to a total payout of {payoutHandler.FormatAmount(totalRewards)} ({totalRewards / blockReward * 100:0.00}% of block reward) to {rewards.Keys.Count} addresses");
        }

        private async Task LogDiscardedSharesAsync(PoolConfig poolConfig, Block block, DateTime value)
        {
            var before = value;
            var pageSize = 50000;
            var currentPage = 0;
            var shares = new Dictionary<string, double>();

            while (true)
            {
                logger.Info(() => $"Fetching page {currentPage} of discarded shares for pool {poolConfig.Id}, block {block.BlockHeight}");

                var page = await shareReadFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.ReadSharesBeforeCreatedAsync(con, poolConfig.Id, before, false, pageSize)));

                currentPage++;

                for (var i = 0; i < page.Length; i++)
                {
                    var share = page[i];
                    var address = share.Miner;

                    if (!shares.ContainsKey(address))
                        shares[address] = share.Difficulty;
                    else
                        shares[address] += share.Difficulty;
                }

                if (page.Length < pageSize)
                    break;

                before = page[^1].Created;
            }

            if (shares.Keys.Count > 0)
            {
                var addressesByShares = shares.Keys.OrderByDescending(x => shares[x]);

                logger.Info(() => $"{FormatUtil.FormatQuantity(shares.Values.Sum())} ({shares.Values.Sum()}) total discarded shares, block {block.BlockHeight}");

                foreach (var address in addressesByShares)
                    logger.Info(() => $"{address} = {FormatUtil.FormatQuantity(shares[address])} ({shares[address]}) discarded shares, block {block.BlockHeight}");
            }
        }

        #endregion // IPayoutScheme

        private async Task<DateTime?> CalculateRewardsAsync(IMiningPool pool, IPayoutHandler payoutHandler, decimal window, Block block, decimal blockReward,
            Dictionary<string, double> shares, Dictionary<string, decimal> rewards, CancellationToken ct)
        {
            var poolConfig = pool.Config;
            var done = false;
            var before = block.Created;
            var inclusive = true;
            var pageSize = 50000;
            var currentPage = 0;
            var accumulatedScore = 0.0m;
            var blockRewardRemaining = blockReward;
            DateTime? shareCutOffDate = null;

            while (!done && !ct.IsCancellationRequested)
            {
                logger.Info(() => $"Fetching page {currentPage} of shares for pool {poolConfig.Id}, block {block.BlockHeight}");

                var page = await shareReadFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.ReadSharesBeforeCreatedAsync(con, poolConfig.Id, before, inclusive, pageSize))); //, sw, logger));

                inclusive = false;
                currentPage++;

                for (var i = 0; !done && i < page.Length; i++)
                {
                    var share = page[i];
                    var address = share.Miner;
                    var shareDiffAdjusted = payoutHandler.AdjustShareDifficulty(share.Difficulty);

                    if (!shares.ContainsKey(address))
                        shares[address] = shareDiffAdjusted;
                    else
                        shares[address] += shareDiffAdjusted;

                    var score = (decimal)(shareDiffAdjusted / share.NetworkDifficulty);

                    if (accumulatedScore + score >= window)
                    {
                        score = window - accumulatedScore;
                        shareCutOffDate = share.Created;
                        done = true;
                    }

                    var reward = score * blockReward / window;
                    accumulatedScore += score;
                    blockRewardRemaining -= reward;

                    if (blockRewardRemaining <= 0 && !done)
                        throw new OverflowException("blockRewardRemaining < 0");

                    if (reward > 0)
                    {
                        if (!rewards.ContainsKey(address))
                            rewards[address] = reward;
                        else
                            rewards[address] += reward;
                    }
                }

                if (page.Length < pageSize)
                    break;

                before = page[^1].Created;
            }

            logger.Info(() => $"Balance-calculation for pool {poolConfig.Id}, block {block.BlockHeight} completed with accumulated score {accumulatedScore:0.####} ({(accumulatedScore / window) * 100:0.#}%)");

            return shareCutOffDate;
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .RetryAsync(RetryCount, OnPolicyRetry);

            shareReadFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}