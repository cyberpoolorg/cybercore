using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using AspNetCoreRateLimit;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

// ReSharper disable InconsistentNaming

namespace Cybercore.Configuration
{
    #region Coin Definitions

    public enum CoinFamily
    {
        [EnumMember(Value = "bitcoin")]
        Bitcoin,

        [EnumMember(Value = "equihash")]
        Equihash,

        [EnumMember(Value = "cryptonote")]
        Cryptonote,

        [EnumMember(Value = "ethereum")]
        Ethereum,

        [EnumMember(Value = "ergo")]
        Ergo,
    }

    public abstract partial class CoinTemplate
    {
        [JsonProperty(Order = -10)]
        public string Name { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string CanonicalName { get; set; }

        [JsonProperty(Order = -9)]
        public string Symbol { get; set; }

        [JsonConverter(typeof(StringEnumConverter), true)]
        [JsonProperty(Order = -8)]
        public CoinFamily Family { get; set; }

        public Dictionary<string, string> ExplorerBlockLinks { get; set; }
        public string ExplorerBlockLink { get; set; }
        public string ExplorerTxLink { get; set; }
        public string ExplorerAccountLink { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }

        [JsonIgnore]
        public static readonly Dictionary<CoinFamily, Type> Families = new()
        {
            { CoinFamily.Bitcoin, typeof(BitcoinTemplate) },
            { CoinFamily.Equihash, typeof(EquihashCoinTemplate) },
            { CoinFamily.Cryptonote, typeof(CryptonoteCoinTemplate) },
            { CoinFamily.Ethereum, typeof(EthereumCoinTemplate) },
            { CoinFamily.Ergo, typeof(ErgoCoinTemplate) },
        };
    }

    public enum BitcoinSubfamily
    {
        [EnumMember(Value = "none")]
        None,
    }

    public partial class BitcoinTemplate : CoinTemplate
    {
        public partial class BitcoinNetworkParams
        {
            [JsonExtensionData]
            public IDictionary<string, object> Extra { get; set; }
        }

        [JsonProperty(Order = -7, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(BitcoinSubfamily.None)]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public BitcoinSubfamily Subfamily { get; set; }

        public JObject CoinbaseHasher { get; set; }
        public JObject HeaderHasher { get; set; }
        public JObject BlockHasher { get; set; }

        [JsonProperty("posBlockHasher")]
        public JObject PoSBlockHasher { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1u)]
        public uint CoinbaseTxVersion { get; set; }

        public string CoinbaseTxComment { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasPayee { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasMasterNodes { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasCoinbasePayload { get; set; }

        [JsonProperty("hasFounderFee")]
        public bool HasFounderFee { get; set; }

        [JsonProperty("hasCoinbaseDevReward")]
        public bool HasCoinbaseDevReward { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1.0d)]
        public double ShareMultiplier { get; set; } = 1.0d;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool CoinbaseIgnoreAuxFlags { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsPseudoPoS { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public JToken BlockTemplateRpcExtraParams { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, BitcoinNetworkParams> Networks { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? CoinbaseMinConfimations { get; set; }
    }

    public enum EquihashSubfamily
    {
        [EnumMember(Value = "none")]
        None,
    }

    public partial class EquihashCoinTemplate : CoinTemplate
    {
        public partial class EquihashNetworkParams
        {
            public string Diff1 { get; set; }
            public int SolutionSize { get; set; } = 1344;
            public int SolutionPreambleSize { get; set; } = 3;
            public JObject Solver { get; set; }
            public string CoinbaseTxNetwork { get; set; }
            public bool PayFoundersReward { get; set; }
            public bool PayFundingStream { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public decimal PercentFoundersReward { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string[] FoundersRewardAddresses { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ulong FoundersRewardSubsidySlowStartInterval { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ulong FoundersRewardSubsidyHalvingInterval { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public decimal PercentTreasuryReward { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ulong TreasuryRewardStartBlockHeight { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string[] TreasuryRewardAddresses { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public double TreasuryRewardAddressChangeInterval { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public uint? OverwinterActivationHeight { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public uint? OverwinterTxVersion { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public uint? OverwinterTxVersionGroupId { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public uint? SaplingActivationHeight { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public uint? SaplingTxVersion { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public uint? SaplingTxVersionGroupId { get; set; }
        }

        [JsonProperty(Order = -7, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(EquihashSubfamily.None)]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public EquihashSubfamily Subfamily { get; set; }

        public Dictionary<string, EquihashNetworkParams> Networks { get; set; }
        public bool UsesZCashAddressFormat { get; set; } = true;
        public bool UseBitcoinPayoutHandler { get; set; }
    }

    public enum CryptonoteSubfamily
    {
        [EnumMember(Value = "none")]
        None,
    }

    public enum CryptonightHashType
    {
        [EnumMember(Value = "randomx")]
        RandomX,
    }

    public partial class CryptonoteCoinTemplate : CoinTemplate
    {
        [JsonProperty(Order = -7, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(CryptonoteSubfamily.None)]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public CryptonoteSubfamily Subfamily { get; set; }

        [JsonConverter(typeof(StringEnumConverter), true)]
        [JsonProperty(Order = -5)]
        public CryptonightHashType Hash { get; set; }

        [JsonProperty(Order = -4, DefaultValueHandling = DefaultValueHandling.Include)]
        public int HashVariant { get; set; }

        public decimal SmallestUnit { get; set; }
        public ulong AddressPrefix { get; set; }
        public ulong AddressPrefixTestnet { get; set; }
        public ulong AddressPrefixStagenet { get; set; }
        public ulong AddressPrefixIntegrated { get; set; }
        public ulong AddressPrefixIntegratedStagenet { get; set; }
        public ulong AddressPrefixIntegratedTestnet { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1.0d)]
        public decimal BlockrewardMultiplier { get; set; }
    }

    public enum EthereumSubfamily
    {
        [EnumMember(Value = "none")]
        None,
    }

    public partial class EthereumCoinTemplate : CoinTemplate
    {
        [JsonProperty(Order = -7, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(EthereumSubfamily.None)]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public EthereumSubfamily Subfamily { get; set; }
    }

    public partial class ErgoCoinTemplate : CoinTemplate
    {
    }

    #endregion // Coin Definitions

    public enum PayoutScheme
    {
        PPLNS = 1,
        PROP = 2,
        SOLO = 3,
    }

    public partial class ClusterLoggingConfig
    {
        public string Level { get; set; }
        public bool EnableConsoleLog { get; set; }
        public bool EnableConsoleColors { get; set; }
        public string LogFile { get; set; }
        public string ApiLogFile { get; set; }
        public bool PerPoolLogFile { get; set; }
        public string LogBaseDirectory { get; set; }
    }

    public partial class NetworkEndpointConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    public partial class AuthenticatedNetworkEndpointConfig : NetworkEndpointConfig
    {
        public string User { get; set; }
        public string Password { get; set; }
    }

    public class DaemonEndpointConfig : AuthenticatedNetworkEndpointConfig
    {
        public bool Ssl { get; set; }
        public bool Http2 { get; set; }
        public string Category { get; set; }
        public string HttpPath { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public class DatabaseConfig : AuthenticatedNetworkEndpointConfig
    {
        public string Database { get; set; }
    }

    public class TcpProxyProtocolConfig
    {
        public bool Enable { get; set; }
        public bool Mandatory { get; set; }
        public string[] ProxyAddresses { get; set; }
    }

    public class PoolEndpoint
    {
        public string ListenAddress { get; set; }
        public string Name { get; set; }
        public double Difficulty { get; set; }
        public TcpProxyProtocolConfig TcpProxyProtocol { get; set; }
        public VarDiffConfig VarDiff { get; set; }
        public bool Tls { get; set; }
        public string TlsPfxFile { get; set; }
    }

    public partial class VarDiffConfig
    {
        public double MinDiff { get; set; }
        public double? MaxDiff { get; set; }
        public double? MaxDelta { get; set; }
        public double TargetTime { get; set; }
        public double RetargetTime { get; set; }
        public double VariancePercent { get; set; }
    }

    public enum BanManagerKind
    {
        Integrated = 1,
        IpTables
    }

    public class ClusterBanningConfig
    {
        public BanManagerKind? Manager { get; set; }
        public bool? BanOnJunkReceive { get; set; }
        public bool? BanOnInvalidShares { get; set; }
    }

    public partial class PoolShareBasedBanningConfig
    {
        public bool Enabled { get; set; }
        public int CheckThreshold { get; set; }
        public double InvalidPercent { get; set; }
        public int Time { get; set; }
    }

    public partial class PoolPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public decimal MinimumPayment { get; set; }
        public PayoutScheme PayoutScheme { get; set; }
        public JToken PayoutSchemeConfig { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class ClusterPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public int Interval { get; set; }
        public string CoinbaseString { get; set; }
    }

    public partial class PersistenceConfig
    {
        public DatabaseConfig Postgres { get; set; }
    }

    public class RewardRecipient
    {
        public string Address { get; set; }
        public decimal Percentage { get; set; }
        public string Type { get; set; }
    }

    public partial class EmailSenderConfig : AuthenticatedNetworkEndpointConfig
    {
        public string FromAddress { get; set; }
        public string FromName { get; set; }
    }

    public partial class PushoverConfig
    {
        public bool Enabled { get; set; }
        public string User { get; set; }
        public string Token { get; set; }
    }

    public partial class AdminNotifications
    {
        public bool Enabled { get; set; }
        public string EmailAddress { get; set; }
        public bool NotifyBlockFound { get; set; }
        public bool NotifyPaymentSuccess { get; set; }
    }

    public partial class NotificationsConfig
    {
        public bool Enabled { get; set; }
        public EmailSenderConfig Email { get; set; }
        public PushoverConfig Pushover { get; set; }
        public AdminNotifications Admin { get; set; }
    }

    public partial class ApiRateLimitConfig
    {
        public bool Disabled { get; set; }
        public RateLimitRule[] Rules { get; set; }
        public string[] IpWhitelist { get; set; }
    }

    public class ApiTlsConfig
    {
        public bool Enabled { get; set; }
        public string TlsPfxFile { get; set; }
        public string TlsPfxPassword { get; set; }
    }


    public partial class ApiConfig
    {
        public bool Enabled { get; set; }
        public string ListenAddress { get; set; }
        public int Port { get; set; }
        public ApiTlsConfig Tls { get; set; }
        public ApiRateLimitConfig RateLimiting { get; set; }
        public int? AdminPort { get; set; }
        public int? MetricsPort { get; set; }
        public string[] AdminIpWhitelist { get; set; }
        public string[] MetricsIpWhitelist { get; set; }
    }

    public partial class ZmqPubSubEndpointConfig
    {
        public string Url { get; set; }
        public string Topic { get; set; }
        public string SharedEncryptionKey { get; set; }
    }

    public partial class ShareRelayEndpointConfig
    {
        public string Url { get; set; }
        public string SharedEncryptionKey { get; set; }
    }

    public partial class ShareRelayConfig
    {
        public string PublishUrl { get; set; }
        public bool Connect { get; set; }
        public string SharedEncryptionKey { get; set; }
    }

    public partial class Statistics
    {
        public int? UpdateInterval { get; set; }
        public int? HashrateCalculationWindow { get; set; }
        public int? GcInterval { get; set; }
        public int? CleanupDays { get; set; }
    }

    public class NicehashClusterConfig
    {
        public bool EnableAutoDiff { get; set; }
    }

    public partial class PoolConfig
    {
        public string Id { get; set; }
        public string Coin { get; set; }
        public string PoolName { get; set; }
        public bool Enabled { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public DaemonEndpointConfig[] Daemons { get; set; }
        public PoolPaymentProcessingConfig PaymentProcessing { get; set; }
        public int PaymentInterval { get; set; }
        public PoolShareBasedBanningConfig Banning { get; set; }
        public RewardRecipient[] RewardRecipients { get; set; }
        public string Address { get; set; }
        public string PubKey { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockTimeInterval { get; set; }
        public bool? EnableInternalStratum { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(false)]
        public bool UseP2PK { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class ClusterConfig
    {
        public byte? InstanceId { get; set; }
        public string[] CoinTemplates { get; set; }
        public string ClusterName { get; set; }
        public ClusterLoggingConfig Logging { get; set; }
        public ClusterBanningConfig Banning { get; set; }
        public PersistenceConfig Persistence { get; set; }
        public ClusterPaymentProcessingConfig PaymentProcessing { get; set; }
        public NotificationsConfig Notifications { get; set; }
        public ApiConfig Api { get; set; }
        public Statistics Statistics { get; set; }
        public NicehashClusterConfig Nicehash { get; set; }
        public ShareRelayConfig ShareRelay { get; set; }
        public ShareRelayEndpointConfig[] ShareRelays { get; set; }
        public int? EquihashMaxThreads { get; set; }
        public string ShareRecoveryFile { get; set; }
        public PoolConfig[] Pools { get; set; }
    }
}