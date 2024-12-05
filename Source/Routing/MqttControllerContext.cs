// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace MQTTnet.AspNetCore.Routing
{
    public class MqttControllerContext : IMqttControllerContext
    {
        public MqttApplicationMessageReceivedEventArgs MqttContext { get; set; }

        public ManagedMqttClient Client { get; set; }
    }
}