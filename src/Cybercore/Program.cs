using Autofac;
using Autofac.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Net;
using Cybercore.Api;
using Cybercore.Api.Controllers;
using Cybercore.Api.Middlewares;
using Cybercore.Mining;
using Cybercore.Util;
using AspNetCoreRateLimit;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog;
using NLog.Extensions.Hosting;
using NLog.Extensions.Logging;
using Prometheus;
using WebSocketManager;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Cybercore;

// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable PossibleNullReferenceException
try
{
<<<<<<< HEAD
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
                        ? (clusterConfig.Api.ListenAddress != "*"
                            ? IPAddress.Parse(clusterConfig.Api.ListenAddress)
                            : IPAddress.Any)
                        : IPAddress.Parse("127.0.0.1");

                    var port = clusterConfig.Api?.Port ?? 4000;
                    var enableApiRateLimiting = clusterConfig.Api?.RateLimiting?.Disabled != true;

                    var apiTlsEnable =
                        clusterConfig.Api?.Tls?.Enabled == true ||
                        !string.IsNullOrEmpty(clusterConfig.Api?.Tls?.TlsPfxFile);

                    if (apiTlsEnable)
                    {
                        if (!File.Exists(clusterConfig.Api.Tls.TlsPfxFile))
                            logger.ThrowLogPoolStartupException(
                                $"Certificate file {clusterConfig.Api.Tls.TlsPfxFile} does not exist!");
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
                                        listenOptions.UseHttps(clusterConfig.Api.Tls.TlsPfxFile,
                                            clusterConfig.Api.Tls.TlsPfxPassword);
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
                                app.UseCors(corsPolicyBuilder =>
                                    corsPolicyBuilder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
                                app.UseWebSockets();
                                app.MapWebSocketManager("/notifications",
                                    app.ApplicationServices.GetService<WebSocketNotificationsRelay>());
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

            catch (JsonException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    await Console.Error.WriteLineAsync(ex.Message);

                await Console.Error.WriteLineAsync("\nCluster cannot start. Good Bye!");
            }

            catch (IOException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    await Console.Error.WriteLineAsync(ex.Message);

                await Console.Error.WriteLineAsync("\nCluster cannot start. Good Bye!");
            }

            catch (AggregateException ex)
            {
                if (ex.InnerExceptions.First() is not PoolStartupAbortException)
                    Console.Error.WriteLine(ex);

                await Console.Error.WriteLineAsync("Cluster cannot start. Good Bye!");
            }

            catch (OperationCanceledException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    await Console.Error.WriteLineAsync(ex.Message);

                await Console.Error.WriteLineAsync("\nCluster cannot start. Good Bye!");
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
=======
    AppDomain.CurrentDomain.UnhandledException += CybercoreBackgroudService.OnAppDomainUnhandledException;

    if (!CybercoreBackgroudService.ParseCommandLine(args, out var configFile))
        return;

    CybercoreBackgroudService.Logo();
>>>>>>> 7b313535651f7481710b2e6f47109d11bf1ebdb0

    var clusterConfig = CybercoreBackgroudService.ReadConfig(configFile);

    if (CybercoreBackgroudService.DumpConfiguration.HasValue())
    {
        CybercoreBackgroudService.DumpParsedConfig(clusterConfig);
        return;
    }

    CybercoreBackgroudService.ValidateConfig();
    CybercoreBackgroudService.ConfigureLogging();
    CybercoreBackgroudService.LogRuntimeInfo();
    CybercoreBackgroudService.ValidateRuntimeEnvironment();

    var logger = LogManager.GetLogger("Program");

    var hostBuilder = Host.CreateDefaultBuilder(args);

    hostBuilder
        .UseServiceProviderFactory(new AutofacServiceProviderFactory())
        .ConfigureContainer((Action<ContainerBuilder>)CybercoreBackgroudService.ConfigureAutofac)
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

            CybercoreBackgroudService.ConfigureBackgroundServices(services);

            services.AddHostedService<CybercoreBackgroudService>();
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
                    services.Configure<IpRateLimitOptions>(CybercoreBackgroudService.ConfigureIpRateLimitOptions);
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

                CybercoreBackgroudService.UseIpWhiteList(app, true, new[] { "/api/admin" }, clusterConfig.Api?.AdminIpWhitelist);
                CybercoreBackgroudService.UseIpWhiteList(app, true, new[] { "/metrics" }, clusterConfig.Api?.MetricsIpWhitelist);

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

    await hostBuilder
        .UseConsoleLifetime()
        .Build().RunAsync();
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