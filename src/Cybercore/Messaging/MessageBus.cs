// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MS-PL license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Cybercore.Util;
using NLog;

namespace Cybercore.Messaging
{
    public class MessageBus : IMessageBus
    {
        private readonly Dictionary<Tuple<Type, string>, NotAWeakReference> messageBus =
            new();

        private readonly IDictionary<Tuple<Type, string>, IScheduler> schedulerMappings =
            new Dictionary<Tuple<Type, string>, IScheduler>();

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public static IMessageBus Current { get; set; } = new MessageBus();

        public void RegisterScheduler<T>(IScheduler scheduler, string contract = null)
        {
            schedulerMappings[new Tuple<Type, string>(typeof(T), contract)] = scheduler;
        }

        public IObservable<T> Listen<T>(string contract = null)
        {
            logger.Debug("Listening to {0}:{1}", typeof(T), contract);

            return setupSubjectIfNecessary<T>(contract).Skip(1);
        }

        public IObservable<T> ListenIncludeLatest<T>(string contract = null)
        {
            logger.Debug("Listening to {0}:{1}", typeof(T), contract);

            return setupSubjectIfNecessary<T>(contract);
        }

        public bool IsRegistered(Type type, string contract = null)
        {
            var ret = false;
            withMessageBus(type, contract, (mb, tuple) => { ret = mb.ContainsKey(tuple) && mb[tuple].IsAlive; });

            return ret;
        }

        public IDisposable RegisterMessageSource<T>(
            IObservable<T> source,
            string contract = null)
        {
            return source.Subscribe(setupSubjectIfNecessary<T>(contract));
        }

        public void SendMessage<T>(T message, string contract = null)
        {
            setupSubjectIfNecessary<T>(contract).OnNext(message);
        }

        private ISubject<T> setupSubjectIfNecessary<T>(string contract)
        {
            ISubject<T> ret = null;

            withMessageBus(typeof(T), contract, (mb, tuple) =>
            {
                if (mb.TryGetValue(tuple, out var subjRef) && subjRef.IsAlive)
                {
                    ret = (ISubject<T>)subjRef.Target;
                    return;
                }

                ret = new ScheduledSubject<T>(getScheduler(tuple), null, new BehaviorSubject<T>(default(T)));
                mb[tuple] = new NotAWeakReference(ret);
            });

            return ret;
        }

        private void withMessageBus(
            Type type, string contract,
            Action<Dictionary<Tuple<Type, string>, NotAWeakReference>,
                Tuple<Type, string>> block)
        {
            lock (messageBus)
            {
                var tuple = new Tuple<Type, string>(type, contract);
                block(messageBus, tuple);
                if (messageBus.ContainsKey(tuple) && !messageBus[tuple].IsAlive)
                    messageBus.Remove(tuple);
            }
        }

        private IScheduler getScheduler(Tuple<Type, string> tuple)
        {
            schedulerMappings.TryGetValue(tuple, out var scheduler);
            return scheduler ?? CurrentThreadScheduler.Instance;
        }
    }

    internal class NotAWeakReference
    {
        public NotAWeakReference(object target)
        {
            Target = target;
        }

        public object Target { get; }
        public bool IsAlive => true;
    }
}
// vim: tw=120 ts=4 sw=4 et :