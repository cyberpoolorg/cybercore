using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Cybercore.Extensions;
using Contract = Cybercore.Contracts.Contract;

namespace Cybercore.Crypto
{
    public class MerkleTree
    {
        public MerkleTree(IEnumerable<byte[]> hashList)
        {
            Steps = CalculateSteps(hashList);
        }

        public IList<byte[]> Steps { get; }

        public List<string> Branches
        {
            get { return Steps.Select(step => step.ToHexString()).ToList(); }
        }

        private IList<byte[]> CalculateSteps(IEnumerable<byte[]> hashList)
        {
            Contract.RequiresNonNull(hashList, nameof(hashList));

            var steps = new List<byte[]>();

            var L = new List<byte[]> { null };
            L.AddRange(hashList);

            var startL = 2;
            var Ll = L.Count;

            if (Ll > 1)
                while (true)
                {
                    if (Ll == 1)
                        break;

                    steps.Add(L[1]);

                    if (Ll % 2 == 1)
                        L.Add(L[^1]);

                    var Ld = new List<byte[]>();

                    for (var i = startL; i < Ll; i += 2)
                        Ld.Add(MerkleJoin(L[i], L[i + 1]));

                    L = new List<byte[]> { null };
                    L.AddRange(Ld);
                    Ll = L.Count;
                }

            return steps;
        }

        private byte[] MerkleJoin(byte[] hash1, byte[] hash2)
        {
            var joined = hash1.Concat(hash2);
            var dHashed = DoubleDigest(joined).ToArray();
            return dHashed;
        }

        public byte[] WithFirst(byte[] first)
        {
            Contract.RequiresNonNull(first, nameof(first));

            foreach (var step in Steps)
                first = DoubleDigest(first.Concat(step)).ToArray();

            return first;
        }

        private static byte[] DoubleDigest(byte[] input)
        {
            using (var hash = SHA256.Create())
            {
                var first = hash.ComputeHash(input, 0, input.Length);
                return hash.ComputeHash(first);
            }
        }

        private static IEnumerable<byte> DoubleDigest(IEnumerable<byte> input)
        {
            return DoubleDigest(input.ToArray());
        }
    }
}