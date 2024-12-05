// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace MQTTnet.AspNetCore.Routing
{
    public interface IMqttControllerContext
    {
        MqttApplicationMessageReceivedEventArgs MqttContext { get; set; }
        
    }
}