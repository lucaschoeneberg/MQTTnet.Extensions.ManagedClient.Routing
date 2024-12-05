using System;

namespace MQTTnet.Extensions.ManagedClient.Routing.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public class FromPayloadAttribute : Attribute
{
    public FromPayloadAttribute()
    {
        
    }
}