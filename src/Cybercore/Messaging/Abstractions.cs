using System;
using System.Reactive.Concurrency;

namespace Cybercore.Messaging
{
    public interface IMessageBus
    {
        void RegisterScheduler<T>(IScheduler scheduler, string contract = null);
        IObservable<T> Listen<T>(string contract = null);
        IObservable<T> ListenIncludeLatest<T>(string contract = null);
        bool IsRegistered(Type type, string contract = null);
        IDisposable RegisterMessageSource<T>(IObservable<T> source, string contract = null);
        void SendMessage<T>(T message, string contract = null);
    }
}