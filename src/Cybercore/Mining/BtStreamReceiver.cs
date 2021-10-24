using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin.Configuration;
using Cybercore.Blockchain.Cryptonote.Configuration;
using Cybercore.Configuration;
using Cybercore.Contracts;
using Cybercore.Extensions;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Cybercore.Time;
using Microsoft.Extensions.Hosting;
using MoreLinq;
using NLog;
using ZeroMQ;

namespace Cybercore.Mining
{
    public class BtStreamReceiver : BackgroundService
    {
        public BtStreamReceiver(
            IMasterClock clock,
            IMessageBus messageBus,
            ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.clock = clock;
            this.messageBus = messageBus;
            this.clusterConfig = clusterConfig;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IMasterClock clock;
        private readonly IMessageBus messageBus;
        private readonly ClusterConfig clusterConfig;

        private static ZSocket SetupSubSocket(ZmqPubSubEndpointConfig relay, bool silent = false)
        {
            var subSocket = new ZSocket(ZSocketType.SUB);

            if (!string.IsNullOrEmpty(relay.SharedEncryptionKey))
                subSocket.SetupCurveTlsClient(relay.SharedEncryptionKey, logger);

            subSocket.Connect(relay.Url);
            subSocket.SubscribeAll();

            if (!silent)
            {
                if (subSocket.CurveServerKey != null && subSocket.CurveServerKey.Any(x => x != 0))
                    logger.Info($"Monitoring Bt-Stream source {relay.Url} using Curve public-key {subSocket.CurveServerKey.ToHexString()}");
                else
                    logger.Info($"Monitoring Bt-Stream source {relay.Url}");
            }

            return subSocket;
        }

        private void ProcessMessage(ZMessage msg)
        {
            var topic = msg[0].ToString(Encoding.UTF8);
            var flags = msg[1].ReadUInt32();
            var data = msg[2].Read();
            var sent = DateTimeOffset.FromUnixTimeMilliseconds(msg[3].ReadInt64()).DateTime;

            if (flags != 0 && ((flags & 1) == 0))
                flags = BitConverter.ToUInt32(BitConverter.GetBytes(flags).ToNewReverseArray());

            if ((flags & 1) == 1)
            {
                using (var stm = new MemoryStream(data))
                {
                    using (var stmOut = new MemoryStream())
                    {
                        using (var ds = new DeflateStream(stm, CompressionMode.Decompress))
                        {
                            ds.CopyTo(stmOut);
                        }

                        data = stmOut.ToArray();
                    }
                }
            }

            var content = Encoding.UTF8.GetString(data);

            messageBus.SendMessage(new BtStreamMessage(topic, content, sent, DateTime.UtcNow));
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            var endpoints = clusterConfig.Pools.Select(x =>
                    x.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>()?.BtStream ??
                    x.Extra.SafeExtensionDataAs<CryptonotePoolConfigExtra>()?.BtStream)
                .Where(x => x != null)
                .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
                .ToArray();

            if (!endpoints.Any())
                return;

            await Task.Run(() =>
            {
                var timeout = TimeSpan.FromMilliseconds(5000);
                var reconnectTimeout = TimeSpan.FromSeconds(300);

                var relays = endpoints
                    .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
                    .ToArray();

                logger.Info(() => "Online");

                while (!ct.IsCancellationRequested)
                {
                    var lastMessageReceived = relays.Select(_ => clock.Now).ToArray();

                    try
                    {
                        var sockets = relays.Select(x => SetupSubSocket(x)).ToArray();

                        using (new CompositeDisposable(sockets))
                        {
                            var pollItems = sockets.Select(_ => ZPollItem.CreateReceiver()).ToArray();

                            while (!ct.IsCancellationRequested)
                            {
                                if (sockets.PollIn(pollItems, out var messages, out var error, timeout))
                                {
                                    for (var i = 0; i < messages.Length; i++)
                                    {
                                        var msg = messages[i];

                                        if (msg != null)
                                        {
                                            lastMessageReceived[i] = clock.Now;

                                            using (msg)
                                            {
                                                ProcessMessage(msg);
                                            }
                                        }

                                        else if (clock.Now - lastMessageReceived[i] > reconnectTimeout)
                                        {
                                            sockets[i].Dispose();
                                            sockets[i] = SetupSubSocket(relays[i], true);

                                            lastMessageReceived[i] = clock.Now;

                                            logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                        }
                                    }

                                    if (error != null)
                                        logger.Error(() => $"{nameof(ShareReceiver)}: {error.Name} [{error.Name}] during receive");
                                }

                                else
                                {
                                    for (var i = 0; i < messages.Length; i++)
                                    {
                                        if (clock.Now - lastMessageReceived[i] > reconnectTimeout)
                                        {
                                            sockets[i].Dispose();
                                            sockets[i] = SetupSubSocket(relays[i], true);

                                            lastMessageReceived[i] = clock.Now;

                                            logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    catch (Exception ex)
                    {
                        logger.Error(() => $"{nameof(ShareReceiver)}: {ex}");

                        if (!ct.IsCancellationRequested)
                            Thread.Sleep(1000);
                    }
                }

                logger.Info(() => "Offline");
            }, ct);
        }
    }
}