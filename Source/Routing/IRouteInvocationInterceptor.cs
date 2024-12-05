using MQTTnet.Server;
using System;
using System.Threading.Tasks;
using MQTTnet.Client;

namespace MQTTnet.AspNetCore.Routing.Routing {
    public interface IRouteInvocationInterceptor
    {
        /// <summary>
        /// Executed before the route handler is executed.
        /// </summary>
        /// <param name="messageReceivedEventArgs">An instance of <see cref="MqttApplicationMessageReceivedEventArgs"/>, containing information about the message received.</param>
        /// <returns>Returns an opaque object that may be used to correlate before- and after route execution. May be null.</returns>
        Task<object> RouteExecuting(MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs);

        /// <summary>
        /// Executed after the route handler has been executed.
        /// </summary>
        /// <param name="correlationObject">The response from <see cref="RouteExecuting"/>. May be null.</param>
        /// <param name="messageReceivedEventArgs">An instance of <see cref="MqttApplicationMessageReceivedEventArgs"/>, containing information about the message response.</param>
        /// <param name="ex">An exception if the route handler failed, otherwise null.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task RouteExecuted(object correlationObject, MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs,
            Exception ex);
    }
}
