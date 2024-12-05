// Copyright (c) .NET Foundation. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt
// in the project root for license information.

// Modifications Copyright (c) Atlas Lift Tech Inc.

using System;
using System.Collections.Generic;

namespace MQTTnet.AspNetCore.Routing
{
    internal class TemplateParser
    {
        public static readonly char[] InvalidParameterNameCharacters =
            ['{', '}', '=', '.'];

        internal static RouteTemplate ParseTemplate(string template)
        {
            var originalTemplate = template;

            template = template.Trim('/');
            var templateSegments = new List<TemplateSegment>();
            if (template == string.Empty) return new RouteTemplate(template, templateSegments);
            var segments = template.Split('/');
            templateSegments = new List<TemplateSegment>(segments.Length);
            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment))
                {
                    throw new InvalidOperationException(
                        $"Invalid template '{template}'. Empty segments are not allowed.");
                }

                if (segment[0] != '{')
                {
                    if (segment[^1] == '}')
                    {
                        throw new InvalidOperationException(
                            $"Invalid template '{template}'. Missing '{{' in parameter segment '{segment}'.");
                    }

                    templateSegments.Add(new TemplateSegment(originalTemplate, segment, isParameter: false));
                }
                else
                {
                    if (segment[^1] != '}')
                    {
                        throw new InvalidOperationException(
                            $"Invalid template '{template}'. Missing '}}' in parameter segment '{segment}'.");
                    }

                    if (segment.Length < 3)
                    {
                        throw new InvalidOperationException(
                            $"Invalid template '{template}'. Empty parameter name in segment '{segment}' is not allowed.");
                    }

                    var invalidCharacter = segment.IndexOfAny(InvalidParameterNameCharacters, 1, segment.Length - 2);

                    if (invalidCharacter != -1)
                    {
                        throw new InvalidOperationException(
                            $"Invalid template '{template}'. The character '{segment[invalidCharacter]}' in parameter segment '{segment}' is not allowed.");
                    }

                    templateSegments.Add(new TemplateSegment(originalTemplate, segment.Substring(1, segment.Length - 2), isParameter: true));
                }
            }

            for (int i = 0; i < templateSegments.Count; i++)
            {
                var currentSegment = templateSegments[i];

                if (currentSegment.IsCatchAll && i != templateSegments.Count - 1)
                {
                    throw new InvalidOperationException($"Invalid template '{template}'. A catch-all parameter can only appear as the last segment of the route template.");
                }

                if (!currentSegment.IsParameter)
                {
                    continue;
                }

                for (var j = i + 1; j < templateSegments.Count; j++)
                {
                    var nextSegment = templateSegments[j];

                    if (currentSegment.IsOptional && !nextSegment.IsOptional)
                    {
                        throw new InvalidOperationException($"Invalid template '{template}'. Non-optional parameters or literal routes cannot appear after optional parameters.");
                    }

                    if (string.Equals(currentSegment.Value, nextSegment.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Invalid template '{template}'. The parameter '{currentSegment}' appears multiple times.");
                    }
                }
            }
            return new RouteTemplate(template, templateSegments);
        }
    }
}