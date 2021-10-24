using System;
using System.Globalization;
using Cybercore.Blockchain;
using Cybercore.Configuration;
using Cybercore.Messaging;
using Cybercore.Mining;
using Cybercore.Notifications.Messages;
using Cybercore.Persistence.Model;

namespace Cybercore.Extensions
{
    public static class MessageBusExtensions
    {
        public static void NotifyBlockFound(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            string minerExplorerLink = null;

            if (!string.IsNullOrEmpty(coin.ExplorerAccountLink))
                minerExplorerLink = string.Format(coin.ExplorerAccountLink, block.Miner);

            messageBus.SendMessage(new BlockFoundNotification
            {
                PoolId = poolId,
                BlockHeight = block.BlockHeight,
                Symbol = coin.Symbol,
                Name = coin.CanonicalName ?? coin.Name,
                Miner = block.Miner,
                MinerExplorerLink = minerExplorerLink,
                Source = block.Source,
            });
        }

        public static void NotifyBlockConfirmationProgress(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            messageBus.SendMessage(new BlockConfirmationProgressNotification
            {
                PoolId = poolId,
                BlockHeight = block.BlockHeight,
                Symbol = coin.Symbol,
                Name = coin.CanonicalName ?? coin.Name,
                Effort = block.Effort,
                Progress = block.ConfirmationProgress,
            });
        }

        public static void NotifyBlockUnlocked(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            string blockExplorerLink = null;
            string minerExplorerLink = null;

            if (block.Status != BlockStatus.Orphaned)
            {
                if (coin.ExplorerBlockLinks.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : "block", out var blockInfobaseUrl))
                {
                    if (blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                        blockExplorerLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                    else if (blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                        blockExplorerLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                }

                if (!string.IsNullOrEmpty(coin.ExplorerAccountLink))
                    minerExplorerLink = string.Format(coin.ExplorerAccountLink, block.Miner);
            }

            messageBus.SendMessage(new BlockUnlockedNotification
            {
                PoolId = poolId,
                BlockHeight = block.BlockHeight,
                BlockType = block.Type,
                Symbol = coin.Symbol,
                Name = coin.CanonicalName ?? coin.Name,
                Reward = block.Reward,
                Status = block.Status,
                Effort = block.Effort,
                BlockHash = block.Hash,
                ExplorerLink = blockExplorerLink,
                Miner = block.Miner,
                MinerExplorerLink = minerExplorerLink,
            });
        }

        public static void NotifyChainHeight(this IMessageBus messageBus, string poolId, ulong height, CoinTemplate coin)
        {
            messageBus.SendMessage(new NewChainHeightNotification
            {
                PoolId = poolId,
                BlockHeight = height,
                Symbol = coin.Symbol,
                Name = coin.CanonicalName ?? coin.Name,
            });
        }

        public static void NotifyHashrateUpdated(this IMessageBus messageBus, string poolId, double hashrate, string miner = null, string worker = null)
        {
            messageBus.SendMessage(new HashrateNotification
            {
                PoolId = poolId,
                Hashrate = hashrate,
                Miner = miner,
                Worker = worker,
            });
        }

        public static void NotifyPoolStatus(this IMessageBus messageBus, IMiningPool pool, PoolStatus status)
        {
            messageBus.SendMessage(new PoolStatusNotification
            {
                Pool = pool,
                Status = status
            });
        }

        public static void SendTelemetry(this IMessageBus messageBus, string groupId, TelemetryCategory cat, TimeSpan elapsed,
            bool? success = null, string error = null, int? total = null)
        {
            messageBus.SendMessage(new TelemetryEvent(groupId, cat, elapsed, success, error)
            {
                Total = total ?? 0,
            });
        }

        public static void SendTelemetry(this IMessageBus messageBus, string groupId, TelemetryCategory cat, string info, TimeSpan elapsed,
            bool? success = null, string error = null, int? total = null)
        {
            messageBus.SendMessage(new TelemetryEvent(groupId, cat, info, elapsed, success, error)
            {
                Total = total ?? 0,
            });
        }
    }
}