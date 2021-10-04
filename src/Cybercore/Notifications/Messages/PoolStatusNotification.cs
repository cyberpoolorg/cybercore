using Cybercore.Mining;
using Cybercore.Persistence.Model;

namespace Cybercore.Notifications.Messages
{
    public enum PoolStatus
    {
        Online,
        Offline
    }

    public record PoolStatusNotification
    {
        public IMiningPool Pool { get; set; }
        public PoolStatus Status { get; set; }
    }
}