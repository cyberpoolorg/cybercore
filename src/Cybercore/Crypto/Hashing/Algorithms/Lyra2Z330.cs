using System;
using Cybercore.Contracts;
using Cybercore.Native;

namespace Cybercore.Crypto.Hashing.Algorithms
{
    public unsafe class Lyra2Z330 : IHashAlgorithm
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} must be exactly 80 bytes long");
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibLyrahash.lyra2z330(input, output, (uint)data.Length);
                }
            }
        }
    }
}
