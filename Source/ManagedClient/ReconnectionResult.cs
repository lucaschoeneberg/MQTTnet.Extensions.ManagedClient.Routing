// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public enum ReconnectionResult
    {
        StillConnected,
        Reconnected,
        Recovered,
        NotConnected
    }
}
