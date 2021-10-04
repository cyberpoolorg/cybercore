using System;
using System.Collections.Generic;
using System.Text;

namespace Cybercore.Time
{
    public class StandardClock : IMasterClock
    {
        public DateTime Now => DateTime.UtcNow;
    }
}