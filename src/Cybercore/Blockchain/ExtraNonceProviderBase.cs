using System;
using System.Security.Cryptography;
using Cybercore.Util;
using NLog;

namespace Cybercore.Blockchain
{
    public class ExtraNonceProviderBase : IExtraNonceProvider
    {
        public ExtraNonceProviderBase(string poolId, int extranonceBytes, byte? instanceId)
        {
            this.logger = LogUtil.GetPoolScopedLogger(this.GetType(), poolId);
            this.extranonceBytes = extranonceBytes;
            idShift = (extranonceBytes * 8) - IdBits;
            nonceMax = (1UL << idShift) - 1;
            idMax = (1U << IdBits) - 1;
            stringFormat = "x" + extranonceBytes * 2;
            var mask = (1L << IdBits) - 1;
            if (instanceId.HasValue)
            {
                id = instanceId.Value;

                if (id > idMax)
                    logger.ThrowLogPoolStartupException($"Provided instance id to large to fit into {IdBits} bits (limit {idMax})");
            }
            else
            {
                using (var rng = RandomNumberGenerator.Create())
                {
                    var bytes = new byte[1];
                    rng.GetNonZeroBytes(bytes);
                    id = bytes[0];
                }
            }
            id = (byte)(id & mask);
            counter = 0;
            logger.Info(() => $"ExtraNonceProvider using {IdBits} bits for instance id, {extranonceBytes * 8 - IdBits} bits for {nonceMax} values, instance id = 0x{id:X}");
        }

        private readonly ILogger logger;
        private const int IdBits = 4;
        private readonly object counterLock = new();
        protected ulong counter;
        protected byte id;
        protected readonly int extranonceBytes;
        protected readonly int idShift;
        protected readonly uint idMax;
        protected readonly ulong nonceMax;
        protected readonly string stringFormat;

        #region IExtraNonceProvider

        public int ByteSize => extranonceBytes;
        public string Next()
        {
            ulong value;
            lock (counterLock)
            {
                counter++;

                if (counter > nonceMax)
                {
                    logger.Warn(() => $"ExtraNonceProvider range exhausted! Rolling over to 0.");

                    counter = 0;
                }
                value = ((ulong)id << idShift) | counter;
            }
            return value.ToString(stringFormat);
        }
        #endregion // IExtraNonceProvider
    }
}