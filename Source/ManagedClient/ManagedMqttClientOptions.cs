// Vendored MQTTnet ManagedClient fork - excluded from nullable analysis (see Directory.Build.props).
#nullable disable

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using MQTTnet.Server;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class ManagedMqttClientOptions
    {
        public MqttClientOptions ClientOptions { get; set; }

        public TimeSpan AutoReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Upper bound for the reconnect delay. The actual delay grows
        /// exponentially from <see cref="AutoReconnectDelay"/> with the number
        /// of consecutive failures (plus jitter) and never exceeds this value.
        /// Defaults to two minutes; must be at least one second, matching the
        /// reconnect delay floor, and at most 4,294,967,294 milliseconds (the .NET
        /// timer maximum). StartAsync rejects values outside this range. A new
        /// StartAsync session resets the failure count. Zero or negative base
        /// delays are treated as one millisecond before applying the floor.
        /// </summary>
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromMinutes(2);

        public TimeSpan ConnectionCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

        public IManagedMqttClientStorage Storage { get; set; }

        /// <summary>
        /// Maximum number of queued outbound messages while offline. Bounds
        /// memory usage; excess is handled per
        /// <see cref="PendingMessagesOverflowStrategy"/>.
        /// </summary>
        public int MaxPendingMessages { get; set; } = 1000;

        public MqttPendingMessagesOverflowStrategy PendingMessagesOverflowStrategy { get; set; } = MqttPendingMessagesOverflowStrategy.DropNewMessage;

        /// <summary>
        /// Defines the maximum amount of topic filters which will be sent in a SUBSCRIBE/UNSUBSCRIBE packet.
        /// Amazon Web Services (AWS) limits this number to 8. The default is int.MaxValue.
        /// </summary>
        public int MaxTopicFiltersInSubscribeUnsubscribePackets { get; set; } = int.MaxValue;
    }
}
