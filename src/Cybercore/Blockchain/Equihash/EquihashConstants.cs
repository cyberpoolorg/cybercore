using System.Globalization;
using NBitcoin.Zcash;

namespace Cybercore.Blockchain.Equihash
{
    public class EquihashConstants
    {
        public const int TargetPaddingLength = 32;

        public static readonly System.Numerics.BigInteger ZCashDiff1b =
            System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber);
    }

    public enum ZOperationStatus
    {
        Queued,
        Executing,
        Success,
        Cancelled,
        Failed
    }

    public static class EquihashCommands
    {
        public const string ZGetBalance = "z_getbalance";
        public const string ZGetTotalBalance = "z_gettotalbalance";
        public const string ZGetListAddresses = "z_listaddresses";
        public const string ZValidateAddress = "z_validateaddress";
        public const string ZShieldCoinbase = "z_shieldcoinbase";
        public const string ZSendMany = "z_sendmany";
        public const string ZGetOperationStatus = "z_getoperationstatus";
        public const string ZGetOperationResult = "z_getoperationresult";
    }
}