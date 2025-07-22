// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class SubscriptionsChangedEventArgs(
        List<MqttClientSubscribeResult> subscribeResult,
        List<MqttClientUnsubscribeResult> unsubscribeResult)
        : EventArgs
    {
        public List<MqttClientSubscribeResult> SubscribeResult { get; } =
            subscribeResult ?? throw new ArgumentNullException(nameof(subscribeResult));

        public List<MqttClientUnsubscribeResult> UnsubscribeResult { get; } =
            unsubscribeResult ?? throw new ArgumentNullException(nameof(unsubscribeResult));
    }
}