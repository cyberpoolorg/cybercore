using System;
using System.Globalization;
using System.Numerics;

namespace Cybercore.Blockchain.Bitcoin
{
    public enum BitcoinAddressType
    {
        Legacy,
        BechSegwit,
        CashAddr,
    }

    public enum BitcoinTransactionCategory
    {
        Send = 1,
        Receive,
        Generate,
        Immature,
        Orphan
    }

    public class BitcoinConstants
    {
        public const int ExtranoncePlaceHolderLength = 8;
        public const decimal SatoshisPerBitcoin = 100000000;
        public static double Pow2x32 = Math.Pow(2, 32);
        public static double Pow2x42 = Math.Pow(2, 42);
        public static readonly BigInteger Diff1 = BigInteger.Parse("00ffff0000000000000000000000000000000000000000000000000000", NumberStyles.HexNumber);
        public const int CoinbaseMinConfimations = 101;
        public const uint VersionRollingPoolMask = 0x1fffe000;
        public const string ZmqPublisherTopicBlockHash = "hashblock";
        public const string ZmqPublisherTopicTxHash = "hashtx";
        public const string ZmqPublisherTopicBlockRaw = "rawblock";
        public const string ZmqPublisherTopicTxRaw = "rawtx";
    }

    public enum BitcoinRPCErrorCode
    {
        RPC_INVALID_REQUEST = -32600,
        RPC_METHOD_NOT_FOUND = -32601,
        RPC_INVALID_PARAMS = -32602,
        RPC_INTERNAL_ERROR = -32603,
        RPC_PARSE_ERROR = -32700,

        RPC_MISC_ERROR = -1, //!< std::exception thrown in command handling
        RPC_FORBIDDEN_BY_SAFE_MODE = -2, //!< Server is in safe mode, and command is not allowed in safe mode
        RPC_TYPE_ERROR = -3, //!< Unexpected type was passed as parameter
        RPC_INVALID_ADDRESS_OR_KEY = -5, //!< Invalid address or key
        RPC_OUT_OF_MEMORY = -7, //!< Ran out of memory during operation
        RPC_INVALID_PARAMETER = -8, //!< Invalid, missing or duplicate parameter
        RPC_DATABASE_ERROR = -20, //!< Database error
        RPC_DESERIALIZATION_ERROR = -22, //!< Error parsing or validating structure in raw format
        RPC_VERIFY_ERROR = -25, //!< General error during transaction or block submission
        RPC_VERIFY_REJECTED = -26, //!< Transaction or block was rejected by network rules
        RPC_VERIFY_ALREADY_IN_CHAIN = -27, //!< Transaction already in chain
        RPC_IN_WARMUP = -28, //!< Client still warming up
        RPC_METHOD_DEPRECATED = -32, //!< RPC method is deprecated
        RPC_TRANSACTION_ERROR = RPC_VERIFY_ERROR,
        RPC_TRANSACTION_REJECTED = RPC_VERIFY_REJECTED,
        RPC_TRANSACTION_ALREADY_IN_CHAIN = RPC_VERIFY_ALREADY_IN_CHAIN,
        RPC_CLIENT_NOT_CONNECTED = -9, //!< Bitcoin is not connected
        RPC_CLIENT_IN_INITIAL_DOWNLOAD = -10, //!< Still downloading initial blocks
        RPC_CLIENT_NODE_ALREADY_ADDED = -23, //!< Node is already added
        RPC_CLIENT_NODE_NOT_ADDED = -24, //!< Node has not been added before
        RPC_CLIENT_NODE_NOT_CONNECTED = -29, //!< Node to disconnect not found in connected nodes
        RPC_CLIENT_INVALID_IP_OR_SUBNET = -30, //!< Invalid IP/Subnet
        RPC_CLIENT_P2P_DISABLED = -31, //!< No valid connection manager instance found
        RPC_WALLET_ERROR = -4, //!< Unspecified problem with wallet (key not found etc.)
        RPC_WALLET_INSUFFICIENT_FUNDS = -6, //!< Not enough funds in wallet or account
        RPC_WALLET_INVALID_ACCOUNT_NAME = -11, //!< Invalid account name
        RPC_WALLET_KEYPOOL_RAN_OUT = -12, //!< Keypool ran out, call keypoolrefill first
        RPC_WALLET_UNLOCK_NEEDED = -13, //!< Enter the wallet passphrase with walletpassphrase first
        RPC_WALLET_PASSPHRASE_INCORRECT = -14, //!< The wallet passphrase entered was incorrect
        RPC_WALLET_WRONG_ENC_STATE = -15, //!< Command given in wrong wallet encryption state (encrypting an encrypted wallet etc.)
        RPC_WALLET_ENCRYPTION_FAILED = -16, //!< Failed to encrypt the wallet
        RPC_WALLET_ALREADY_UNLOCKED = -17, //!< Wallet is already unlocked
        RPC_WALLET_NOT_FOUND = -18, //!< Invalid wallet specified
        RPC_WALLET_NOT_SPECIFIED = -19, //!< No wallet specified (error when there are multiple wallets loaded)
    }

    public static class BitcoinCommands
    {
        public const string GetBalance = "getbalance";
        public const string ListUnspent = "listunspent";
        public const string GetNetworkInfo = "getnetworkinfo";
        public const string GetMiningInfo = "getmininginfo";
        public const string GetNetworkHashPS = "getnetworkhashps";
        public const string GetPeerInfo = "getpeerinfo";
        public const string ValidateAddress = "validateaddress";
        public const string GetAddressInfo = "getaddressinfo";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string GetBlockSubsidy = "getblocksubsidy";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockchainInfo = "getblockchaininfo";
        public const string GetBlock = "getblock";
        public const string GetBlockHash = "getblockhash";
        public const string GetTransaction = "gettransaction";
        public const string SendMany = "sendmany";
        public const string WalletPassphrase = "walletpassphrase";
        public const string WalletLock = "walletlock";
        public const string GetInfo = "getinfo";
        public const string GetDifficulty = "getdifficulty";
        public const string GetConnectionCount = "getconnectioncount";
    }
}