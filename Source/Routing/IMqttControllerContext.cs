// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using MQTTnet.Client;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    public interface IMqttControllerContext
    {
        MqttApplicationMessageReceivedEventArgs MqttContext { get; set; }
    }
}