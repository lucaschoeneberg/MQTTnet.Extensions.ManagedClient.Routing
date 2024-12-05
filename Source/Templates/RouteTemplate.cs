// Copyright (c) .NET Foundation. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt
// in the project root for license information. // Modifications Copyright (c) Atlas Lift Tech Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MQTTnet.Extensions.ManagedClient.Routing.Templates
{
    [DebuggerDisplay("{TemplateText}")]
    internal class RouteTemplate : IEquatable<RouteTemplate>
    {
        public RouteTemplate(string templateText, List<TemplateSegment> segments)
        {
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            TemplateText = templateText;
            OptionalSegmentsCount = CalculateOptionalSegments(segments);
            ContainsCatchAllSegment = segments.Any(template => template.IsCatchAll);
        }

        public string TemplateText { get; }
        public IList<TemplateSegment> Segments { get; }
        public int OptionalSegmentsCount { get; }
        public bool ContainsCatchAllSegment { get; }

        private static int CalculateOptionalSegments(IEnumerable<TemplateSegment> segments)
        {
            return segments.Count(template => template.IsOptional);
        }

        public bool Equals(RouteTemplate other)
        {
            return other != null &&
                   string.Equals(TemplateText, other.TemplateText, StringComparison.Ordinal) &&
                   Segments.Count == other.Segments.Count &&
                   Segments.Zip(other.Segments, (a, b) => a.Equals(b)).All(equal => equal);
        }

        public override bool Equals(object obj)
        {
            if (obj is RouteTemplate otherRouteTemplate)
            {
                return Equals(otherRouteTemplate);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return TemplateText.GetHashCode();
        }
    }
}