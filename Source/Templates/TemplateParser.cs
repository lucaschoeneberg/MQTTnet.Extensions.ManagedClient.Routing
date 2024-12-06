// Copyright (c) .NET Foundation. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt
// in the project root for license information.

// Modifications Copyright (c) Atlas Lift Tech Inc.

using System;
using System.Collections.Generic;

namespace MQTTnet.Extensions.ManagedClient.Routing.Templates
{
    internal class TemplateParser
    {
        public static readonly char[] InvalidParameterNameCharacters = { '{', '}', '=', '.' };
        private const string InvalidTemplateMessage = "Invalid template '{0}'. {1}";

        internal static RouteTemplate ParseTemplate(string template)
        {
            var originalTemplate = template;
            template = template.Trim('/');
            List<TemplateSegment> templateSegments = new();

            if (template == string.Empty)
            {
                return new RouteTemplate(template, templateSegments);
            }

            var segments = template.Split('/');
            foreach (var segment in segments)
            {
                ValidateSegment(template, segment, originalTemplate, templateSegments);
            }

            ValidateSegmentsOrder(template, templateSegments);
            return new RouteTemplate(template, templateSegments);
        }

        private static void ValidateSegment(string template, string segment, string originalTemplate,
            List<TemplateSegment> templateSegments)
        {
            if (string.IsNullOrEmpty(segment))
            {
                throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                    "Empty segments are not allowed."));
            }

            if (segment[0] != '{')
            {
                ValidateLiteralSegment(template, segment, originalTemplate, templateSegments);
            }
            else
            {
                ValidateParameterSegment(template, segment, originalTemplate, templateSegments);
            }
        }

        private static void ValidateLiteralSegment(string template, string segment, string originalTemplate,
            List<TemplateSegment> templateSegments)
        {
            if (segment[^1] == '}')
            {
                throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                    $"Missing '{{' in parameter segment '{segment}'."));
            }

            templateSegments.Add(new TemplateSegment(originalTemplate, segment, isParameter: false));
        }

        private static void ValidateParameterSegment(string template, string segment, string originalTemplate,
            List<TemplateSegment> templateSegments)
        {
            if (segment[^1] != '}')
            {
                throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                    $"Missing '}}' in parameter segment '{segment}'."));
            }

            if (segment.Length < 3)
            {
                throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                    $"Empty parameter name in segment '{segment}' is not allowed."));
            }

            int invalidCharacterIndex = segment.IndexOfAny(InvalidParameterNameCharacters, 1, segment.Length - 2);
            if (invalidCharacterIndex != -1)
            {
                throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                    $"The character '{segment[invalidCharacterIndex]}' in parameter segment '{segment}' is not allowed."));
            }

            templateSegments.Add(new TemplateSegment(originalTemplate, segment[1..^1], isParameter: true));
        }

        private static void ValidateSegmentsOrder(string template, List<TemplateSegment> templateSegments)
        {
            for (int i = 0; i < templateSegments.Count; i++)
            {
                TemplateSegment currentSegment = templateSegments[i];
                if (currentSegment.IsCatchAll && i != templateSegments.Count - 1)
                {
                    throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                        "A catch-all parameter can only appear as the last segment of the route template."));
                }

                if (!currentSegment.IsParameter) continue;

                for (int j = i + 1; j < templateSegments.Count; j++)
                {
                    TemplateSegment nextSegment = templateSegments[j];
                    if (currentSegment.IsOptional && !nextSegment.IsOptional)
                    {
                        throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                            "Non-optional parameters or literal routes cannot appear after optional parameters."));
                    }

                    if (string.Equals(currentSegment.Value, nextSegment.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(string.Format(InvalidTemplateMessage, template,
                            $"The parameter '{currentSegment}' appears multiple times."));
                    }
                }
            }
        }
    }
}