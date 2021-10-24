using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cybercore.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcResponse : JsonRpcResponse<object>
    {
        public JsonRpcResponse()
        {
        }

        public JsonRpcResponse(object result, object id = null) : base(result, id)
        {
        }

        public JsonRpcResponse(JsonRpcException ex, object id = null, object result = null) : base(ex, id, result)
        {
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcResponse<T>
    {
        public JsonRpcResponse()
        {
        }

        public JsonRpcResponse(T result, object id = null)
        {
            Result = result;
            Id = id;
        }

        public JsonRpcResponse(JsonRpcException ex, object id, object result)
        {
            Error = ex;
            Id = id;

            if (result != null)
                Result = JToken.FromObject(result);
        }

        [JsonProperty(PropertyName = "result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty(PropertyName = "error")]
        public JsonRpcException Error { get; set; }

        [JsonProperty(PropertyName = "id", NullValueHandling = NullValueHandling.Ignore)]
        public object Id { get; set; }

        public TParam ResultAs<TParam>() where TParam : class
        {
            if (Result is JToken)
                return ((JToken)Result)?.ToObject<TParam>();

            return (TParam)Result;
        }
    }
}