// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using MQTTnet.Client;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    public class MqttControllerContext : IMqttControllerContext
    {
        public MqttApplicationMessageReceivedEventArgs MqttContext { get; set; }
    }
}