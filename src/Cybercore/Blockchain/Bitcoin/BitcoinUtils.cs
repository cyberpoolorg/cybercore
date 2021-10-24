using System;
using System.Diagnostics;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using static Cybercore.Blockchain.Bitcoin.CashAddr;
using static Cybercore.Blockchain.Bitcoin.BchAddr;

namespace Cybercore.Blockchain.Bitcoin
{
    public static class BitcoinUtils
    {
        public static IDestination AddressToDestination(string address, Network expectedNetwork)
        {
            var decoded = Encoders.Base58Check.DecodeData(address);
            var networkVersionBytes = expectedNetwork.GetVersionBytes(Base58Type.PUBKEY_ADDRESS, true);
            decoded = decoded.Skip(networkVersionBytes.Length).ToArray();
            var result = new KeyId(decoded);

            return result;
        }

        public static IDestination BechSegwitAddressToDestination(string address, Network expectedNetwork, string bechPrefix)
        {
            var encoder = Encoders.Bech32(bechPrefix);
            var decoded = encoder.Decode(address, out var witVersion);
            var result = new WitKeyId(decoded);

            Debug.Assert(result.GetAddress(expectedNetwork).ToString() == address);
            return result;
        }

        public static IDestination CashAddrToDestination(string address, Network expectedNetwork, bool fP2Sh = false)
        {
            BchAddr.BchAddrData bchAddr = BchAddr.DecodeCashAddressWithPrefix(address);
            if (fP2Sh)
                return new ScriptId(bchAddr.Hash);
            else
                return new KeyId(bchAddr.Hash);
        }
    }
}