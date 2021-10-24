using System;
using Cybercore.Contracts;
using Cybercore.Native;

namespace Cybercore.Crypto.Hashing.Algorithms
{
    public unsafe class Phi5 : IHashAlgorithm
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.phi5(input, output, (uint)data.Length);
                }
            }
        }
    }
}
