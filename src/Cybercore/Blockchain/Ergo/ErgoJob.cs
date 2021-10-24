using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Cybercore.Contracts;
using Cybercore.Crypto;
using Cybercore.Crypto.Hashing.Algorithms;
using Cybercore.Extensions;
using Cybercore.Stratum;
using NBitcoin;

namespace Cybercore.Blockchain.Ergo
{
    public class ErgoJob
    {
        public WorkMessage BlockTemplate { get; private set; }
        public double Difficulty { get; private set; }
        public uint Height => BlockTemplate.Height;
        public string JobId { get; protected set; }

        private object[] jobParams;
        private BigInteger n;
        private readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IHashAlgorithm hasher = new Blake2b();
        private int extraNonceSize;

        private static readonly uint nBase = (uint)Math.Pow(2, 26);
        private const uint IncreaseStart = 600 * 1024;
        private const uint IncreasePeriodForN = 50 * 1024;
        private const uint NIncreasementHeightMax = 9216000;

        public static uint CalcN(uint height)
        {
            height = Math.Min(NIncreasementHeightMax, height);

            switch (height)
            {
                case < IncreaseStart:
                    return nBase;
                case >= NIncreasementHeightMax:
                    return 2147387550;
            }

            var step = nBase;
            var iterationsNumber = (height - IncreaseStart) / IncreasePeriodForN + 1;

            for (var i = 0; i < iterationsNumber; i++)
                step = step / 100 * 105;

            return step;
        }

        protected bool RegisterSubmit(string nTime, string nonce)
        {
            var key = new StringBuilder()
                .Append(nTime)
                .Append(nonce)
                .ToString();

            return submissions.TryAdd(key, true);
        }

        protected virtual byte[] SerializeCoinbase(string msg, string nonce)
        {
            using (var stream = new MemoryStream())
            {
                stream.Write(msg.HexToByteArray());
                stream.Write(nonce.HexToByteArray());

                return stream.ToArray();
            }
        }

        private BigInteger[] GenIndexes(byte[] seed)
        {
            Span<byte> hash = stackalloc byte[32];
            hasher.Digest(seed, hash);

            Span<byte> extendedHash = stackalloc byte[64];
            hash.CopyTo(extendedHash);
            hash.CopyTo(extendedHash.Slice(32, 32));

            var result = new BigInteger[32];

            for (var i = 0; i < 32; i++)
            {
                var x = BitConverter.ToUInt32(extendedHash.Slice(i, 4)).ToBigEndian();
                result[i] = x % n;
            }

            return result;
        }

        protected virtual Share ProcessShareInternal(StratumConnection worker, string nonce)
        {
            var context = worker.ContextAs<ErgoWorkerContext>();

            var coinbase = SerializeCoinbase(BlockTemplate.Msg, nonce);
            Span<byte> hash = stackalloc byte[32];
            hasher.Digest(coinbase, hash);

            var slice = hash.Slice(24, 8);
            var tmp2 = new BigInteger(slice, true, true) % n;
            var i = tmp2.ToByteArray(false, true).PadFront(0, 4);

            var h = new BigInteger(Height).ToByteArray(true, true).PadFront(0, 4);
            var ihM = i.Concat(h).Concat(ErgoConstants.M).ToArray();
            hasher.Digest(ihM, hash);
            var e = hash[1..].ToArray();

            var eCoinbase = e.Concat(coinbase).ToArray();
            var jTmp = GenIndexes(eCoinbase);
            var j = jTmp.Select(x => x.ToByteArray(true, true).PadFront(0, 4)).ToArray();

            var f = j.Select(x =>
            {
                var buf2 = x.Concat(h).Concat(ErgoConstants.M).ToArray();

                Span<byte> tmp = stackalloc byte[32];
                hasher.Digest(buf2, tmp);

                return new BigInteger(tmp[1..], true, true);
            }).Aggregate((x, y) => x + y);

            var fBytes = f.ToByteArray(true, true).PadFront(0, 32);
            hasher.Digest(fBytes, hash);
            var fh = new BigInteger(hash, true, true);
            var fhTarget = new Target(fh);

            var stratumDifficulty = context.Difficulty;
            var ratio = fhTarget.Difficulty / stratumDifficulty;

            var isBlockCandidate = fh < BlockTemplate.B;

            if (!isBlockCandidate && ratio < 0.99)
            {
                if (context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = fhTarget.Difficulty / context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({fhTarget.Difficulty})");

                    stratumDifficulty = context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({fhTarget.Difficulty})");
            }

            var result = new Share
            {
                BlockHeight = Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty / ErgoConstants.ShareMultiplier
            };

            if (isBlockCandidate)
            {
                result.IsBlockCandidate = true;
            }

            return result;
        }

        public object[] GetJobParams(bool isNew)
        {
            jobParams[^1] = isNew;
            return jobParams;
        }

        public virtual Share ProcessShare(StratumConnection worker, string extraNonce2, string nTime, string nonce)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

            var context = worker.ContextAs<ErgoWorkerContext>();

            if (nonce.Length != context.ExtraNonce1.Length + extraNonceSize * 2)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            if (!nonce.StartsWith(context.ExtraNonce1))
                throw new StratumException(StratumError.Other, $"incorrect extraNonce2 in nonce (expected {context.ExtraNonce1}, got {nonce.Substring(0, Math.Min(nonce.Length, context.ExtraNonce1.Length))})");

            if (nTime == "undefined")
                nTime = string.Empty;

            if (!RegisterSubmit(nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, $"duplicate share");

            return ProcessShareInternal(worker, nonce);
        }

        public void Init(WorkMessage blockTemplate, int blockVersion, int extraNonceSize, string jobId)
        {
            this.extraNonceSize = extraNonceSize;

            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = new Target(BlockTemplate.B).Difficulty;
            n = CalcN(Height);

            jobParams = new object[]
            {
                JobId,
                Height,
                BlockTemplate.Msg,
                string.Empty,
                string.Empty,
                blockVersion,
                null,
                string.Empty,
                false
            };
        }
    }
}
