using System;
using System.Linq;
using Cybercore.Contracts;
using Cybercore.Extensions;
using Cybercore.Native;

namespace Cybercore.Crypto.Hashing.Algorithms
{
    public unsafe class Keccak : IHashAlgorithm
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.RequiresNonNull(extra, nameof(extra));
            Contract.Requires<ArgumentException>(extra.Length > 0, $"{nameof(extra)} must not be empty");
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var nTime = (ulong)extra[0];
            var nTimeHex = nTime.ToString("X").HexToByteArray();

            Span<byte> dataEx = stackalloc byte[data.Length + nTimeHex.Length];
            data.CopyTo(dataEx);

            if (nTimeHex.Length > 0)
            {
                var dest = dataEx[data.Length..];
                nTimeHex.CopyTo(dest);
            }

            fixed (byte* input = dataEx)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.keccak(input, output, (uint)data.Length);
                }
            }
        }
    }
}