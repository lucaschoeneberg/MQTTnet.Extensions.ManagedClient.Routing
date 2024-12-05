// Copyright (c) .NET Foundation. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt
// in the project root for license information.

// Modifications Copyright (c) Atlas Lift Tech Inc. All rights reserved.
// Modifications Copyright (c) Mardu All rights reserved.

using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MQTTnet.Extensions.ManagedClient.Routing.Templates;

namespace MQTTnet.Extensions.ManagedClient.Routing.Routing
{
    [DebuggerDisplay("Handler = {Handler}, Template = {Template}")]
    internal class MqttRoute
    {
        private const string PathSegmentSeparator = "/";
        private static readonly StringComparer ParameterComparer = StringComparer.Ordinal;

        public MqttRoute(RouteTemplate template, MethodInfo handler, string[] unusedRouteParameterNames)
        {
            Template = template;
            UnusedRouteParameterNames = unusedRouteParameterNames;
            Handler = handler;
        }

        public RouteTemplate Template { get; }
        public string[] UnusedRouteParameterNames { get; }
        public MethodInfo Handler { get; }
        public RouteTemplate ControllerTemplate { get; internal set; }
        public bool HaveControllerParameter { get; internal set; }

        internal void Match(MqttRouteContext context)
        {
            string catchAllParameterValue = Template.ContainsCatchAllSegment &&
                                            context.Segments.Length >= Template.Segments.Count
                ? string.Join(PathSegmentSeparator, context.Segments.Skip(Template.Segments.Count - 1))
                : null;

            if (!Template.ContainsCatchAllSegment && Template.OptionalSegmentsCount == 0 &&
                Template.Segments.Count != context.Segments.Length)
            {
                return;
            }

            Dictionary<string, object> routeParameters = null;
            var numMatchingSegments = CalculateMatchingSegments(context, ref routeParameters, catchAllParameterValue);

            if (!Template.ContainsCatchAllSegment && UnusedRouteParameterNames.Length > 0)
            {
                InitializeUnusedParameters(ref routeParameters);
            }

            if (!IsValidRouteMatch(context, numMatchingSegments)) return;
            context.Parameters = routeParameters;
            context.Handler = Handler;
            context.HaveControllerParameter = HaveControllerParameter;
            context.ControllerTemplate = ControllerTemplate;
        }

        private int CalculateMatchingSegments(MqttRouteContext context, ref Dictionary<string, object> routeParameters,
            string catchAllParameterValue)
        {
            var numMatchingSegments = 0;
            for (var i = 0; i < Template.Segments.Count; i++)
            {
                var segment = Template.Segments[i];
                if (segment.IsCatchAll)
                {
                    numMatchingSegments++;
                    routeParameters ??= new Dictionary<string, object>(ParameterComparer);
                    routeParameters[segment.Value] = catchAllParameterValue;
                    break;
                }

                if (i >= context.Segments.Length ||
                    !segment.Match(i < context.Segments.Length ? context.Segments[i] : null,
                        out var matchedParameterValue))
                {
                    break;
                }

                numMatchingSegments++;
                if (!segment.IsParameter) continue;
                routeParameters ??= new Dictionary<string, object>(ParameterComparer);
                routeParameters[segment.Value] = matchedParameterValue;
            }

            return numMatchingSegments;
        }

        private void InitializeUnusedParameters(ref Dictionary<string, object> routeParameters)
        {
            routeParameters ??= new Dictionary<string, object>(ParameterComparer);
            foreach (var paramName in UnusedRouteParameterNames)
            {
                routeParameters[paramName] = null;
            }
        }

        private bool IsValidRouteMatch(MqttRouteContext context, int numMatchingSegments)
        {
            var allRouteSegmentsMatch = numMatchingSegments >= context.Segments.Length;
            var allNonOptionalSegmentsMatch =
                numMatchingSegments >= (Template.Segments.Count - Template.OptionalSegmentsCount);

            return Template.ContainsCatchAllSegment || (allRouteSegmentsMatch && allNonOptionalSegmentsMatch);
        }
    }
}