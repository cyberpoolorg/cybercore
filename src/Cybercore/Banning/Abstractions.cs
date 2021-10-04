using System;
using System.Net;

namespace Cybercore.Banning
{
    public interface IBanManager
    {
        bool IsBanned(IPAddress address);
        void Ban(IPAddress address, TimeSpan duration);
    }
}