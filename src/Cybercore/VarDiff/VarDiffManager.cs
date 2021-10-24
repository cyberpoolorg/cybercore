using System;
using Cybercore.Configuration;
using Cybercore.Time;
using Cybercore.Util;
using Contract = Cybercore.Contracts.Contract;

namespace Cybercore.VarDiff
{
    public class VarDiffManager
    {
        public VarDiffManager(VarDiffConfig varDiffOptions, IMasterClock clock)
        {
            options = varDiffOptions;
            this.clock = clock;
            bufferSize = 10;
            var variance = varDiffOptions.TargetTime * (varDiffOptions.VariancePercent / 100.0);
            tMin = varDiffOptions.TargetTime - variance;
            tMax = varDiffOptions.TargetTime + variance;
        }

        private readonly int bufferSize;
        private readonly VarDiffConfig options;
        private readonly double tMax;
        private readonly double tMin;
        private readonly IMasterClock clock;

        public double? Update(VarDiffContext ctx, double difficulty, bool isIdleUpdate)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            lock (ctx)
            {
                var ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000.0;

                if (!ctx.LastTs.HasValue)
                {
                    ctx.LastRtc = ts;
                    ctx.LastTs = ts;
                    ctx.TimeBuffer = new CircularDoubleBuffer(bufferSize);
                    return null;
                }

                var minDiff = options.MinDiff;
                var maxDiff = options.MaxDiff ?? Math.Max(minDiff, double.MaxValue);
                var sinceLast = ts - ctx.LastTs.Value;
                var timeTotal = ctx.TimeBuffer.Sum();
                var timeCount = ctx.TimeBuffer.Size;
                var avg = (timeTotal + sinceLast) / (timeCount + 1);

                if (!isIdleUpdate)
                {
                    ctx.TimeBuffer.PushBack(sinceLast);
                    ctx.LastTs = ts;
                }

                if (ts - ctx.LastRtc < options.RetargetTime || avg >= tMin && avg <= tMax)
                    return null;

                var newDiff = difficulty * options.TargetTime / avg;

                if (options.MaxDelta.HasValue && options.MaxDelta > 0)
                {
                    var delta = Math.Abs(newDiff - difficulty);

                    if (delta > options.MaxDelta)
                    {
                        if (newDiff > difficulty)
                            newDiff -= delta - options.MaxDelta.Value;
                        else if (newDiff < difficulty)
                            newDiff += delta - options.MaxDelta.Value;
                    }
                }

                if (newDiff < minDiff)
                    newDiff = minDiff;
                if (newDiff > maxDiff)
                    newDiff = maxDiff;

                if (newDiff < difficulty || newDiff > difficulty)
                {
                    ctx.LastRtc = ts;
                    ctx.LastUpdate = clock.Now;
                    ctx.TimeBuffer = new CircularDoubleBuffer(bufferSize);

                    return newDiff;
                }
            }

            return null;
        }
    }
}