using Autofac;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cybercore.Crypto.Hashing.Algorithms;
using Newtonsoft.Json.Linq;

namespace Cybercore.Crypto
{
    public static class HashAlgorithmFactory
    {
        private static readonly ConcurrentDictionary<string, IHashAlgorithm> cache = new();

        public static IHashAlgorithm GetHash(IComponentContext ctx, JObject definition)
        {
            var hash = definition["hash"]?.Value<string>().ToLower();

            if (string.IsNullOrEmpty(hash))
                throw new NotSupportedException("$Invalid or empty hash value {hash}");

            var args = definition["args"]?
                .Select(token => token.Type == JTokenType.Object ? GetHash(ctx, (JObject)token) : token.Value<object>())
                .ToArray();

            return InstantiateHash(ctx, hash, args);
        }

        private static IHashAlgorithm InstantiateHash(IComponentContext ctx, string name, object[] args)
        {
            if (name == "reverse")
                name = nameof(DigestReverser);

            var hasArgs = args != null && args.Length > 0;
            if (!hasArgs && cache.TryGetValue(name, out var result))
                return result;

            var hashClass = (typeof(Sha256D).Namespace + "." + name).ToLower();
            var hashType = typeof(Sha256D).Assembly.GetType(hashClass, true, true);

            if (hasArgs)
                result = (IHashAlgorithm)ctx.Resolve(hashType, args.Select((x, i) => new PositionalParameter(i, x)));
            else
            {
                result = (IHashAlgorithm)ctx.Resolve(hashType);
                cache.TryAdd(name, result);
            }

            return result;
        }
    }
}