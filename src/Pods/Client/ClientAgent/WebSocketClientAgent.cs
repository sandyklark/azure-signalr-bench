﻿using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.SignalRBench.Common;
using Newtonsoft.Json;

namespace Azure.SignalRBench.Client
{
    class WebSocketClientAgent : IClientAgent
    {
        public ClientAgentContext Context { get; }
        public int GlobalIndex { get; }
        public string[] Groups { get; } = Array.Empty<string>();

        private WebSocketHubConnection Connection { get; }

        public WebSocketClientAgent(string connectionString, Protocol protocol, string[] groups, int globalIndex, ClientAgentContext context)
        {
            if (!TryParseEndpoint(connectionString, out var endpoint))
            {
                throw new ArgumentNullException("Connection string misses required property Endpoint");
            }
            Context = context;
            Connection = new WebSocketHubConnection(endpoint);
            Connection.On(context.Measure);
            Groups = groups;
            GlobalIndex = globalIndex;
        }

        public Task BroadcastAsync(string payload) => throw new NotImplementedException();

        public Task EchoAsync(string payload)
        {
            var data = new Data()
            {
                Ticks = DateTime.Now.Ticks,
                Payload = payload
            };
            return Connection.SendAsync(JsonConvert.SerializeObject(data));
        }

        public Task GroupBroadcastAsync(string group, string payload) => throw new NotImplementedException();

        public Task JoinGroupAsync() => throw new NotImplementedException();

        public Task SendToClientAsync(int index, string payload)
        {
            throw new NotImplementedException();
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await Connection.StartAsync(cancellationToken);
            await Context.OnConnected(this, false);
        }

        public async Task StopAsync()
        {
            await Connection.StopAsync();
        }

        private bool TryParseEndpoint(string connectionString, out string endpoint)
        {
            var properties = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var property in properties)
            {
                var kvp = property.Split('=');
                if (kvp.Length != 2) continue;

                if (string.Compare("endpoint", kvp.First(), true) == 0)
                {
                    endpoint = kvp.Last();
                    endpoint = endpoint.Replace("http", "ws");
                    return true;
                }
            }
            endpoint = "";
            return false;
        }

        private sealed class Data
        {
            public string Payload { get; set; } = "";
            public long Ticks { get; set; }
        }

        private sealed class WebSocketHubConnection
        {
            private readonly ClientWebSocket _socket;
            private readonly CancellationTokenSource _connectionStoppedCts = new CancellationTokenSource();

            private Action<long, string>? _handler;
            public Uri ResourceUri { get; }
            private CancellationToken ConnectionStoppedToken => _connectionStoppedCts.Token;

            public WebSocketHubConnection(string endpoint)
            {
                _socket = new ClientWebSocket();
                ResourceUri = new Uri(endpoint + "/ws/client");
            }

            public void On(Action<long, string> callback)
            {
                _handler = callback;
            }

            public Task SendAsync(string payload)
            {
                return _socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, default);
            }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                await _socket.ConnectAsync(ResourceUri, cancellationToken);
                _ = ReceiveLoop();
            }

            public async Task StopAsync()
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", default);
                _connectionStoppedCts.Cancel();
            }

            private async Task ReceiveLoop()
            {
                var buffer = new byte[1 << 10];
                while (_socket.State == WebSocketState.Open)
                {
                    try
                    {
                        var response = await _socket.ReceiveAsync(buffer, ConnectionStoppedToken);
                        var dataStr = Encoding.UTF8.GetString(buffer, 0, response.Count);
                        var data = JsonConvert.DeserializeObject<Data>(dataStr);
                        _handler?.Invoke(data.Ticks, data.Payload);
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                }
            }
        }
    }
}