// Copyright (c) .NET Foundation. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt
// in the project root for license information.

// Modifications Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    /// <summary>
    /// Caches <see cref="ObjectFactory"/> instances produced by <see cref="ActivatorUtilities.CreateFactory(Type, Type[])"/>.
    /// </summary>
    internal class TypeActivatorCache : ITypeActivatorCache
    {
        private readonly Func<Type, ObjectFactory> _createFactory =
            (type) => ActivatorUtilities.CreateFactory(type, Type.EmptyTypes);

        private readonly ConcurrentDictionary<Type, ObjectFactory> _typeActivatorCache = new();

        /// <inheritdoc/>
        public TInstance CreateInstance<TInstance>(IServiceProvider serviceProvider, Type implementationType)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            ArgumentNullException.ThrowIfNull(implementationType);
            var createFactory = _typeActivatorCache.GetOrAdd(implementationType, _createFactory);

            return (TInstance)createFactory(serviceProvider, arguments: null);
        }
    }
}