// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public sealed class ApplicationMessageProcessedEventArgs(
        ManagedMqttApplicationMessage applicationMessage,
        Exception exception)
        : EventArgs
    {
        public ManagedMqttApplicationMessage ApplicationMessage { get; } = applicationMessage ?? throw new ArgumentNullException(nameof(applicationMessage));

        /// <summary>
        /// Then this is _null_ the message was processed successfully without any error.
        /// </summary>
        public Exception Exception { get; } = exception;
    }
}
