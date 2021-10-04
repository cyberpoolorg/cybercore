using System;
using System.Collections.Generic;
using System.Text;

namespace Cybercore.Notifications.Messages
{
    public enum TelemetryCategory
    {
        Share = 1,
        BtStream,
        RpcRequest,
        Connections,
        Hash,
    }

    public record TelemetryEvent
    {
        public TelemetryEvent(string groupId, TelemetryCategory category, TimeSpan elapsed, bool? success = null, string error = null)
        {
            GroupId = groupId;
            Category = category;
            Elapsed = elapsed;
            Success = success;
            Error = error;
        }

        public TelemetryEvent(string groupId, TelemetryCategory category, string info, TimeSpan elapsed, bool? success = null, string error = null) :
            this(groupId, category, elapsed, success, error)
        {
            Info = info;
        }

        public string GroupId { get; }
        public TelemetryCategory Category { get; }
        public string Info { get; }
        public TimeSpan Elapsed { get; }
        public bool? Success { get; }
        public string Error { get; }
        public int Total { get; set; }
    }
}