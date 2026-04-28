// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Formats one attribute usage as the <c>Name(arg, Named=val)</c>
/// string the docfx syntax-content prefix carries above each member's
/// signature. Walker provides arguments pre-formatted, so this is
/// pure layout -- exact-length precompute + single
/// <see cref="string.Create{TState}"/> fill so each rendered usage
/// allocates one string and nothing else.
/// </summary>
internal static class AttributeUsageFormatter
{
    /// <summary>The pair-separator string written between adjacent attribute arguments.</summary>
    private const string ArgumentSeparator = ", ";

    /// <summary>
    /// Formats <paramref name="attribute"/> as <c>Name</c> when there
    /// are no arguments, or <c>Name(arg, Named=val)</c> otherwise.
    /// Caller wraps the returned text in the surrounding <c>[]</c>.
    /// </summary>
    /// <param name="attribute">Attribute usage to render.</param>
    /// <returns>The bracket-less usage string.</returns>
    public static string Render(ApiAttribute attribute)
    {
        if (attribute.Arguments is [])
        {
            return attribute.DisplayName;
        }

        var totalLength = ComputeLength(attribute);
        return string.Create(totalLength, attribute, static (span, attr) =>
        {
            attr.DisplayName.AsSpan().CopyTo(span);
            var cursor = attr.DisplayName.Length;
            span[cursor++] = '(';
            for (var i = 0; i < attr.Arguments.Length; i++)
            {
                if (i > 0)
                {
                    ArgumentSeparator.AsSpan().CopyTo(span[cursor..]);
                    cursor += ArgumentSeparator.Length;
                }

                var arg = attr.Arguments[i];
                if (arg.Name is { Length: > 0 } name)
                {
                    name.AsSpan().CopyTo(span[cursor..]);
                    cursor += name.Length;
                    span[cursor++] = '=';
                }

                arg.Value.AsSpan().CopyTo(span[cursor..]);
                cursor += arg.Value.Length;
            }

            span[cursor] = ')';
        });
    }

    /// <summary>
    /// Sums the final character count of the rendered attribute usage
    /// so the <see cref="string.Create{TState}"/> allocation is exact.
    /// </summary>
    /// <param name="attribute">Attribute whose usage length to compute.</param>
    /// <returns>The total character count, including the surrounding parens and separators.</returns>
    public static int ComputeLength(ApiAttribute attribute)
    {
        var total = attribute.DisplayName.Length + 2;
        for (var i = 0; i < attribute.Arguments.Length; i++)
        {
            if (i > 0)
            {
                total += ArgumentSeparator.Length;
            }

            var arg = attribute.Arguments[i];
            if (arg.Name is { Length: > 0 } name)
            {
                total += name.Length + 1;
            }

            total += arg.Value.Length;
        }

        return total;
    }
}
