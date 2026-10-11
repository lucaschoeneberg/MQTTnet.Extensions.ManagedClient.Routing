// Copyright (c) Atlas Lift Tech Inc. All rights reserved.
namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    public class MqttControllerContext : IMqttControllerContext
    {
        /// <summary>
        /// The message context for the currently executing action. Set by the
        /// routing infrastructure before the action is invoked.
        /// </summary>
        public MqttApplicationMessageReceivedEventArgs MqttContext { get; set; } = null!;
    }
}
