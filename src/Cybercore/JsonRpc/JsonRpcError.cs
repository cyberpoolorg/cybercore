using System;
using Newtonsoft.Json;

namespace Cybercore.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcException
    {
        public JsonRpcException(int code, string message, object data, Exception inner = null)
        {
            Code = code;
            Message = message;
            Data = data;
            InnerException = inner;
        }

        [JsonProperty(PropertyName = "code")]
        public int Code { get; set; }

        [JsonProperty(PropertyName = "message")]
        public string Message { get; set; }

        [JsonProperty(PropertyName = "data")]
        public object Data { get; set; }

        [JsonIgnore]
        public Exception InnerException { get; set; }
    }
}