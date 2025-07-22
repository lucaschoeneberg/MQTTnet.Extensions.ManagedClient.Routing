// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    public interface IMqttControllerContext
    {
        MqttApplicationMessageReceivedEventArgs MqttContext { get; set; }
    }
}