using Microsoft.Extensions.Logging;
using MQTTnet.AspNetCore.Routing.Routing;
using MQTTnet.Server;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MQTTnet.Client;

namespace ExampleServer {
    public class DemoRouteInvocationInterceptor : IRouteInvocationInterceptor
    {
        private readonly ILogger _logger;

        public DemoRouteInvocationInterceptor(ILogger<DemoRouteInvocationInterceptor> logger)
        {
            _logger = logger;
        }
        
        private class CorrelationObject 
        {
            public string ClientId { get; set; }
            public string Topic { get; set; }
            public Stopwatch Stopwatch { get; set; }
        }

        public Task<object> RouteExecuting(MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs)
        {
            Debug.Assert( messageReceivedEventArgs != null );

            var correlationObject = new CorrelationObject 
            {
                Stopwatch = Stopwatch.StartNew(),
                ClientId = messageReceivedEventArgs.ClientId, 
                Topic = messageReceivedEventArgs.ApplicationMessage.Topic 
            };

            _logger.LogInformation( $"Before - {correlationObject.ClientId},{correlationObject.Topic}" );
            return Task.FromResult<object>( correlationObject );
        }

        public Task RouteExecuted(object o, MqttApplicationMessageReceivedEventArgs messageReceivedEventArgs,
            Exception ex)
        {
            Debug.Assert( o != null );
            Debug.Assert( o.GetType() == typeof( CorrelationObject ) );

            var correlationObject = (CorrelationObject)o;
            _logger.LogInformation( $"After - {correlationObject.ClientId},{correlationObject.Topic},{correlationObject.Stopwatch.ElapsedMilliseconds},{ex?.Message}" );
            return Task.CompletedTask;
        }
    }
}
