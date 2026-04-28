// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Common;
using SourceDocParser.Model;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Filters and renders the attribute list shown above a type or
/// member signature. The walker emits every attribute Roslyn
/// surfaces -- this layer drops the compiler-emitted markers users
/// don't want to see (NullableContext, IsReadOnly, RefSafetyRules,
/// etc.) using a namespace-prefix denylist, mirroring the docfx
/// filter convention. Anything else passes through and gets
/// rendered as <c>[Name(args)]</c> in source-like form.
/// </summary>
internal static class AttributeFilter
{
    /// <summary>
    /// Renders the user-meaningful attributes from <paramref name="attributes"/>
    /// as a single inline-code line (one usage per attribute, separated
    /// by spaces). Returns the empty string when nothing survives the
    /// filter -- the caller can use that to skip the section entirely.
    /// The denylist + allowlist live in <see cref="AttributeFilterRules"/>
    /// so every emitter applies the same rule set.
    /// </summary>
    /// <param name="attributes">The full attribute list from the walker.</param>
    /// <returns>The pre-formatted markdown line, or empty when no attributes survive.</returns>
    public static string RenderInlineList(ApiAttribute[] attributes)
    {
        if (attributes is [])
        {
            return string.Empty;
        }

        var sb = new StringBuilder(capacity: attributes.Length * 32);
        var first = true;
        for (var i = 0; i < attributes.Length; i++)
        {
            var attribute = attributes[i];
            if (AttributeFilterRules.IsExcluded(attribute.Uid))
            {
                continue;
            }

            if (!first)
            {
                sb.Append(' ');
            }

            first = false;
            sb.Append('`').Append('[').Append(attribute.DisplayName);
            if (attribute.Arguments is [_, ..])
            {
                AppendArguments(sb, attribute.Arguments);
            }

            sb.Append(']').Append('`');
        }

        return sb.ToString();
    }

    /// <summary>Appends the constructor and named arguments of an attribute to <paramref name="sb"/>, wrapped in parentheses.</summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="arguments">Attribute arguments, in source order.</param>
    private static void AppendArguments(StringBuilder sb, ApiAttributeArgument[] arguments)
    {
        sb.Append('(');
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var argument = arguments[i];
            if (argument.Name is { Length: > 0 } name)
            {
                sb.Append(name).Append(" = ");
            }

            sb.Append(argument.Value);
        }

        sb.Append(')');
    }
}
