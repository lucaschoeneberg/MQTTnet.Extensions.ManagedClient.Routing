// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class SendSubscribeUnsubscribeResult(
        List<MqttClientSubscribeResult> subscribeResults,
        List<MqttClientUnsubscribeResult> unsubscribeResults)
    {
        public List<MqttClientSubscribeResult> SubscribeResults { get; private set; } =
            subscribeResults ?? throw new ArgumentNullException(nameof(subscribeResults));

        public List<MqttClientUnsubscribeResult> UnsubscribeResults { get; private set; } =
            unsubscribeResults ?? throw new ArgumentNullException(nameof(unsubscribeResults));
    }
}