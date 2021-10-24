using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.Metadata;
using AutoMapper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Api;
using Cybercore.Api.Controllers;
using Cybercore.Api.Middlewares;
using Cybercore.Api.Responses;
using Cybercore.Blockchain.Ergo;
using Cybercore.Configuration;
using Cybercore.Crypto.Hashing.Algorithms;
using Cybercore.Crypto.Hashing.Equihash;
using Cybercore.Crypto.Hashing.Ethash;
using Cybercore.Extensions;
using Cybercore.Messaging;
using Cybercore.Mining;
using Cybercore.Native;
using Cybercore.Notifications;
using Cybercore.Payments;
using Cybercore.Persistence.Dummy;
using Cybercore.Persistence.Postgres;
using Cybercore.Persistence.Postgres.Repositories;
using Cybercore.Util;
using AspNetCoreRateLimit;
using FluentValidation;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin.Zcash;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Conditions;
using NLog.Config;
using NLog.Extensions.Hosting;
using NLog.Extensions.Logging;
using NLog.Layouts;
using NLog.Targets;
using Prometheus;
using WebSocketManager;
using ILogger = NLog.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable PossibleNullReferenceException

namespace Cybercore
{
    public class Program : BackgroundService
    {
        public static async Task Main(string[] args)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

                if (!ParseCommandLine(args, out var configFile))
                    return;

                isShareRecoveryMode = shareRecoveryOption.HasValue();

                Logo();
                clusterConfig = ReadConfig(configFile);

                if (dumpConfigOption.HasValue())
                {
                    DumpParsedConfig(clusterConfig);
                    return;
                }

                ValidateConfig();
                ConfigureLogging();
                LogRuntimeInfo();
                ValidateRuntimeEnvironment();

                var hostBuilder = new HostBuilder();

                hostBuilder
                    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                    .ConfigureContainer((Action<ContainerBuilder>)ConfigureAutofac)
                    .UseNLog()
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddNLog();
                        logging.SetMinimumLevel(LogLevel.Trace);
                    })
                    .ConfigureServices((ctx, services) =>
                    {
                        services.AddHttpClient();
                        services.AddMemoryCache();

                        ConfigureBackgroundServices(services);

                        services.AddHostedService<Program>();
                    });

                if (clusterConfig.Api == null || clusterConfig.Api.Enabled)
                {
                    var address = clusterConfig.Api?.ListenAddress != null
                        ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                        : IPAddress.Parse("127.0.0.1");

                    var port = clusterConfig.Api?.Port ?? 4000;
                    var enableApiRateLimiting = clusterConfig.Api?.RateLimiting?.Disabled != true;

                    var apiTlsEnable =
                        clusterConfig.Api?.Tls?.Enabled == true ||
                        !string.IsNullOrEmpty(clusterConfig.Api?.Tls?.TlsPfxFile);

                    if (apiTlsEnable)
                    {
                        if (!File.Exists(clusterConfig.Api.Tls.TlsPfxFile))
                            logger.ThrowLogPoolStartupException($"Certificate file {clusterConfig.Api.Tls.TlsPfxFile} does not exist!");
                    }

                    hostBuilder.ConfigureWebHost(builder =>
                    {
                        builder.ConfigureServices(services =>
                        {
                            if (enableApiRateLimiting)
                            {
                                services.Configure<IpRateLimitOptions>(ConfigureIpRateLimitOptions);
                                services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
                                services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
                                services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
                                services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
                            }

                            services.AddSingleton<PoolApiController, PoolApiController>();
                            services.AddSingleton<AdminApiController, AdminApiController>();
                            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

                            services.AddMvc(options => { options.EnableEndpointRouting = false; })
                                .SetCompatibilityVersion(CompatibilityVersion.Latest)
                                .AddControllersAsServices()
                                .AddJsonOptions(options => { options.JsonSerializerOptions.WriteIndented = true; });

                            services.AddResponseCompression();
                            services.AddCors();
                            services.AddWebSocketManager();
                        })
                        .UseKestrel(options =>
                        {
                            options.Listen(address, port, listenOptions =>
                            {
                                if (apiTlsEnable)
                                    listenOptions.UseHttps(clusterConfig.Api.Tls.TlsPfxFile, clusterConfig.Api.Tls.TlsPfxPassword);
                            });
                        })
                        .Configure(app =>
                        {
                            if (enableApiRateLimiting)
                                app.UseIpRateLimiting();

                            app.UseMiddleware<ApiExceptionHandlingMiddleware>();

                            UseIpWhiteList(app, true, new[] { "/api/admin" }, clusterConfig.Api?.AdminIpWhitelist);
                            UseIpWhiteList(app, true, new[] { "/metrics" }, clusterConfig.Api?.MetricsIpWhitelist);

                            app.UseResponseCompression();
                            app.UseCors(corsPolicyBuilder => corsPolicyBuilder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
                            app.UseWebSockets();
                            app.MapWebSocketManager("/notifications", app.ApplicationServices.GetService<WebSocketNotificationsRelay>());
                            app.UseMetricServer();
                            app.UseMvc();

                        });

                        logger.Info(() => $"Prometheus Metrics {address}:{port}/metrics");
                        logger.Info(() => $"WebSocket notifications streaming {address}:{port}/notifications");
                    });
                }

                host = hostBuilder
                    .UseConsoleLifetime()
                    .Build();

                await host.RunAsync();
            }

            catch (PoolStartupAbortException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    await Console.Error.WriteLineAsync(ex.Message);

                await Console.Error.WriteLineAsync("\nCluster cannot start. Good Bye!");
            }

            catch (JsonException)
            {
            }

            catch (IOException)
            {
            }

            catch (AggregateException ex)
            {
                if (ex.InnerExceptions.First() is not PoolStartupAbortException)
                    Console.Error.WriteLine(ex);

                await Console.Error.WriteLineAsync("Cluster cannot start. Good Bye!");
            }

            catch (OperationCanceledException)
            {
            }

            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);

                await Console.Error.WriteLineAsync("Cluster cannot start. Good Bye!");
            }
        }

        private static void ConfigureBackgroundServices(IServiceCollection services)
        {
            services.AddHostedService<NotificationService>();
            services.AddHostedService<BtStreamReceiver>();

            if (clusterConfig.ShareRelay == null)
            {
                services.AddHostedService<ShareRecorder>();
                services.AddHostedService<ShareReceiver>();
            }

            else
                services.AddHostedService<ShareRelay>();

            if (clusterConfig.Api == null || clusterConfig.Api.Enabled)
                services.AddHostedService<MetricsPublisher>();

            if (clusterConfig.PaymentProcessing?.Enabled == true &&
               clusterConfig.Pools.Any(x => x.PaymentProcessing?.Enabled == true))
                services.AddHostedService<PayoutManager>();
            else
                logger.Info("Payment processing is not enabled");


            if (clusterConfig.ShareRelay == null)
            {
                services.AddHostedService<StatsRecorder>();
            }
        }

        private static IHost host;
        private readonly IComponentContext container;
        private static ILogger logger;
        private static CommandOption dumpConfigOption;
        private static CommandOption shareRecoveryOption;
        private static bool isShareRecoveryMode;
        private static ClusterConfig clusterConfig;
        private static readonly ConcurrentDictionary<string, IMiningPool> pools = new();

        private static readonly AdminGcStats gcStats = new();
        private static readonly Regex regexJsonTypeConversionError =
            new("\"([^\"]+)\"[^\']+\'([^\']+)\'.+\\s(\\d+),.+\\s(\\d+)", RegexOptions.Compiled);

        public Program(IComponentContext container)
        {
            this.container = container;
        }

        private static void ConfigureAutofac(ContainerBuilder builder)
        {
            builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);
            builder.RegisterInstance(clusterConfig);
            builder.RegisterInstance(pools);
            builder.RegisterInstance(gcStats);

            var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });
            builder.Register((ctx, parms) => amConf.CreateMapper());

            ConfigurePersistence(builder);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (isShareRecoveryMode)
            {
                await RecoverSharesAsync(shareRecoveryOption.Value());
                return;
            }

            ConfigureMisc();

            if (clusterConfig.InstanceId.HasValue)
                logger.Info($"This is cluster node {clusterConfig.InstanceId.Value}{(!string.IsNullOrEmpty(clusterConfig.ClusterName) ? $" [{clusterConfig.ClusterName}]" : string.Empty)}");

            var coinTemplates = LoadCoinTemplates();
            logger.Info($"{coinTemplates.Keys.Count} coins loaded from {string.Join(", ", clusterConfig.CoinTemplates)}");

            await Task.WhenAll(clusterConfig.Pools
                .Where(config => config.Enabled)
                .Select(config => RunPool(config, coinTemplates, ct)));
        }

        private Task RunPool(PoolConfig poolConfig, Dictionary<string, CoinTemplate> coinTemplates, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                if (!coinTemplates.TryGetValue(poolConfig.Coin, out var template))
                    logger.ThrowLogPoolStartupException($"Pool {poolConfig.Id} references undefined coin '{poolConfig.Coin}'");

                poolConfig.Template = template;

                var poolImpl = container.Resolve<IEnumerable<Meta<Lazy<IMiningPool, CoinFamilyAttribute>>>>()
                    .First(x => x.Value.Metadata.SupportedFamilies.Contains(poolConfig.Template.Family)).Value;

                var pool = poolImpl.Value;
                pool.Configure(poolConfig, clusterConfig);
                pools[poolConfig.Id] = pool;

                await pool.RunAsync(ct);
            }, ct);
        }

        private Task RecoverSharesAsync(string recoveryFilename)
        {
            var shareRecorder = container.Resolve<ShareRecorder>();
            return shareRecorder.RecoverSharesAsync(clusterConfig, recoveryFilename);
        }

        private static void LogRuntimeInfo()
        {
            logger.Info(() => $"{RuntimeInformation.FrameworkDescription.Trim()} on {RuntimeInformation.OSDescription.Trim()} [{RuntimeInformation.ProcessArchitecture}]");
        }

        private static void ValidateConfig()
        {
            foreach (var config in clusterConfig.Pools)
            {
                config.EnableInternalStratum ??= clusterConfig.ShareRelays == null || clusterConfig.ShareRelays.Length == 0;
            }

            try
            {
                clusterConfig.Validate();

                if (clusterConfig.Notifications?.Admin?.Enabled == true)
                {
                    if (string.IsNullOrEmpty(clusterConfig.Notifications?.Email?.FromName))
                        logger.ThrowLogPoolStartupException($"Notifications are enabled but email sender name is not configured (notifications.email.fromName)");

                    if (string.IsNullOrEmpty(clusterConfig.Notifications?.Email?.FromAddress))
                        logger.ThrowLogPoolStartupException($"Notifications are enabled but email sender address name is not configured (notifications.email.fromAddress)");

                    if (string.IsNullOrEmpty(clusterConfig.Notifications?.Admin?.EmailAddress))
                        logger.ThrowLogPoolStartupException($"Admin notifications are enabled but recipient address is not configured (notifications.admin.emailAddress)");
                }
            }

            catch (ValidationException ex)
            {
                Console.Error.WriteLine($"Configuration is not valid:\n\n{string.Join("\n", ex.Errors.Select(x => "=> " + x.ErrorMessage))}");
                throw new PoolStartupAbortException(string.Empty);
            }
        }

        private static void DumpParsedConfig(ClusterConfig config)
        {
            Console.WriteLine("\nCurrent configuration as parsed from config file:");

            Console.WriteLine(JsonConvert.SerializeObject(config, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            }));
        }

        private static bool ParseCommandLine(string[] args, out string configFile)
        {
            configFile = null;

            var app = new CommandLineApplication
            {
                FullName = "Cybercore - Mining Pool Engine",
                ShortVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}",
                LongVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}"
            };

            var versionOption = app.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = app.Option("-c|--config <configfile>", "Configuration File",
                CommandOptionType.SingleValue);
            dumpConfigOption = app.Option("-dc|--dumpconfig",
                "Dump the configuration (useful for trouble-shooting typos in the config file)",
                CommandOptionType.NoValue);
            shareRecoveryOption = app.Option("-rs", "Import lost shares using existing recovery file",
                CommandOptionType.SingleValue);
            app.HelpOption("-? | -h | --help");

            app.Execute(args);

            if (versionOption.HasValue())
            {
                app.ShowVersion();
                return false;
            }

            if (!configFileOption.HasValue())
            {
                app.ShowHelp();
                return false;
            }

            configFile = configFileOption.Value();

            return true;
        }

        private static ClusterConfig ReadConfig(string file)
        {
            try
            {
                Console.WriteLine($"Using configuration file {file}\n");

                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                using (var reader = new StreamReader(file, Encoding.UTF8))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<ClusterConfig>(jsonReader);
                    }
                }
            }

            catch (JsonSerializationException ex)
            {
                HumanizeJsonParseException(ex);
                throw;
            }

            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                throw;
            }

            catch (IOException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        private static void HumanizeJsonParseException(JsonSerializationException ex)
        {
            var m = regexJsonTypeConversionError.Match(ex.Message);

            if (m.Success)
            {
                var value = m.Groups[1].Value;
                var type = Type.GetType(m.Groups[2].Value);
                var line = m.Groups[3].Value;
                var col = m.Groups[4].Value;

                if (type == typeof(PayoutScheme))
                    Console.Error.WriteLine($"Error: Payout scheme '{value}' is not (yet) supported (line {line}, column {col})");
            }

            else
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void ValidateRuntimeEnvironment()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.UserName == "root")
                logger.Warn(() => "Running as root is discouraged!");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X86)
                throw new PoolStartupAbortException("Cybercore requires 64-Bit Windows");
        }

        private static void Logo()
        {
            Console.WriteLine(@"
	    ______________.___.________________________________________  ________ _____________________  ____   ____________  
	    \_   ___ \__  |   |\______   \_   _____/\______   \_   ___ \ \_____  \\______   \_   _____/  \   \ /   /\_____  \ 
	    /    \  \//   |   | |    |  _/|    __)_  |       _/    \  \/  /   |   \|       _/|    __)_    \   Y   /  /  ____/ 
	    \     \___\____   | |    |   \|        \ |    |   \     \____/    |    \    |   \|        \    \     /  /       \ 
	     \______  / ______| |______  /_______  / |____|_  /\______  /\_______  /____|_  /_______  /     \___/   \_______ \
	            \/\/               \/        \/         \/        \/         \/       \/        \/                      \/
	    ");
            Console.WriteLine(" https://github.com/cyberpoolorg/cybercore\n");
            Console.WriteLine(" Please contribute to the development of the project by donating:\n");
            Console.WriteLine(" BTC - 1H8Ze41raYGXYAiLAEiN12vmGH34A7cuua");
            Console.WriteLine(" LTC - LSE19SHK3DMxFVyk35rhTFaw7vr1f8zLkT");
            Console.WriteLine(" ETH - 0x52FdE416C1D51525aEA390E39CfD5016dAFC01F7");
            Console.WriteLine(" ETC - 0x6F2B787312Df5B08a6b7073Bdb8fF04442B6A11f");
            Console.WriteLine();
        }

        private static void ConfigureLogging()
        {
            var config = clusterConfig.Logging;
            var loggingConfig = new LoggingConfiguration();

            if (config != null)
            {
                var level = !string.IsNullOrEmpty(config.Level)
                    ? NLog.LogLevel.FromString(config.Level)
                    : NLog.LogLevel.Info;

                var layout = "[${longdate}] [${level:format=FirstCharacter:uppercase=true}] [${logger:shortName=true}] ${message} ${exception:format=ToString,StackTrace}";

                var nullTarget = new NullTarget("null");

                loggingConfig.AddTarget(nullTarget);
                loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Internal.*", true);
                loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Infrastructure.*", true);
                loggingConfig.AddRule(level, NLog.LogLevel.Warn, nullTarget, "System.Net.Http.HttpClient.*", true);

                if (!string.IsNullOrEmpty(config.ApiLogFile) && !isShareRecoveryMode)
                {
                    var target = new FileTarget("file")
                    {
                        FileName = GetLogPath(config, config.ApiLogFile),
                        FileNameKind = FilePathKind.Unknown,
                        Layout = layout
                    };

                    loggingConfig.AddTarget(target);
                    loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target, "Microsoft.AspNetCore.*", true);
                }

                if (config.EnableConsoleLog || isShareRecoveryMode)
                {
                    if (config.EnableConsoleColors)
                    {
                        var target = new ColoredConsoleTarget("console")
                        {
                            Layout = layout
                        };

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Trace"),
                            ConsoleOutputColor.DarkMagenta, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Debug"),
                            ConsoleOutputColor.Gray, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Info"),
                            ConsoleOutputColor.White, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Warn"),
                            ConsoleOutputColor.Yellow, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Error"),
                            ConsoleOutputColor.Red, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Fatal"),
                            ConsoleOutputColor.DarkRed, ConsoleOutputColor.White));

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target);
                    }

                    else
                    {
                        var target = new ConsoleTarget("console")
                        {
                            Layout = layout
                        };

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target);
                    }
                }

                if (!string.IsNullOrEmpty(config.LogFile) && !isShareRecoveryMode)
                {
                    var target = new FileTarget("file")
                    {
                        FileName = GetLogPath(config, config.LogFile),
                        FileNameKind = FilePathKind.Unknown,
                        Layout = layout
                    };

                    loggingConfig.AddTarget(target);
                    loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target);
                }

                if (config.PerPoolLogFile && !isShareRecoveryMode)
                {
                    foreach (var poolConfig in clusterConfig.Pools)
                    {
                        var target = new FileTarget(poolConfig.Id)
                        {
                            FileName = GetLogPath(config, poolConfig.Id + ".log"),
                            FileNameKind = FilePathKind.Unknown,
                            Layout = layout
                        };

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target, poolConfig.Id);
                    }
                }
            }

            LogManager.Configuration = loggingConfig;

            logger = LogManager.GetLogger("Core");
        }

        private static Layout GetLogPath(ClusterLoggingConfig config, string name)
        {
            if (string.IsNullOrEmpty(config.LogBaseDirectory))
                return name;

            return Path.Combine(config.LogBaseDirectory, name);
        }

        private void ConfigureMisc()
        {
            ZcashNetworks.Instance.EnsureRegistered();
            var messageBus = container.Resolve<IMessageBus>();
            EquihashSolver.messageBus = messageBus;

            if (clusterConfig.EquihashMaxThreads.HasValue)
                EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads.Value;

            Dag.messageBus = messageBus;
            Verthash.messageBus = messageBus;
            LibRandomX.messageBus = messageBus;
        }

        private static void ConfigurePersistence(ContainerBuilder builder)
        {
            if (clusterConfig.Persistence == null &&
                clusterConfig.PaymentProcessing?.Enabled == true &&
                clusterConfig.ShareRelay == null)
                logger.ThrowLogPoolStartupException("Persistence is not configured!");

            if (clusterConfig.Persistence?.Postgres != null)
                ConfigurePostgres(clusterConfig.Persistence.Postgres, builder);
            else
                ConfigureDummyPersistence(builder);
        }

        private static void ConfigurePostgres(DatabaseConfig pgConfig, ContainerBuilder builder)
        {
            if (string.IsNullOrEmpty(pgConfig.Host))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'host'");

            if (pgConfig.Port == 0)
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'port'");

            if (string.IsNullOrEmpty(pgConfig.Database))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'database'");

            if (string.IsNullOrEmpty(pgConfig.User))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'user'");

            var connectionString = $"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};CommandTimeout=900;";

            builder.RegisterInstance(new PgConnectionFactory(connectionString))
                .AsImplementedInterfaces();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t =>
                    t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private static void ConfigureDummyPersistence(ContainerBuilder builder)
        {
            builder.RegisterInstance(new DummyConnectionFactory(string.Empty))
                .AsImplementedInterfaces();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t =>
                    t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private Dictionary<string, CoinTemplate> LoadCoinTemplates()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var defaultTemplates = Path.Combine(basePath, "coins.json");

            clusterConfig.CoinTemplates = new[]
            {
                defaultTemplates
            }
            .Concat(clusterConfig.CoinTemplates != null ?
                clusterConfig.CoinTemplates.Where(x => x != defaultTemplates) :
                Array.Empty<string>())
            .ToArray();

            return CoinTemplateLoader.Load(container, clusterConfig.CoinTemplates);
        }

        private static void UseIpWhiteList(IApplicationBuilder app, bool defaultToLoopback, string[] locations, string[] whitelist)
        {
            var ipList = whitelist?.Select(IPAddress.Parse).ToList();
            if (defaultToLoopback && (ipList == null || ipList.Count == 0))
                ipList = new List<IPAddress>(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback, IPUtils.IPv4LoopBackOnIPv6 });

            if (ipList.Count > 0)
            {
                if (!ipList.Any(x => x.Equals(IPAddress.Loopback)))
                    ipList.Add(IPAddress.Loopback);
                if (!ipList.Any(x => x.Equals(IPAddress.IPv6Loopback)))
                    ipList.Add(IPAddress.IPv6Loopback);
                if (!ipList.Any(x => x.Equals(IPUtils.IPv4LoopBackOnIPv6)))
                    ipList.Add(IPUtils.IPv4LoopBackOnIPv6);

                logger.Info(() => $"API Access to {string.Join(",", locations)} restricted to {string.Join(",", ipList.Select(x => x.ToString()))}");

                app.UseMiddleware<IPAccessWhitelistMiddleware>(locations, ipList.ToArray());
            }
        }

        private static void ConfigureIpRateLimitOptions(IpRateLimitOptions options)
        {
            options.EnableEndpointRateLimiting = false;

            options.EndpointWhitelist = new List<string>
            {
                "*:/api/admin",
                "get:/metrics",
                "*:/notifications",
            };

            options.IpWhitelist = clusterConfig.Api?.RateLimiting?.IpWhitelist?.ToList();

            if (options.IpWhitelist == null || options.IpWhitelist.Count == 0)
            {
                options.IpWhitelist = new List<string>
                {
                    IPAddress.Loopback.ToString(),
                    IPAddress.IPv6Loopback.ToString(),
                    IPUtils.IPv4LoopBackOnIPv6.ToString()
                };
            }

            var rules = clusterConfig.Api?.RateLimiting?.Rules?.ToList();

            if (rules == null || rules.Count == 0)
            {
                rules = new List<RateLimitRule>
                {
                    new()
                    {
                        Endpoint = "*",
                        Period = "1s",
                        Limit = 5,
                    }
                };
            }

            options.GeneralRules = rules;

            logger.Info(() => $"API access limited to {(string.Join(", ", rules.Select(x => $"{x.Limit} requests per {x.Period}")))}, except from {string.Join(", ", options.IpWhitelist)}");
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (logger != null)
            {
                logger.Error(e.ExceptionObject);
                LogManager.Flush(TimeSpan.Zero);
            }

            Console.Error.WriteLine("** AppDomain unhandled exception: {0}", e.ExceptionObject);
        }
    }
}