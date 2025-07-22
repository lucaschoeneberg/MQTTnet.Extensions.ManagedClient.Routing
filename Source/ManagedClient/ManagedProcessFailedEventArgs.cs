// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using MQTTnet.Packets;

namespace MQTTnet.Extensions.ManagedClient.Routing.ManagedClient
{
    public class ManagedProcessFailedEventArgs : EventArgs
    {
        public ManagedProcessFailedEventArgs(Exception exception, List<MqttTopicFilter> addedSubscriptions, List<string> removedSubscriptions)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));

            if (addedSubscriptions != null)
            {
                AddedSubscriptions = new List<string>(addedSubscriptions.Select(item => item.Topic));
            }

            if (removedSubscriptions != null)
            {
                RemovedSubscriptions = new List<string>(removedSubscriptions);
            }
        }

        public Exception Exception { get; }

        public List<string> AddedSubscriptions { get; }
        public List<string> RemovedSubscriptions { get; }
    }
}