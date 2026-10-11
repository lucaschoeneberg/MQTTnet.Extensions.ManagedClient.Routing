// Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    [MqttController]
    public abstract class MqttBaseController
    {
        /// <summary>
        /// Connection context is set by controller activator. If this class is instantiated directly, it will be null.
        /// </summary>
        public MqttApplicationMessageReceivedEventArgs MqttContext => ControllerContext.MqttContext;

        /// <summary>
        /// Gets the <see cref="MqttApplicationMessage"/> for the executing action.
        /// </summary>
        public MqttApplicationMessage Message => MqttContext.ApplicationMessage;

        /// <summary>
        /// ControllerContext is set by controller activator. If this class is instantiated directly, it will be null.
        /// </summary>
        [MqttControllerContext]
        public MqttControllerContext ControllerContext { get; set; } = null!;

        /// <summary>
        /// Completes the action successfully. The message keeps its accepted
        /// state; no reply is published. This is an explicit no-op marker for readability.
        /// </summary>
        /// <returns>The created <see cref="Task"/> for the reponse.</returns>
        [NonAction]
        public virtual Task Ok()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Completes the action successfully. Alias for <see cref="Ok"/>.
        /// </summary>
        /// <returns>The created <see cref="Task"/> for the reponse.</returns>
        [NonAction]
        public virtual Task Accepted() => Ok();

        /// <summary>
        /// Completes the action without effect. Note: this does not reject the
        /// message by itself. Throw an exception (or set the context state) to
        /// mark processing as failed.
        /// </summary>
        /// <returns>The created <see cref="Task"/> for the reponse.</returns>
        [NonAction]
        public virtual Task BadMessage()
        {
            return Task.CompletedTask;
        }
    }
}
