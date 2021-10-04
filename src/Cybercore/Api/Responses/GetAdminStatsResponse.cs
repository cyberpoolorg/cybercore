using System;

namespace Cybercore.Api.Responses
{
    public class AdminGcStats
    {
        public int GcGen0 { get; set; }
        public int GcGen1 { get; set; }
        public int GcGen2 { get; set; }
        public string MemAllocated { get; set; }
        public double MaxFullGcDuration { get; set; } = 0;
    }
}