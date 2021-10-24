using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Microsoft.Extensions.Hosting;
using Prometheus;

namespace Cybercore.Notifications
{
    public class MetricsPublisher : BackgroundService
    {
        public MetricsPublisher(
            IMessageBus messageBus)
        {
            CreateMetrics();

            this.messageBus = messageBus;
        }

        private Summary btStreamLatencySummary;
        private Counter shareCounter;
        private Summary rpcRequestDurationSummary;
        private readonly IMessageBus messageBus;
        private Counter validShareCounter;
        private Counter invalidShareCounter;
        private Summary hashComputationSummary;
        private Gauge poolConnectionsCounter;

        private void CreateMetrics()
        {
            poolConnectionsCounter = Metrics.CreateGauge("cybercore_pool_connections", "Number of connections per pool", new GaugeConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            btStreamLatencySummary = Metrics.CreateSummary("cybercore_btstream_latency", "Latency of streaming block-templates in ms", new SummaryConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            shareCounter = Metrics.CreateCounter("cybercore_shares_total", "Received shares per pool", new CounterConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            validShareCounter = Metrics.CreateCounter("cybercore_valid_shares_total", "Valid received shares per pool", new CounterConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            invalidShareCounter = Metrics.CreateCounter("cybercore_invalid_shares_total", "Invalid received shares per pool", new CounterConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            rpcRequestDurationSummary = Metrics.CreateSummary("cybercore_rpcrequest_execution_time", "Duration of RPC requests ms", new SummaryConfiguration
            {
                LabelNames = new[] { "pool", "method" }
            });

            hashComputationSummary = Metrics.CreateSummary("cybercore_hash_computation_time", "Duration of RPC requests ms", new SummaryConfiguration
            {
                LabelNames = new[] { "algo" }
            });
        }

        private void OnTelemetryEvent(TelemetryEvent msg)
        {
            switch (msg.Category)
            {
                case TelemetryCategory.Share:
                    shareCounter.WithLabels(msg.GroupId).Inc();

                    if (msg.Success.HasValue)
                    {
                        if (msg.Success.Value)
                            validShareCounter.WithLabels(msg.GroupId).Inc();
                        else
                            invalidShareCounter.WithLabels(msg.GroupId).Inc();
                    }
                    break;

                case TelemetryCategory.BtStream:
                    btStreamLatencySummary.WithLabels(msg.GroupId).Observe(msg.Elapsed.TotalMilliseconds);
                    break;

                case TelemetryCategory.RpcRequest:
                    rpcRequestDurationSummary.WithLabels(msg.GroupId, msg.Info).Observe(msg.Elapsed.TotalMilliseconds);
                    break;

                case TelemetryCategory.Connections:
                    poolConnectionsCounter.WithLabels(msg.GroupId).Set(msg.Total);
                    break;

                case TelemetryCategory.Hash:
                    hashComputationSummary.WithLabels(msg.GroupId).Observe(msg.Elapsed.TotalMilliseconds);
                    break;
            }
        }

        protected override Task ExecuteAsync(CancellationToken ct)
        {
            return messageBus.Listen<TelemetryEvent>()
                .ObserveOn(TaskPoolScheduler.Default)
                .Do(OnTelemetryEvent)
                .ToTask(ct);
        }
    }
}