using Cybercore.Configuration;
using Cybercore.JsonRpc;

namespace Cybercore.DaemonInterface
{
    public class DaemonResponse<T>
    {
        public JsonRpcException Error { get; set; }
        public T Response { get; set; }
        public AuthenticatedNetworkEndpointConfig Instance { get; set; }
    }
}