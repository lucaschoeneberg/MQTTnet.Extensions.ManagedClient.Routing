// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class InterceptingPublishMessageEventArgs : EventArgs
    {
        public InterceptingPublishMessageEventArgs(ManagedMqttApplicationMessage applicationMessage)
        {
            ApplicationMessage = applicationMessage ?? throw new ArgumentNullException(nameof(applicationMessage));
        }

        public ManagedMqttApplicationMessage ApplicationMessage { get; }

        public bool AcceptPublish { get; set; } = true;
    }
}
