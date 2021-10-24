using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Cybercore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ZeroMQ;
using Contract = Cybercore.Contracts.Contract;

namespace Cybercore.DaemonInterface
{
    public class DaemonClient
    {
        public DaemonClient(JsonSerializerSettings serializerSettings, IMessageBus messageBus, string server, string poolId)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(poolId), $"{nameof(poolId)} must not be empty");

            this.serializerSettings = serializerSettings;
            this.messageBus = messageBus;
            this.poolId = poolId;

            serializer = new JsonSerializer
            {
                ContractResolver = serializerSettings.ContractResolver
            };
        }

        private readonly JsonSerializerSettings serializerSettings;

        protected DaemonEndpointConfig[] endPoints;
        private readonly JsonSerializer serializer;

        private static readonly HttpClient httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,

            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
        });

        private readonly IMessageBus messageBus;

        private readonly string poolId;

        protected void PublishTelemetry(TelemetryCategory cat, TimeSpan elapsed, string info, bool? success = null, string error = null)
        {
            messageBus.SendMessage(new TelemetryEvent(poolId, cat, info, elapsed, success));
        }

        #region API-Surface

        public void Configure(DaemonEndpointConfig[] endPoints)
        {
            Contract.RequiresNonNull(endPoints, nameof(endPoints));
            Contract.Requires<ArgumentException>(endPoints.Length > 0, $"{nameof(endPoints)} must not be empty");

            this.endPoints = endPoints;
        }

        public Task<DaemonResponse<JToken>[]> ExecuteCmdAllAsync(ILogger logger, string method, CancellationToken ct)
        {
            return ExecuteCmdAllAsync<JToken>(logger, method, ct);
        }

        public async Task<DaemonResponse<TResponse>[]> ExecuteCmdAllAsync<TResponse>(ILogger logger, string method, CancellationToken ct,
            object payload = null, JsonSerializerSettings payloadJsonSerializerSettings = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { "\"" + method + "\"" });

            var tasks = endPoints.Select(endPoint => BuildRequestTask(logger, endPoint, method, payload, CancellationToken.None, payloadJsonSerializerSettings)).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }

            catch (Exception)
            {
            }

            var results = tasks.Select((x, i) => MapDaemonResponse<TResponse>(i, x))
                .ToArray();

            return results;
        }

        public Task<DaemonResponse<JToken>> ExecuteCmdAnyAsync(ILogger logger, string method, CancellationToken ct, bool throwOnError = false)
        {
            return ExecuteCmdAnyAsync<JToken>(logger, method, ct, null, null, throwOnError);
        }

        public async Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TResponse>(ILogger logger, string method, CancellationToken ct, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null, bool throwOnError = false)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { "\"" + method + "\"" });

            var tasks = endPoints.Select(endPoint => BuildRequestTask(logger, endPoint, method, payload, CancellationToken.None, payloadJsonSerializerSettings)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonResponse<TResponse>(0, taskFirstCompleted, throwOnError);
            return result;
        }

        public async Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TResponse>(ILogger logger, CancellationToken ct, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null, bool throwOnError = false)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { "\"" + method + "\"" });

            var tasks = endPoints.Select(endPoint => BuildRequestTask(logger, endPoint, method, payload, ct, payloadJsonSerializerSettings)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonResponse<TResponse>(0, taskFirstCompleted, throwOnError);
            return result;
        }

        public Task<DaemonResponse<JToken>> ExecuteCmdSingleAsync(ILogger logger, string method, CancellationToken ct)
        {
            return ExecuteCmdAnyAsync<JToken>(logger, method, ct);
        }

        public async Task<DaemonResponse<TResponse>> ExecuteCmdSingleAsync<TResponse>(ILogger logger, string method, CancellationToken ct, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { "\"" + method + "\"" });

            var task = BuildRequestTask(logger, endPoints.First(), method, payload, CancellationToken.None, payloadJsonSerializerSettings);
            await Task.WhenAny(task);

            var result = MapDaemonResponse<TResponse>(0, task);
            return result;
        }

        public async Task<DaemonResponse<TResponse>> ExecuteCmdSingleAsync<TResponse>(ILogger logger, CancellationToken ct, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { "\"" + method + "\"" });

            var task = BuildRequestTask(logger, endPoints.First(), method, payload, ct, payloadJsonSerializerSettings);
            await Task.WhenAny(task);

            var result = MapDaemonResponse<TResponse>(0, task);
            return result;
        }

        public async Task<DaemonResponse<JToken>[]> ExecuteBatchAnyAsync(ILogger logger, CancellationToken ct, params DaemonCmd[] batch)
        {
            Contract.RequiresNonNull(batch, nameof(batch));

            logger.LogInvoke(batch.Select(x => "\"" + x.Method + "\"").ToArray());

            var tasks = endPoints.Select(endPoint => BuildBatchRequestTask(logger, ct, endPoint, batch)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonBatchResponse(0, taskFirstCompleted);
            return result;
        }

        public IObservable<byte[]> WebsocketSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig,
                (int Port, string HttpPath, bool Ssl)> portMap, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { method });

            return Observable.Merge(portMap.Keys
                    .Select(endPoint => WebsocketSubscribeEndpoint(logger, ct, endPoint, portMap[endPoint], method, payload, payloadJsonSerializerSettings)))
                .Publish()
                .RefCount();
        }

        public IObservable<ZMessage> ZmqSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig, (string Socket, string Topic)> portMap)
        {
            logger.LogInvoke();

            return Observable.Merge(portMap.Keys
                    .Select(endPoint => ZmqSubscribeEndpoint(logger, ct, endPoint, portMap[endPoint].Socket, portMap[endPoint].Topic)))
                .Publish()
                .RefCount();
        }

        #endregion // API-Surface

        private async Task<JsonRpcResponse> BuildRequestTask(ILogger logger, DaemonEndpointConfig endPoint, string method, object payload,
            CancellationToken ct, JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            var rpcRequestId = GetRequestId();

            var sw = Stopwatch.StartNew();

            var rpcRequest = new JsonRpcRequest<object>(method, payload, rpcRequestId);

            var protocol = (endPoint.Ssl || endPoint.Http2) ? "https" : "http";
            var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";
            if (!string.IsNullOrEmpty(endPoint.HttpPath))
                requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Headers.ConnectionClose = false;

                if (endPoint.Http2)
                    request.Version = new Version(2, 0);

                var json = JsonConvert.SerializeObject(rpcRequest, payloadJsonSerializerSettings ?? serializerSettings);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(endPoint.User))
                {
                    var auth = $"{endPoint.User}:{endPoint.Password}";
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
                }

                logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

                using (var response = await httpClient.SendAsync(request, ct))
                {
                    var responseContent = await response.Content.ReadAsStringAsync(ct);

                    using (var jreader = new JsonTextReader(new StringReader(responseContent)))
                    {
                        var result = serializer.Deserialize<JsonRpcResponse>(jreader);

                        sw.Stop();
                        PublishTelemetry(TelemetryCategory.RpcRequest, sw.Elapsed, method, response.IsSuccessStatusCode);

                        return result;
                    }
                }
            }
        }

        private async Task<JsonRpcResponse<JToken>[]> BuildBatchRequestTask(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, DaemonCmd[] batch)
        {
            var sw = Stopwatch.StartNew();
            var rpcRequests = batch.Select(x => new JsonRpcRequest<object>(x.Method, x.Payload, GetRequestId()));
            var protocol = (endPoint.Ssl || endPoint.Http2) ? "https" : "http";
            var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";
            if (!string.IsNullOrEmpty(endPoint.HttpPath))
                requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Headers.ConnectionClose = false;

                if (endPoint.Http2)
                    request.Version = new Version(2, 0);

                var json = JsonConvert.SerializeObject(rpcRequests, serializerSettings);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(endPoint.User))
                {
                    var auth = $"{endPoint.User}:{endPoint.Password}";
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
                }

                logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

                using (var response = await httpClient.SendAsync(request, ct))
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(ct);

                    using (var jreader = new JsonTextReader(new StringReader(jsonResponse)))
                    {
                        var result = serializer.Deserialize<JsonRpcResponse<JToken>[]>(jreader);

                        sw.Stop();
                        PublishTelemetry(TelemetryCategory.RpcRequest, sw.Elapsed, string.Join(", ", batch.Select(x => x.Method)), true);

                        return result;
                    }
                }
            }
        }

        protected string GetRequestId()
        {
            var rpcRequestId = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + StaticRandom.Next(10)).ToString();
            return rpcRequestId;
        }

        private DaemonResponse<TResponse> MapDaemonResponse<TResponse>(int i, Task<JsonRpcResponse> x, bool throwOnError = false)
            where TResponse : class
        {
            var resp = new DaemonResponse<TResponse>
            {
                Instance = endPoints[i]
            };

            if (x.IsFaulted)
            {
                Exception inner;

                if (x.Exception.InnerExceptions.Count == 1)
                    inner = x.Exception.InnerException;
                else
                    inner = x.Exception;

                if (throwOnError)
                    throw inner;

                resp.Error = new JsonRpcException(-500, x.Exception.Message, null, inner);
            }

            else if (x.IsCanceled)
            {
                resp.Error = new JsonRpcException(-500, "Cancelled", null);
            }

            else
            {
                Debug.Assert(x.IsCompletedSuccessfully);

                if (x.Result?.Result is JToken token)
                    resp.Response = token?.ToObject<TResponse>(serializer);
                else
                    resp.Response = (TResponse)x.Result?.Result;

                resp.Error = x.Result?.Error;
            }

            return resp;
        }

        private DaemonResponse<JToken>[] MapDaemonBatchResponse(int i, Task<JsonRpcResponse<JToken>[]> x)
        {
            if (x.IsFaulted)
                return x.Result?.Select(y => new DaemonResponse<JToken>
                {
                    Instance = endPoints[i],
                    Error = new JsonRpcException(-500, x.Exception.Message, null)
                }).ToArray();

            Debug.Assert(x.IsCompletedSuccessfully);

            return x.Result?.Select(y => new DaemonResponse<JToken>
            {
                Instance = endPoints[i],
                Response = y.Result != null ? JToken.FromObject(y.Result) : null,
                Error = y.Error
            }).ToArray();
        }

        private IObservable<byte[]> WebsocketSubscribeEndpoint(ILogger logger, CancellationToken ct, NetworkEndpointConfig endPoint,
            (int Port, string HttpPath, bool Ssl) conf, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            return Observable.Defer(() => Observable.Create<byte[]>(obs =>
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                Task.Run(async () =>
                {
                    using (cts)
                    {
                        var buf = new byte[0x10000];

                        while (!cts.IsCancellationRequested)
                        {
                            try
                            {
                                using (var client = new ClientWebSocket())
                                {
                                    var protocol = conf.Ssl ? "wss" : "ws";
                                    var uri = new Uri($"{protocol}://{endPoint.Host}:{conf.Port}{conf.HttpPath}");
                                    client.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                                    logger.Debug(() => $"Establishing WebSocket connection to {uri}");
                                    await client.ConnectAsync(uri, cts.Token);

                                    var request = new JsonRpcRequest(method, payload, GetRequestId());
                                    var json = JsonConvert.SerializeObject(request, payloadJsonSerializerSettings);
                                    var requestData = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));

                                    logger.Debug(() => $"Sending WebSocket subscription request to {uri}");
                                    await client.SendAsync(requestData, WebSocketMessageType.Text, true, cts.Token);

                                    var stream = new MemoryStream();

                                    while (!cts.IsCancellationRequested && client.State == WebSocketState.Open)
                                    {
                                        stream.SetLength(0);
                                        var complete = false;

                                        do
                                        {
                                            using (var ctsTimeout = new CancellationTokenSource())
                                            {
                                                using (var ctsComposite = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ctsTimeout.Token))
                                                {
                                                    ctsTimeout.CancelAfter(TimeSpan.FromMinutes(10));

                                                    var response = await client.ReceiveAsync(buf, ctsComposite.Token);

                                                    if (response.MessageType == WebSocketMessageType.Binary)
                                                        throw new InvalidDataException("expected text, received binary data");

                                                    await stream.WriteAsync(buf, 0, response.Count, ctsComposite.Token);

                                                    complete = response.EndOfMessage;
                                                }
                                            }
                                        } while (!complete && !cts.IsCancellationRequested && client.State == WebSocketState.Open);

                                        logger.Debug(() => $"Received WebSocket message with length {stream.Length}");

                                        obs.OnNext(stream.ToArray());
                                    }
                                }
                            }

                            catch (Exception ex)
                            {
                                logger.Error(() => $"{ex.GetType().Name} '{ex.Message}' while streaming websocket responses. Reconnecting in 5s");
                            }

                            if (!cts.IsCancellationRequested)
                                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                        }
                    }
                }, cts.Token);

                return Disposable.Create(() => { cts.Cancel(); });
            }));
        }

        private static IObservable<ZMessage> ZmqSubscribeEndpoint(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, string url, string topic)
        {
            return Observable.Defer(() => Observable.Create<ZMessage>(obs =>
            {
                var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);

                Task.Run(() =>
                {
                    using (tcs)
                    {
                        while (!tcs.IsCancellationRequested)
                        {
                            try
                            {
                                using (var subSocket = new ZSocket(ZSocketType.SUB))
                                {
                                    subSocket.Connect(url);
                                    subSocket.Subscribe(topic);

                                    logger.Debug($"Subscribed to {url}/{topic}");

                                    while (!tcs.IsCancellationRequested)
                                    {
                                        var msg = subSocket.ReceiveMessage();
                                        obs.OnNext(msg);
                                    }
                                }
                            }

                            catch (Exception ex)
                            {
                                logger.Error(ex);
                            }

                            Thread.Sleep(1000);
                        }
                    }
                }, tcs.Token);

                return Disposable.Create(() => { tcs.Cancel(); });
            }));
        }
    }
}