﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace Azure.SignalRBench.Messages
{
    public class MessageClient : IMessageClient, IDisposable
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly ISubscriber _subscriber;
        private readonly string _sender;
        private int _ackId;

        private MessageClient(IConnectionMultiplexer connection, ISubscriber subscriber, string sender)
        {
            _connection = connection;
            _subscriber = subscriber;
            _sender = sender;
        }

        public async static Task<MessageClient> ConnectAsync(string connectionString, string sender, params MessageHandler[] handlers)
        {
            var connection = await ConnectionMultiplexer.ConnectAsync(connectionString ?? throw new ArgumentNullException(nameof(connectionString)));
            var subscriber = connection.GetSubscriber();
            var result = new MessageClient(connection, subscriber, sender ?? throw new ArgumentNullException(nameof(sender)));
            foreach (var handler in handlers ?? throw new ArgumentNullException(nameof(handlers)))
            {
                var cmq = await subscriber.SubscribeAsync($"{handler.Name}:{handler.Type}");
                cmq.OnMessage(cm => handler.Handle(cm.Message));
            }
            return result;
        }

        public async Task SendCommandAsync(string target, CommandMessage commandMessage)
        {
            var ackId = Interlocked.Increment(ref _ackId);
            commandMessage.Sender = _sender;
            commandMessage.AckId = ackId;
            await _subscriber.PublishAsync($"{target}:{MessageType.Command}", JsonConvert.SerializeObject(commandMessage));
        }

        public async Task AckAsync(CommandMessage commandMessage, bool isCompleted, double? progress = null)
        {
            var message = new AckMessage { Sender = _sender, AckId = commandMessage.AckId, IsCompleted = isCompleted, Progress = progress };
            await _subscriber.PublishAsync($"{commandMessage.Sender}:{MessageType.Ack}", JsonConvert.SerializeObject(message));
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}