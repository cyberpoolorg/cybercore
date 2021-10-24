using System;
using Cybercore.Configuration;
using Cybercore.Time;
using Cybercore.VarDiff;

namespace Cybercore.Mining
{
    public class ShareStats
    {
        public int ValidShares { get; set; }
        public int InvalidShares { get; set; }
    }

    public class WorkerContextBase
    {
        private double? pendingDifficulty;
        public ShareStats Stats { get; set; }
        public VarDiffContext VarDiff { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsAuthorized { get; set; } = false;
        public bool IsSubscribed { get; set; }
        public double Difficulty { get; set; }
        public double? PreviousDifficulty { get; set; }
        public string UserAgent { get; set; }
        public bool HasPendingDifficulty => pendingDifficulty.HasValue;

        public void Init(PoolConfig poolConfig, double difficulty, VarDiffConfig varDiffConfig, IMasterClock clock)
        {
            Difficulty = difficulty;
            LastActivity = clock.Now;
            Stats = new ShareStats();

            if (varDiffConfig != null)
                VarDiff = new VarDiffContext { Config = varDiffConfig };
        }

        public void EnqueueNewDifficulty(double difficulty)
        {
            pendingDifficulty = difficulty;
        }

        public bool ApplyPendingDifficulty()
        {
            if (pendingDifficulty.HasValue)
            {
                SetDifficulty(pendingDifficulty.Value);
                pendingDifficulty = null;

                return true;
            }

            return false;
        }

        public void SetDifficulty(double difficulty)
        {
            PreviousDifficulty = Difficulty;
            Difficulty = difficulty;
        }

        public void Dispose()
        {
        }
    }
}