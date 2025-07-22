// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MQTTnet.Internal;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public class ManagedMqttClientStorageManager(IManagedMqttClientStorage storage)
    {
        private readonly List<ManagedMqttApplicationMessage> _messages = [];
        private readonly AsyncLock _messagesLock = new();

        private readonly IManagedMqttClientStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));

        public async Task<List<ManagedMqttApplicationMessage>> LoadQueuedMessagesAsync()
        {
            var loadedMessages = await _storage.LoadQueuedMessagesAsync().ConfigureAwait(false);
            _messages.AddRange(loadedMessages);

            return _messages;
        }

        public async Task AddAsync(ManagedMqttApplicationMessage applicationMessage)
        {
            ArgumentNullException.ThrowIfNull(applicationMessage);

            using (await _messagesLock.EnterAsync().ConfigureAwait(false))
            {
                _messages.Add(applicationMessage);
                await SaveAsync().ConfigureAwait(false);
            }
        }

        public async Task RemoveAsync(ManagedMqttApplicationMessage applicationMessage)
        {
            ArgumentNullException.ThrowIfNull(applicationMessage);

            using (await _messagesLock.EnterAsync().ConfigureAwait(false))
            {
                var index = _messages.IndexOf(applicationMessage);
                if (index == -1)
                {
                    return;
                }

                _messages.RemoveAt(index);
                await SaveAsync().ConfigureAwait(false);
            }
        }

        private Task SaveAsync()
        {
            return _storage.SaveQueuedMessagesAsync(_messages);
        }
    }
}
