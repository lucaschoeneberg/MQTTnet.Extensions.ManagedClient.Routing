using System;

namespace MQTTnet.Extensions.ManagedClient.Routing.Attributes
{
    /// <summary>
    /// When creating a custom controller that does not inherit from <see cref="Routing.MqttBaseController"/>, this attribute
    /// tells the activator which property the <see cref="MQTTnet.MqttApplicationMessageReceivedEventArgs"/> should be assigned to.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class MqttControllerContextAttribute : Attribute
    {
    }
}
