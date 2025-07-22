// Copyright (c) Atlas Lift Tech Inc. All rights reserved.
namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    public class MqttControllerContext : IMqttControllerContext
    {
        public MqttApplicationMessageReceivedEventArgs MqttContext { get; set; }
    }
}