using System;

namespace Cybercore.Time
{
    public interface IMasterClock
    {
        DateTime Now { get; }
    }
}