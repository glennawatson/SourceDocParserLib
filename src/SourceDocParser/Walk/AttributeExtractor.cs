// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Pulls attribute metadata off any <see cref="ISymbol"/> and converts
/// it into the model's <see cref="ApiAttribute"/> shape. The walker
/// emits attributes faithfully — every attribute Roslyn surfaces is
/// returned, including compiler-emitted markers from
/// <c>System.Runtime.CompilerServices</c>. Presentation-layer
/// filtering belongs to the consuming emitter.
/// </summary>
internal static class AttributeExtractor
{
    /// <summary>The fully-qualified name of <c>System.ObsoleteAttribute</c>.</summary>
    private const string ObsoleteAttributeFullName = "System.ObsoleteAttribute";

    /// <summary>
    /// Returns the model representation of every attribute applied to
    /// <paramref name="symbol"/>, in declaration order.
    /// </summary>
    /// <param name="symbol">The symbol whose attributes to extract.</param>
    /// <returns>The attributes; an empty array when the symbol has none.</returns>
    public static ApiAttribute[] Extract(ISymbol symbol)
    {
        var attributes = symbol.GetAttributes();
        if (attributes.IsDefaultOrEmpty)
        {
            return [];
        }

        var result = new ApiAttribute[attributes.Length];
        for (var i = 0; i < attributes.Length; i++)
        {
            result[i] = Convert(attributes[i]);
        }

        return result;
    }

    /// <summary>
    /// Resolves the <c>[Obsolete]</c> marker on <paramref name="symbol"/>
    /// (if any) into a flag-plus-message pair. Returns
    /// <c>(false, null)</c> when no marker is present.
    /// </summary>
    /// <param name="symbol">The symbol to inspect.</param>
    /// <returns>Tuple of obsolete flag and optional message.</returns>
    public static (bool IsObsolete, string? Message) ResolveObsolete(ISymbol symbol)
    {
        var attributes = symbol.GetAttributes();
        for (var i = 0; i < attributes.Length; i++)
        {
            var data = attributes[i];
            if (!IsObsoleteAttribute(data.AttributeClass))
            {
                continue;
            }

            var message = data.ConstructorArguments.Length > 0
                ? data.ConstructorArguments[0].Value as string
                : null;
            return (true, string.IsNullOrEmpty(message) ? null : message);
        }

        return (false, null);
    }

    /// <summary>Tests whether <paramref name="attributeClass"/> is <c>System.ObsoleteAttribute</c>.</summary>
    /// <param name="attributeClass">The attribute class symbol; may be null when Roslyn cannot resolve it.</param>
    /// <returns>True for <c>System.ObsoleteAttribute</c>.</returns>
    private static bool IsObsoleteAttribute(INamedTypeSymbol? attributeClass) =>
        attributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))
            == ObsoleteAttributeFullName;

    /// <summary>Converts one Roslyn <see cref="AttributeData"/> into an <see cref="ApiAttribute"/>.</summary>
    /// <param name="data">The Roslyn attribute usage.</param>
    /// <returns>The model attribute, with constructor and named arguments preserved in source order.</returns>
    private static ApiAttribute Convert(AttributeData data)
    {
        var attributeClass = data.AttributeClass;
        var displayName = attributeClass is null
            ? "Attribute"
            : StripAttributeSuffix(attributeClass.Name);
        var uid = attributeClass?.GetDocumentationCommentId() ?? string.Empty;

        var positional = data.ConstructorArguments;
        var named = data.NamedArguments;
        var args = new ApiAttributeArgument[positional.Length + named.Length];

        for (var i = 0; i < positional.Length; i++)
        {
            args[i] = new ApiAttributeArgument(Name: null, Value: FormatConstant(positional[i]));
        }

        for (var i = 0; i < named.Length; i++)
        {
            var entry = named[i];
            args[positional.Length + i] = new ApiAttributeArgument(Name: entry.Key, Value: FormatConstant(entry.Value));
        }

        return new ApiAttribute(displayName, uid, args);
    }

    /// <summary>Drops the trailing <c>Attribute</c> suffix from a class name when present.</summary>
    /// <param name="name">The class name.</param>
    /// <returns>The shortened name (e.g. <c>Obsolete</c> from <c>ObsoleteAttribute</c>).</returns>
    private static string StripAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal) && name.Length > "Attribute".Length
            ? name[..^"Attribute".Length]
            : name;

    /// <summary>
    /// Formats a Roslyn <see cref="TypedConstant"/> as a source-like
    /// string suitable for inline rendering inside an attribute usage
    /// (e.g. <c>"hello"</c>, <c>true</c>, <c>typeof(Foo)</c>,
    /// <c>SomeEnum.Bar</c>).
    /// </summary>
    /// <param name="constant">The typed constant to format.</param>
    /// <returns>The source-like rendering.</returns>
    private static string FormatConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        return constant.Kind switch
        {
            TypedConstantKind.Type => constant.Value is ITypeSymbol type
                ? "typeof(" + type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + ")"
                : "typeof(?)",
            TypedConstantKind.Enum => FormatEnumConstant(constant),
            TypedConstantKind.Array => FormatArrayConstant(constant),
            _ => FormatPrimitive(constant.Value),
        };
    }

    /// <summary>Formats an enum-typed constant as <c>EnumName.MemberName</c> (best effort).</summary>
    /// <param name="constant">An enum-typed constant.</param>
    /// <returns>The formatted enum literal.</returns>
    private static string FormatEnumConstant(TypedConstant constant)
    {
        var typeName = constant.Type is INamedTypeSymbol named
            ? named.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : "?";
        return string.Create(CultureInfo.InvariantCulture, $"{typeName}.{constant.Value}");
    }

    /// <summary>Formats an array-typed constant as <c>[a, b, c]</c>.</summary>
    /// <param name="constant">An array-typed constant.</param>
    /// <returns>The formatted array literal.</returns>
    private static string FormatArrayConstant(TypedConstant constant)
    {
        var sb = new StringBuilder().Append('[');
        for (var i = 0; i < constant.Values.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(FormatConstant(constant.Values[i]));
        }

        return sb.Append(']').ToString();
    }

    /// <summary>Formats a primitive constant value with C# source conventions (string quoting, bool lowercasing).</summary>
    /// <param name="value">The boxed primitive value.</param>
    /// <returns>The formatted literal.</returns>
    private static string FormatPrimitive(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        char c => "'" + c + "'",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
