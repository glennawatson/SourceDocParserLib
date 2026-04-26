// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace SourceDocParser.Docfx;

/// <summary>
/// Fluent <see cref="StringBuilder"/> extensions that compose the docfx
/// ManagedReference YAML page top-to-bottom. Each helper returns the
/// same builder so call sites read as a single chained expression.
/// Class itself is <see langword="internal"/> — the methods are public
/// so other code in this assembly (and the test project via the
/// internal-class type) can compose pages from individual primitives
/// when targeted scenarios call for it.
/// </summary>
internal static class DocfxYamlBuilderExtensions
{
    /// <summary>
    /// Writes the type item header — uid / commentId / id / parent /
    /// children / langs / name / nameWithType / fullName / type /
    /// assemblies / namespace / summary / syntax — at indent level 0
    /// (one item, prefixed with <c>- </c>).
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Type whose item entry to write.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendTypeItem(this StringBuilder sb, ApiType type)
    {
        sb.Append("- uid: ").AppendScalar(DocfxCommentId.ToUid(type.Uid)).AppendLine()
            .AppendIfPresent("  commentId: ", DocfxCommentId.ForType(type))
            .Append("  id: ").AppendScalar(type.Name).AppendLine()
            .AppendChildren(type)
            .AppendLine("  langs:")
            .AppendLine("  - csharp")
            .Append("  name: ").AppendScalar(type.Name).AppendLine()
            .Append("  nameWithType: ").AppendScalar(type.Name).AppendLine()
            .Append("  fullName: ").AppendScalar(type.FullName).AppendLine()
            .Append("  type: ")
            .AppendLine(DocfxYamlEmitter.MemberTypeForType(type))
            .AppendLine("  assemblies:")
            .Append("  - ").AppendScalar(type.AssemblyName).AppendLine()
            .AppendNamespace(type.Namespace)
            .AppendBlockScalar("  summary: ", type.Documentation.Summary)
            .AppendBlockScalar("  remarks: ", type.Documentation.Remarks)
            .AppendInheritance(type)
            .AppendKindSpecificSyntax(type);
        return sb;
    }

    /// <summary>
    /// Writes one item entry per member on the type. Enums and delegates
    /// have no member items — the type page already carries the value
    /// list / Invoke signature inline.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Type whose members to enumerate.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendMemberItems(this StringBuilder sb, ApiType type)
    {
        var members = MembersOf(type);
        if (members is null)
        {
            return sb;
        }

        for (var i = 0; i < members.Count; i++)
        {
            sb.AppendMemberItem(type, members[i]);
        }

        return sb;
    }

    /// <summary>
    /// Writes the page-level <c>references:</c> list. No-op when the
    /// list is empty.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="references">Distinct reference list.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendPageReferences(this StringBuilder sb, List<ApiTypeReference> references)
    {
        if (references is [])
        {
            return sb;
        }

        sb.Append("references:\n");
        for (var i = 0; i < references.Count; i++)
        {
            sb.AppendReference(references[i]);
        }

        return sb;
    }

    /// <summary>
    /// Appends a YAML scalar — wraps the value in double quotes when it
    /// could otherwise be misparsed (leading colon, leading dash, leading
    /// whitespace, special tokens like <c>true/false/null</c>, or any
    /// embedded character that needs escaping). Hot path is a single
    /// pass over the string; the common no-escape case returns straight
    /// to the inline append.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="value">Scalar value to encode.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendScalar(this StringBuilder sb, string value)
    {
        if (value is [])
        {
            return sb.Append("''");
        }

        return NeedsQuoting(value) ? sb.AppendQuotedScalar(value) : sb.Append(value);
    }

    /// <summary>
    /// Writes the <c>children:</c> list — UIDs of every documented
    /// member on the type. Skipped when the type has no members
    /// (enum/delegate or empty class).
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Type whose children to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendChildren(this StringBuilder sb, ApiType type)
    {
        var members = MembersOf(type);
        if (members is null or [])
        {
            return sb;
        }

        sb.Append("  children:\n");
        for (var i = 0; i < members.Count; i++)
        {
            sb.AppendChild(members[i]);
        }

        return sb;
    }

    /// <summary>Writes one entry of the children list.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="member">Member whose UID to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendChild(this StringBuilder sb, ApiMember member) =>
        sb.Append("  - ").AppendScalar(DocfxCommentId.ToUid(member.Uid)).AppendLine();

    /// <summary>
    /// Writes the <c>namespace:</c> field, skipped for the global
    /// namespace.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="ns">Namespace text (empty for global).</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendNamespace(this StringBuilder sb, string ns) =>
        ns is [] ? sb : sb.Append("  namespace: ").AppendScalar(ns).AppendLine();

    /// <summary>
    /// Writes the inheritance / implements blocks. No-op for delegate
    /// and enum types where the rendered docfx page doesn't surface
    /// either field.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Type the references belong to.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendInheritance(this StringBuilder sb, ApiType type) =>
        type is ApiObjectType or ApiUnionType
            ? sb.AppendBaseType(type.BaseType).AppendImplements(type.Interfaces)
            : sb;

    /// <summary>Writes the single-entry <c>inheritance:</c> block.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="baseRef">Base-type reference, or <see langword="null"/> to skip.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendBaseType(this StringBuilder sb, ApiTypeReference? baseRef) =>
        baseRef is { Uid: var uid }
            ? sb.Append("  inheritance:\n  - ")
                .AppendScalar(uid is [_, ..] ? DocfxCommentId.ToUid(uid) : baseRef.DisplayName)
                .AppendLine()
            : sb;

    /// <summary>Writes the <c>implements:</c> block listing each interface.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="interfaces">Interface references to enumerate.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendImplements(this StringBuilder sb, List<ApiTypeReference> interfaces)
    {
        if (interfaces is [])
        {
            return sb;
        }

        sb.Append("  implements:\n");
        for (var i = 0; i < interfaces.Count; i++)
        {
            sb.AppendInterface(interfaces[i]);
        }

        return sb;
    }

    /// <summary>Writes one entry of the implements list.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="iface">Interface reference to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendInterface(this StringBuilder sb, ApiTypeReference iface) =>
        sb.Append("  - ").AppendScalar(iface is { Uid: [_, ..] uid } ? DocfxCommentId.ToUid(uid) : iface.DisplayName).AppendLine();

    /// <summary>
    /// Dispatches the type-page <c>syntax:</c> block to the right
    /// per-kind helper (enum value table, delegate signature). Object
    /// and union types have no top-level syntax block — their members
    /// carry it.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Type to render.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendKindSpecificSyntax(this StringBuilder sb, ApiType type) => type switch
    {
        ApiEnumType e => sb.AppendEnumSyntax(e),
        ApiDelegateType d => sb.AppendDelegateSyntax(d),
        _ => sb,
    };

    /// <summary>
    /// Writes the enum value table as a syntax block — each value
    /// becomes a parameter-shaped entry with id / defaultValue /
    /// description so docfx's default template renders it the same
    /// as it would for an enum produced by its native walker.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Enum type to render.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendEnumSyntax(this StringBuilder sb, ApiEnumType type)
    {
        if (type.Values is [])
        {
            return sb;
        }

        sb.Append("  syntax:\n    content: ")
            .AppendScalar($"public enum {type.Name}").AppendLine()
            .Append("    return:\n      type: ")
            .AppendScalar(type.UnderlyingType is { Uid: [_, ..] uid } ? DocfxCommentId.ToUid(uid) : type.UnderlyingType.DisplayName)
            .AppendLine()
            .Append("    parameters:\n");

        for (var i = 0; i < type.Values.Count; i++)
        {
            sb.AppendEnumValue(type.Values[i]);
        }

        return sb;
    }

    /// <summary>Writes one enum value as an id / defaultValue / description triple.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="value">Enum value to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendEnumValue(this StringBuilder sb, ApiEnumValue value) => sb
        .Append("    - id: ").AppendScalar(value.Name).AppendLine()
        .Append("      defaultValue: ").AppendScalar(value.Value).AppendLine()
        .AppendBlockScalar("      description: ", value.Documentation.Summary);

    /// <summary>
    /// Writes the delegate's Invoke signature as a syntax block —
    /// content (the formatted signature), parameters, and return type.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Delegate type to render.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendDelegateSyntax(this StringBuilder sb, ApiDelegateType type) => sb
        .Append("  syntax:\n    content: ").AppendScalar(type.Invoke.Signature).AppendLine()
        .AppendParameters(type.Invoke.Parameters, indent: "    ")
        .AppendReturnIfPresent(type.Invoke.ReturnType, indent: "    ");

    /// <summary>
    /// Writes one member entry inside the page-level <c>items:</c> list
    /// — uid / commentId / id / parent / langs / names / type /
    /// assemblies / namespace / summary / remarks / syntax.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Containing type (provides parent UID + names).</param>
    /// <param name="member">Member to render.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendMemberItem(this StringBuilder sb, ApiType type, ApiMember member)
    {
        sb.Append("- uid: ").AppendScalar(DocfxCommentId.ToUid(member.Uid)).AppendLine()
            .AppendIfPresent("  commentId: ", DocfxCommentId.ForMember(member))
            .Append("  id: ").AppendScalar(member.Name).AppendLine()
            .Append("  parent: ").AppendScalar(DocfxCommentId.ToUid(type.Uid)).AppendLine()
            .AppendLine("  langs:")
            .AppendLine("  - csharp")
            .Append("  name: ").AppendScalar(member.Name).AppendLine()
            .Append("  nameWithType: ").AppendQualifiedScalar(type.Name, '.', member.Name).AppendLine()
            .Append("  fullName: ").AppendQualifiedScalar(type.FullName, '.', member.Name).AppendLine()
            .Append("  type: ")
            .AppendLine(DocfxYamlEmitter.MemberTypeForKind(member.Kind))
            .AppendLine("  assemblies:")
            .Append("  - ").AppendScalar(type.AssemblyName).AppendLine()
            .AppendNamespace(type.Namespace)
            .AppendBlockScalar("  summary: ", member.Documentation.Summary)
            .AppendBlockScalar("  remarks: ", member.Documentation.Remarks)
            .AppendMemberSyntax(member);
        return sb;
    }

    /// <summary>
    /// Appends two scalars joined by <paramref name="separator"/> as a
    /// single YAML scalar — fast path writes the parts straight to the
    /// builder when both halves are quote-safe (the dominant case for
    /// .NET identifier names), and only allocates a joined string when
    /// either part needs escaping. Avoids the per-member
    /// <c>type.Name + "." + member.Name</c> concatenation that
    /// dominated allocations on docfx YAML pages with wide member
    /// surfaces.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="left">Left half of the qualified name.</param>
    /// <param name="separator">Joining character (typically <c>.</c>).</param>
    /// <param name="right">Right half of the qualified name.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendQualifiedScalar(this StringBuilder sb, string left, char separator, string right)
    {
        if (left.Length == 0)
        {
            return sb.AppendScalar(right);
        }

        if (right.Length == 0)
        {
            return sb.AppendScalar(left);
        }

        // Either half needing quoting forces the whole composite into
        // double quotes — we have to materialise the joined string for
        // the escape pass, but build it via string.Create so we don't
        // pay for separator.ToString() on top of the unavoidable result
        // allocation. The composite probe walks both halves in one pass
        // instead of running NeedsQuoting twice.
        if (CompositeNeedsQuoting(left, separator, right))
        {
            var joined = string.Create(
                left.Length + 1 + right.Length,
                (Left: left, Separator: separator, Right: right),
                static (dest, state) =>
                {
                    state.Left.AsSpan().CopyTo(dest);
                    dest[state.Left.Length] = state.Separator;
                    state.Right.AsSpan().CopyTo(dest[(state.Left.Length + 1)..]);
                });
            return sb.AppendScalar(joined);
        }

        return sb.Append(left).Append(separator).Append(right);
    }

    /// <summary>
    /// Writes the syntax block for a member item — content + parameters
    /// + return for methods/operators/constructors, content + return for
    /// properties, content for fields/events.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="member">Member whose syntax to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendMemberSyntax(this StringBuilder sb, ApiMember member) => sb
        .Append("  syntax:\n    content: ").AppendScalar(member.Signature).AppendLine()
        .AppendParameters(member.Parameters, indent: "    ")
        .AppendReturnIfPresent(member.ReturnType, indent: "    ");

    /// <summary>
    /// Writes the parameters list under a syntax block at the supplied
    /// indent depth. No-op for parameter-less members.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="parameters">Parameter list to render.</param>
    /// <param name="indent">Indent prefix (typically <c>"    "</c>).</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendParameters(this StringBuilder sb, List<ApiParameter> parameters, string indent)
    {
        if (parameters is [])
        {
            return sb;
        }

        sb.Append(indent).Append("parameters:\n");
        for (var i = 0; i < parameters.Count; i++)
        {
            sb.AppendParameter(parameters[i], indent);
        }

        return sb;
    }

    /// <summary>Writes one entry of the parameters list (id / type / optional defaultValue).</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="parameter">Parameter to emit.</param>
    /// <param name="indent">Indent prefix carried over from the enclosing syntax block.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendParameter(this StringBuilder sb, ApiParameter parameter, string indent) => sb
        .Append(indent).Append("- id: ").AppendScalar(parameter.Name).AppendLine()
        .Append(indent).Append("  type: ")
        .AppendScalar(parameter.Type is { Uid: [_, ..] uid } ? DocfxCommentId.ToUid(uid) : parameter.Type.DisplayName).AppendLine()
        .AppendDefaultValue(parameter.DefaultValue, indent);

    /// <summary>Writes the parameter's <c>defaultValue:</c> field when present.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="defaultValue">Default value literal, or <see langword="null"/> to skip.</param>
    /// <param name="indent">Indent prefix carried over from the parameter block.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendDefaultValue(this StringBuilder sb, string? defaultValue, string indent) =>
        defaultValue is { Length: > 0 } def
            ? sb.Append(indent).Append("  defaultValue: ").AppendScalar(def).AppendLine()
            : sb;

    /// <summary>
    /// Calls <see cref="AppendReturn"/> when <paramref name="returnType"/>
    /// is non-null. Lets the call sites stay flat.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="returnType">Optional return-type reference.</param>
    /// <param name="indent">Indent prefix.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendReturnIfPresent(this StringBuilder sb, ApiTypeReference? returnType, string indent) =>
        returnType is null ? sb : sb.AppendReturn(returnType, indent);

    /// <summary>Writes the <c>return:</c> block for a member or delegate signature.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="returnType">Return-type reference.</param>
    /// <param name="indent">Indent prefix carried over from the syntax block.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendReturn(this StringBuilder sb, ApiTypeReference returnType, string indent) => sb
        .Append(indent).Append("return:\n").Append(indent).Append("  type: ")
        .AppendScalar(returnType is { Uid: [_, ..] uid } ? DocfxCommentId.ToUid(uid) : returnType.DisplayName)
        .AppendLine();

    /// <summary>
    /// Writes one entry to the page-level <c>references:</c> list — uid,
    /// commentId, name, nameWithType, fullName.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="reference">Reference to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendReference(this StringBuilder sb, ApiTypeReference reference)
    {
        var key = reference is { Uid: [_, ..] uid } ? DocfxCommentId.ToUid(uid) : reference.DisplayName;
        var commentId = reference is { Uid: [_, ..] uid2 } ? uid2 : "T:" + reference.DisplayName;
        return sb
            .Append("- uid: ").AppendScalar(key).AppendLine()
            .Append("  commentId: ").AppendScalar(commentId).AppendLine()
            .Append("  name: ").AppendScalar(reference.DisplayName).AppendLine()
            .Append("  nameWithType: ").AppendScalar(reference.DisplayName).AppendLine()
            .Append("  fullName: ").AppendScalar(reference.DisplayName).AppendLine();
    }

    /// <summary>
    /// Writes <paramref name="prefix"/> + <paramref name="value"/> when
    /// the value is non-empty; no-op otherwise. Keeps the page terse by
    /// dropping every empty-string field.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="prefix">Key + colon prefix.</param>
    /// <param name="value">Value to write (skipped when null/empty).</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendIfPresent(this StringBuilder sb, string prefix, string? value) =>
        value is null or [] ? sb : sb.Append(prefix).AppendScalar(value).AppendLine();

    /// <summary>
    /// Writes a multi-line string as a YAML literal block (<c>|-</c>) when
    /// <paramref name="value"/> contains a newline; otherwise emits it
    /// as an inline scalar. Empty values are skipped entirely.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="prefix">Key + colon + space prefix.</param>
    /// <param name="value">Body text.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendBlockScalar(this StringBuilder sb, string prefix, string value)
    {
        if (value is [])
        {
            return sb;
        }

        return value.Contains('\n')
            ? sb.AppendLiteralBlock(prefix, value)
            : sb.Append(prefix).AppendScalar(value).AppendLine();
    }

    /// <summary>
    /// Writes a YAML literal block (<c>|-</c>) — the multi-line variant
    /// of <see cref="AppendBlockScalar"/>, hoisted out so the inline
    /// path stays branch-free.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="prefix">Key + colon + space prefix.</param>
    /// <param name="value">Body text containing at least one newline.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendLiteralBlock(this StringBuilder sb, string prefix, string value)
    {
        var prefixSpan = prefix.AsSpan();
        var indentLength = prefixSpan.IndexOfAnyExcept(' ');
        if (indentLength < 0)
        {
            indentLength = prefixSpan.Length;
        }

        var indent = new string(' ', indentLength + 2);
        var key = prefixSpan[indentLength..].TrimEnd();
        sb.Append(' ', indentLength).Append(key).Append(" |-\n");

        foreach (var line in value.AsSpan().EnumerateLines())
        {
            sb.Append(indent).Append(line).AppendLine();
        }

        return sb;
    }

    /// <summary>
    /// Writes a quoted scalar — wraps the value in double quotes and
    /// escapes embedded characters per the YAML spec.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="value">Scalar to quote.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendQuotedScalar(this StringBuilder sb, string value)
    {
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            sb.AppendQuotedChar(value[i]);
        }

        return sb.Append('"');
    }

    /// <summary>
    /// Writes one character of a quoted scalar — handles the standard
    /// YAML escape sequences (<c>\n</c> / <c>\r</c> / <c>\t</c> /
    /// <c>\"</c> / <c>\\</c>) plus a hex fallback for control
    /// characters.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="c">Character to encode.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendQuotedChar(this StringBuilder sb, char c) => c switch
    {
        '"' => sb.Append("\\\""),
        '\\' => sb.Append(@"\\"),
        '\n' => sb.Append("\\n"),
        '\r' => sb.Append("\\r"),
        '\t' => sb.Append("\\t"),
        _ when c < ' ' => sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)),
        _ => sb.Append(c),
    };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> would be
    /// misparsed by a YAML reader without quoting — first character is
    /// a reserved indicator, the value matches a boolean/null token, or
    /// any character along the way needs escaping. Public so the
    /// trigger set can be unit-tested directly without going through a
    /// full page render.
    /// </summary>
    /// <param name="value">Scalar to inspect (must be non-empty).</param>
    /// <returns><see langword="true"/> when the scalar must be quoted.</returns>
    public static bool NeedsQuoting(string value) =>
        HasReservedLeadingIndicator(value[0])
        || IsReservedYamlToken(value)
        || ScanForTerminators(value.AsSpan(), prev: '\0', next: '\0');

    /// <summary>
    /// Composite-aware variant of <see cref="NeedsQuoting"/> for the
    /// <c>left + separator + right</c> shape used by
    /// <see cref="AppendQualifiedScalar"/>. Walks both halves once with
    /// boundary-aware lookups so the cold quoted-fallback path doesn't
    /// pay for two separate scans, and skips the reserved-token check
    /// entirely (a composite with a separator in the middle can't
    /// equal a single reserved token like <c>null</c>).
    /// </summary>
    /// <param name="left">Left half of the composite (must be non-empty).</param>
    /// <param name="separator">Joining character.</param>
    /// <param name="right">Right half of the composite (must be non-empty).</param>
    /// <returns><see langword="true"/> when the composite scalar must be quoted.</returns>
    public static bool CompositeNeedsQuoting(string left, char separator, string right) =>
        HasReservedLeadingIndicator(left[0])
        || ScanForTerminators(left.AsSpan(), prev: '\0', next: separator)
        || ScanForTerminators(right.AsSpan(), prev: separator, next: '\0');

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="first"/> is one of the
    /// YAML reserved leading indicators that force quoting on a plain
    /// scalar.
    /// </summary>
    /// <param name="first">First character of the scalar.</param>
    /// <returns><see langword="true"/> when the character is reserved.</returns>
    internal static bool HasReservedLeadingIndicator(char first) =>
        first is ' ' or '\t' or '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`';

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> matches one of
    /// the YAML 1.1 boolean / null reserved tokens that must be quoted
    /// to round-trip as a string.
    /// </summary>
    /// <param name="value">Scalar to test.</param>
    /// <returns><see langword="true"/> when the scalar matches a reserved token.</returns>
    internal static bool IsReservedYamlToken(string value) =>
        value is "true" or "false" or "null" or "True" or "False" or "Null"
            or "TRUE" or "FALSE" or "NULL" or "~" or "yes" or "no";

    /// <summary>
    /// Walks <paramref name="value"/> once looking for any character
    /// that would terminate or otherwise disrupt a YAML plain scalar.
    /// <paramref name="prev"/> and <paramref name="next"/> let the
    /// scanner check the context-sensitive <c>:</c>+space and
    /// space+<c>#</c> rules across composite boundaries — pass <c>'\0'</c>
    /// when there's no boundary character to consult.
    /// </summary>
    /// <param name="value">Scalar segment to scan.</param>
    /// <param name="prev">Character immediately before the segment, or <c>'\0'</c> for "none".</param>
    /// <param name="next">Character immediately after the segment, or <c>'\0'</c> for "none".</param>
    /// <returns><see langword="true"/> when the segment contains a terminator.</returns>
    internal static bool ScanForTerminators(in ReadOnlySpan<char> value, char prev, char next)
    {
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case < ' ' or '"' or '\\':
                    return true;
                case ':':
                    {
                        var following = i == value.Length - 1 ? next : value[i + 1];
                        if (following is ' ' or '\t' or '\0')
                        {
                            return true;
                        }

                        break;
                    }

                case '#':
                    {
                        var preceding = i is 0 ? prev : value[i - 1];
                        if (preceding is ' ' or '\t')
                        {
                            return true;
                        }

                        break;
                    }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the member list a type carries, or <see langword="null"/>
    /// for kinds without a flat member surface (enums and delegates).
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <returns>The member list, or <see langword="null"/>.</returns>
    private static List<ApiMember>? MembersOf(ApiType type) => type switch
    {
        ApiObjectType o => o.Members,
        ApiUnionType u => u.Members,
        _ => null,
    };
}
