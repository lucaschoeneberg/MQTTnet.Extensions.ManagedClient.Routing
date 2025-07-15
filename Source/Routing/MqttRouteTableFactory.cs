// Copyright (c) .NET Foundation. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt
// in the project root for license information.

// Modifications Copyright (c) Atlas Lift Tech Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using MQTTnet.Extensions.ManagedClient.Routing.Attributes;
using MQTTnet.Extensions.ManagedClient.Routing.Templates;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    internal static class MqttRouteTableFactory
    {
        private static readonly ConcurrentDictionary<Key, MqttRouteTable> Cache = new ConcurrentDictionary<Key, MqttRouteTable>();
        public static readonly IComparer<MqttRoute> RoutePrecedence = Comparer<MqttRoute>.Create(RouteComparison);

        /// <summary>
        /// Given a list of assemblies, find all instances of MqttControllers and wire up routing for them. Instances of
        /// controllers must inherit from MqttBaseController and be decorated with an MqttRoute attribute.
        /// </summary>
        /// <param name="assembly">Assemblies to scan for routes</param>
        /// <returns></returns>
        internal static MqttRouteTable Create(IEnumerable<Assembly> assemblies)
        {
            var enumerable = assemblies as Assembly[] ?? assemblies.ToArray();
            var key = new Key(enumerable.OrderBy(a => a.FullName).ToArray());

            if (Cache.TryGetValue(key, out var resolvedComponents))
            {
                return resolvedComponents;
            }

            var asm = enumerable;

            var actions = asm.SelectMany(a => a.GetTypes())
                .Where(type => type.GetCustomAttribute(typeof(MqttControllerAttribute), true) != null)
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public))
                .Where(m => !m.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), true).Any() && !m.IsDefined(typeof(NonActionAttribute)));

            var routeTable = Create(actions);

            Cache.TryAdd(key, routeTable);

            return routeTable;
        }

        internal static MqttRouteTable Create(IEnumerable<MethodInfo> actions)
        {
            // A future perf improvement would be to use a stringbuilder to avoid multiple string allocations

            var templatesByHandler = new Dictionary<MethodInfo, string[]>();

            foreach (var action in actions)
            {
                // We're deliberately using inherit = false here. // MqttRouteAttribute is defined as non-inherited,
                // because inheriting a route attribute always causes an ambiguity. You end up with two components (base
                // class and derived class) with the same route.
                var controllerTemplates = action.DeclaringType.GetCustomAttributes<MqttRouteAttribute>(inherit: false)
                    .Select(c => ReplaceTokens(c.Template, action.DeclaringType.Name, action.Name) + "/")
                    .ToArray();

                var routeAttributes = action.GetCustomAttributes<MqttRouteAttribute>(inherit: false)
                    .Select(a => ReplaceTokens(a.Template, action.DeclaringType.Name, action.Name))
                    .ToArray();

                if (controllerTemplates.Length == 0)
                {
                    controllerTemplates = new string[] { "" };
                }

                // If an action doesn't have a route attribute on it, we use the action name. Unlike Mvc/WebAPI we don't
                // need to strip the "Get", "Put", etc. prefixes from the action because MQTT doesn't have verbs by convention.
                if (routeAttributes.Length == 0)
                {
                    routeAttributes = new string[] { action.Name };
                }

                // If an action starts with a /, we throw away the inherited portion of the path. We don't process ~/
                // because it wouldn't make sense in the context of Mqtt routing which has no concept of relative paths.
                var templates = controllerTemplates.SelectMany((c) => routeAttributes, (c, a) =>  $"{c}{a}").ToArray();
              
                templatesByHandler.Add(action, templates);
            }

            return Create(templatesByHandler);
        }

        /// <summary>
        /// Generate routes given a collection of MethodInfo objects and templates that should call those methods
        /// </summary>
        /// <param name="templatesByHandler">Templates that should route to each handler</param>
        internal static MqttRouteTable Create(Dictionary<MethodInfo, string[]> templatesByHandler)
        {
            var routes = new List<MqttRoute>();

            foreach (var keyValuePair in templatesByHandler)
            {
                var parsedTemplates = keyValuePair.Value.Select(v => TemplateParser.ParseTemplate(v)).ToArray();

                var allRouteParameterNames = parsedTemplates
                    .SelectMany(GetParameterNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var parsedTemplate in parsedTemplates)
                {
                    var unusedRouteParameterNames = allRouteParameterNames
                        .Except(GetParameterNames(parsedTemplate), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var methodInfo = keyValuePair.Key;
                    var entry = new MqttRoute(parsedTemplate, methodInfo, unusedRouteParameterNames);
                    var mrat = methodInfo.DeclaringType.GetCustomAttribute<MqttRouteAttribute>();
                    if (mrat != null)
                    {
                        entry.ControllerTemplate = TemplateParser.ParseTemplate(mrat.Template);
                        var haveparams = entry.ControllerTemplate.Segments.Any(c => c.IsParameter);
                        entry.HaveControllerParameter = haveparams;
                    }
                    routes.Add(entry);
                }
            }

            return new MqttRouteTable(routes.OrderBy(id => id, RoutePrecedence).ToArray());
        }

        /// <summary>
        /// Returns the names of all parameters in a given RouteTemplate
        /// </summary>
        private static string[] GetParameterNames(RouteTemplate routeTemplate)
        {
            return routeTemplate.Segments
                .Where(s => s.IsParameter)
                .Select(s => s.Value)
                .ToArray();
        }

        /// <summary>
        /// Given a route template string suchs a "[controller]/[action]" replace the tokens with the values provided.
        /// /// Controllers with a suffix of "Controller" will be chopped to exclude the word Controller from the
        /// returns route string.
        /// </summary>
        /// <param name="template">Template string</param>
        /// <param name="controllerName">Name of the controller object</param>
        /// <param name="actionName">Name of the action method</param>
        /// <returns>String with replaced values</returns>
        private static string ReplaceTokens(string template, string controllerName, string actionName)
        {
            // In a future enhancement, we may allow escaping tokens with a "[[" to have feature parity with AspNet routing.
            return template
                // Strip "Controller" suffix from controller name if needed
                .Replace("[controller]", controllerName.EndsWith("Controller") ? controllerName[..^10] : controllerName)
                .Replace("[action]", actionName);
        }

        /// <summary>
        /// Route precedence algorithm. We collect all the routes and sort them from most specific to less specific. The
        /// specificity of a route is given by the specificity of its segments and the position of those segments in the route.
        /// * A literal segment is more specific than a parameter segment.
        /// * A parameter segment with more constraints is more specific than one with fewer constraints
        /// * Segment earlier in the route are evaluated before segments later in the route. For example: /Literal is
        /// more specific than /Parameter /Route/With/{parameter} is more specific than /{multiple}/With/{parameters}
        /// /Product/{id:int} is more specific than /Product/{id}
        ///
        /// Routes can be ambiguous if: They are composed of literals and those literals have the same values (case
        /// insensitive) They are composed of a mix of literals and parameters, in the same relative order and the
        /// literals have the same values. For example:
        /// * /literal and /Literal /{parameter}/literal and /{something}/literal /{parameter:constraint}/literal and /{something:constraint}/literal
        ///
        /// To calculate the precedence we sort the list of routes as follows:
        /// * Shorter routes go first.
        /// * A literal wins over a parameter in precedence.
        /// * For literals with different values (case insensitive) we choose the lexical order
        /// * For parameters with different numbers of constraints, the one with more wins If we get to the end of the
        /// comparison routing we've detected an ambiguous pair of routes.
        /// * Catch-all routes go last
        /// </summary>
        internal static int RouteComparison(MqttRoute x, MqttRoute y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            var xTemplate = x.Template;
            var yTemplate = y.Template;

            var xCount = xTemplate.Segments.Count;
            var yCount = yTemplate.Segments.Count;

            if (xCount == 0 && yCount == 0)
            {
                return 0;
            }

            if (xCount == 0 || yCount == 0)
            {
                return xCount < yCount ? -1 : 1;
            }

            if (xCount != yCount)
            {
                if (!xTemplate.Segments[^1].IsCatchAll && yTemplate.Segments[^1].IsCatchAll)
                {
                    return -1;
                }

                if (xTemplate.Segments[^1].IsCatchAll && !yTemplate.Segments[^1].IsCatchAll)
                {
                    return 1;
                }

                return xCount < yCount ? -1 : 1;
            }

            for (var i = 0; i < xTemplate.Segments.Count; i++)
            {
                var xSegment = xTemplate.Segments[i];
                var ySegment = yTemplate.Segments[i];

                if (!xSegment.IsCatchAll && ySegment.IsCatchAll)
                {
                    return -1;
                }

                if (xSegment.IsCatchAll && !ySegment.IsCatchAll)
                {
                    return 1;
                }

                switch (xSegment.IsParameter)
                {
                    case false when ySegment.IsParameter:
                        return -1;
                    case true when !ySegment.IsParameter:
                        return 1;
                    // Always favor non-optional parameters over optional ones
                    case true when !xSegment.IsOptional && ySegment.IsOptional:
                        return -1;
                    case true when xSegment.IsOptional && !ySegment.IsOptional:
                        return 1;
                    case true when xSegment.Constraints.Length > ySegment.Constraints.Length:
                        return -1;
                    case true when xSegment.Constraints.Length < ySegment.Constraints.Length:
                        return 1;
                    case true:
                        break;
                    default:
                    {
                        var comparison = string.Compare(xSegment.Value, ySegment.Value, StringComparison.OrdinalIgnoreCase);

                        if (comparison != 0)
                        {
                            return comparison;
                        }

                        break;
                    }
                }
            }

            throw new InvalidOperationException($@"The following routes are ambiguous:
'{x.Template.TemplateText}' in '{x.Handler.DeclaringType.FullName}.{x.Handler.Name}'
'{y.Template.TemplateText}' in '{y.Handler.DeclaringType.FullName}.{y.Handler.Name}'
");
        }

        private readonly struct Key : IEquatable<Key>
        {
            public readonly Assembly[] Assemblies;

            public Key(Assembly[] assemblies)
            {
                Assemblies = assemblies;
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public bool Equals(Key other)
            {
                if (Assemblies == null && other.Assemblies == null)
                {
                    return true;
                }

                if (Assemblies == null || other.Assemblies == null)
                {
                    return false;
                }

                if (Assemblies.Length != other.Assemblies.Length)
                {
                    return false;
                }

                return !Assemblies.Where((t, i) => !t.Equals(other.Assemblies[i])).Any();
            }

            public override int GetHashCode()
            {
                var hash = new HashCode();

                if (Assemblies == null) return hash.ToHashCode();
                foreach (var t in Assemblies)
                {
                    hash.Add(t);
                }

                return hash.ToHashCode();
            }
        }
    }
}