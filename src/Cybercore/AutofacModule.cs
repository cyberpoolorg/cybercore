using Autofac;
using System.Linq;
using System.Reflection;
using Cybercore.Api;
using Cybercore.Banning;
using Cybercore.Blockchain.Bitcoin;
using Cybercore.Blockchain.Cryptonote;
using Cybercore.Blockchain.Equihash;
using Cybercore.Blockchain.Ethereum;
using Cybercore.Blockchain.Ergo;
using Cybercore.Configuration;
using Cybercore.Crypto;
using Cybercore.Crypto.Hashing.Equihash;
using Cybercore.Messaging;
using Cybercore.Mining;
using Cybercore.Nicehash;
using Cybercore.Notifications;
using Cybercore.Payments;
using Cybercore.Payments.PaymentSchemes;
using Cybercore.Pushover;
using Cybercore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Module = Autofac.Module;
using Microsoft.AspNetCore.Mvc;

namespace Cybercore
{
    public class AutofacModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            var thisAssembly = typeof(AutofacModule).GetTypeInfo().Assembly;

            builder.RegisterInstance(new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessDictionaryKeys = false
                    }
                }
            });

            builder.RegisterType<MessageBus>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<StandardClock>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<IntegratedBanManager>()
                .Keyed<IBanManager>(BanManagerKind.Integrated)
                .SingleInstance();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetCustomAttributes<CoinFamilyAttribute>().Any() && t.GetInterfaces()
                    .Any(i =>
                        i.IsAssignableFrom(typeof(IMiningPool)) ||
                        i.IsAssignableFrom(typeof(IPayoutHandler)) ||
                        i.IsAssignableFrom(typeof(IPayoutScheme))))
                .WithMetadataFrom<CoinFamilyAttribute>()
                .AsImplementedInterfaces();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetInterfaces().Any(i => i.IsAssignableFrom(typeof(IHashAlgorithm))))
                .PropertiesAutowired()
                .AsSelf();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.IsAssignableTo<EquihashSolver>())
                .PropertiesAutowired()
                .AsSelf();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.IsAssignableTo<ControllerBase>())
                .PropertiesAutowired()
                .AsSelf();

            builder.RegisterType<WebSocketNotificationsRelay>()
                .PropertiesAutowired()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<NicehashService>()
                .SingleInstance();

            builder.RegisterType<PushoverClient>()
                .SingleInstance();

            builder.RegisterType<PayoutManager>()
                .SingleInstance();

            builder.RegisterType<ShareRecorder>()
                .SingleInstance();

            builder.RegisterType<ShareReceiver>()
                .SingleInstance();

            builder.RegisterType<BtStreamReceiver>()
                .SingleInstance();

            builder.RegisterType<ShareRelay>()
                .SingleInstance();

            builder.RegisterType<StatsRecorder>()
                .SingleInstance();

            builder.RegisterType<NotificationService>()
                .SingleInstance();

            builder.RegisterType<MetricsPublisher>()
                .SingleInstance();

            builder.RegisterType<PPLNSPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
                .SingleInstance();

            builder.RegisterType<SOLOPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.SOLO)
                .SingleInstance();

            builder.RegisterType<PROPPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PROP)
                .SingleInstance();

            builder.RegisterType<BitcoinJobManager>();
            builder.RegisterType<CryptonoteJobManager>();
            builder.RegisterType<EquihashJobManager>();
            builder.RegisterType<ErgoJobManager>();
            builder.RegisterType<EthereumJobManager>();
            builder.RegisterType<EthereumJobManager>();
            base.Load(builder);
        }
    }
}