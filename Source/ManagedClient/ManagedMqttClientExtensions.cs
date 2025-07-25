// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public static class ManagedMqttClientExtensions
    {
        public static Task EnqueueAsync(
            this IManagedMqttClient managedMqttClient,
            string topic,
            string payload = null,
            MqttQualityOfServiceLevel qualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
            bool retain = false)
        {
            ArgumentNullException.ThrowIfNull(managedMqttClient);

            ArgumentNullException.ThrowIfNull(topic);

            var applicationMessage = new MqttApplicationMessageBuilder().WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(qualityOfServiceLevel)
                .Build();

            return managedMqttClient.EnqueueAsync(applicationMessage);
        }

        public static Task SubscribeAsync(
            this IManagedMqttClient managedMqttClient,
            string topic,
            MqttQualityOfServiceLevel qualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce)
        {
            ArgumentNullException.ThrowIfNull(managedMqttClient);

            ArgumentNullException.ThrowIfNull(topic);

            return managedMqttClient.SubscribeAsync(
                new List<MqttTopicFilter>
                {
                    new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(qualityOfServiceLevel)
                        .Build()
                });
        }

        public static Task UnsubscribeAsync(this IManagedMqttClient managedMqttClient, string topic)
        {
            ArgumentNullException.ThrowIfNull(managedMqttClient);

            ArgumentNullException.ThrowIfNull(topic);

            return managedMqttClient.UnsubscribeAsync(new List<string> { topic });
        }
    }
}