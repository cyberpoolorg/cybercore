using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Ethereum;
using Cybercore.Contracts;
using Cybercore.Extensions;
using Cybercore.Messaging;
using Cybercore.Native;
using Cybercore.Notifications.Messages;
using NLog;

namespace Cybercore.Crypto.Hashing.Ethash
{
    public class Dag : IDisposable
    {
        public Dag(ulong epoch)
        {
            Epoch = epoch;
        }

        public ulong Epoch { get; set; }
        private IntPtr handle = IntPtr.Zero;
        private static readonly Semaphore sem = new(1, 1);
        internal static IMessageBus messageBus;
        public DateTime LastUsed { get; set; }

        public static unsafe string GetDefaultDagDirectory()
        {
            var chars = new byte[512];

            fixed (byte* data = chars)
            {
                if (LibEthash.ethash_get_default_dirname(data, chars.Length))
                {
                    int length;
                    for (length = 0; length < chars.Length; length++)
                    {
                        if (data[length] == 0)
                            break;
                    }

                    return Encoding.UTF8.GetString(data, length);
                }
            }
            return null;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                LibEthash.ethash_full_delete(handle);
                handle = IntPtr.Zero;
            }
        }

        public async ValueTask GenerateAsync(string dagDir, ILogger logger, CancellationToken ct)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir), $"{nameof(dagDir)} must not be empty");

            if (handle == IntPtr.Zero)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        sem.WaitOne();

                        if (handle != IntPtr.Zero)
                            return;

                        logger.Info(() => $"Generating DAG for epoch {Epoch}");

                        var started = DateTime.Now;
                        var block = Epoch * EthereumConstants.EpochLength;
                        var light = LibEthash.ethash_light_new(block);

                        try
                        {
                            handle = LibEthash.ethash_full_new(dagDir, light, progress =>
                            {
                                logger.Info(() => $"Generating DAG for epoch {Epoch}: {progress}%");
                                return !ct.IsCancellationRequested ? 0 : 1;
                            });

                            if (handle == IntPtr.Zero)
                                throw new OutOfMemoryException("ethash_full_new IO or memory error");

                            logger.Info(() => $"Done generating DAG for epoch {Epoch} after {DateTime.Now - started}");
                        }

                        finally
                        {
                            if (light != IntPtr.Zero)
                                LibEthash.ethash_light_delete(light);
                        }
                    }

                    finally
                    {
                        sem.Release();
                    }
                }, ct);
            }
        }

        public unsafe bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
        {
            Contract.RequiresNonNull(hash, nameof(hash));
            logger.LogInvoke();
            var sw = Stopwatch.StartNew();
            mixDigest = null;
            result = null;
            var value = new LibEthash.ethash_return_value();

            fixed (byte* input = hash)
            {
                LibEthash.ethash_full_compute(handle, input, nonce, ref value);
            }

            if (value.success)
            {
                mixDigest = value.mix_hash.value;
                result = value.result.value;
            }

            messageBus?.SendTelemetry("Ethash", TelemetryCategory.Hash, sw.Elapsed, value.success);
            return value.success;
        }
    }
}