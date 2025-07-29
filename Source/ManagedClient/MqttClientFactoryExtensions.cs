// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using MQTTnet.Diagnostics.Logger;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public static class MqttClientFactoryExtensions
    {
        public static IManagedMqttClient CreateManagedMqttClient(this MqttClientFactory factory,
            IMqttClient mqttClient = null)
        {
            ArgumentNullException.ThrowIfNull(factory);

            return mqttClient == null
                ? new ManagedMqttClient(factory.CreateMqttClient(), factory.DefaultLogger)
                : new ManagedMqttClient(mqttClient, factory.DefaultLogger);
        }

        public static IManagedMqttClient CreateManagedMqttClient(this MqttClientFactory factory, IMqttNetLogger logger)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(logger);

            return new ManagedMqttClient(factory.CreateMqttClient(logger), logger);
        }

        public static ManagedMqttClientOptionsBuilder CreateManagedMqttClientOptionsBuilder(this MqttClientFactory _)
        {
            return new ManagedMqttClientOptionsBuilder();
        }
    }
}