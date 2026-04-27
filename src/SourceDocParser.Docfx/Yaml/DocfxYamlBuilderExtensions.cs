// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Runtime.CompilerServices;
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
    public static StringBuilder AppendTypeItem(this StringBuilder sb, ApiType type) =>
        sb.AppendTypeItem(type, DocfxCatalogIndexes.Empty);

    /// <summary>
    /// Catalog-aware overload of <see cref="AppendTypeItem(StringBuilder, ApiType)"/>.
    /// Emits the same type item plus the <c>derivedClasses</c>,
    /// <c>inheritedMembers</c>, <c>extensionMethods</c>, and
    /// <c>seealso</c> blocks pulled from <paramref name="indexes"/> —
    /// whichever entries exist for the type. Fields are written in
    /// docfx's display order so the output diffs cleanly against
    /// <c>dotnet docfx metadata</c>.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Type whose item entry to write.</param>
    /// <param name="indexes">Pre-built catalog rollups; supply <see cref="DocfxCatalogIndexes.Empty"/> to skip the rollups.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendTypeItem(this StringBuilder sb, ApiType type, DocfxCatalogIndexes indexes)
    {
        sb.Append("- uid: ").AppendScalar(DocfxCommentId.ToUid(type.Uid)).AppendLine()
            .AppendIfPresent("  commentId: ", DocfxCommentId.ForType(type))
            .Append("  id: ").AppendScalar(type.Name).AppendLine()
            .AppendIfPresent("  parent: ", type.Namespace)
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
            .AppendDerivedClasses(indexes.GetDerived(type.Uid))
            .AppendInheritedMembers(indexes.GetInherited(type.Uid))
            .AppendExtensionMethods(indexes.GetExtensions(type.Uid))
            .AppendExtensionBlocks(type is ApiObjectType obj ? obj.ExtensionBlocks : [])
            .AppendSeealso(type.Documentation.SeeAlso)
            .AppendKindSpecificSyntax(type)
            .AppendAttributes(type.Attributes);
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

        for (var i = 0; i < members.Length; i++)
        {
            if (DocfxCompilerGenerated.IsCompilerGenerated(members[i].Name))
            {
                continue;
            }

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
    /// <param name="internalUids">UIDs of types emitted in this run; drives <c>isExternal</c> + <c>href</c>.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendPageReferences(
        this StringBuilder sb,
        ApiTypeReference[] references,
        HashSet<string> internalUids)
    {
        if (references is [])
        {
            return sb;
        }

        sb.Append("references:\n");
        for (var i = 0; i < references.Length; i++)
        {
            DocfxReferenceEnricher.AppendEnrichedReference(sb, references[i], internalUids);
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

        // Docfx alphabetises children by uid. We collect non-mangled
        // uids into a single array sized to the kept count, then sort
        // in-place — O(N log N), one heap allocation, no
        // intermediate List<T> growth. For typical N=10–50 the
        // alternatives (binary-insert into a List, SortedSet of
        // node-allocations) lose on either work or allocation count.
        var kept = CountNonCompilerGenerated(members);
        if (kept == 0)
        {
            return sb;
        }

        var uids = new string[kept];
        var cursor = 0;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (DocfxCompilerGenerated.IsCompilerGenerated(member.Name))
            {
                continue;
            }

            uids[cursor++] = DocfxCommentId.ToUid(member.Uid);
        }

        Array.Sort(uids, StringComparer.Ordinal);

        sb.Append("  children:\n");
        for (var i = 0; i < uids.Length; i++)
        {
            sb.Append("  - ").AppendScalar(uids[i]).AppendLine();
        }

        return sb;
    }

    /// <summary>Counts non-compiler-generated members so the children buffer can be sized exactly.</summary>
    /// <param name="members">Member array to scan.</param>
    /// <returns>The number of members that survive the compiler-gen filter.</returns>
    public static int CountNonCompilerGenerated(ApiMember[] members)
    {
        var kept = 0;
        for (var i = 0; i < members.Length; i++)
        {
            if (!DocfxCompilerGenerated.IsCompilerGenerated(members[i].Name))
            {
                kept++;
            }
        }

        return kept;
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
    public static StringBuilder AppendInheritance(this StringBuilder sb, ApiType type) => type switch
    {
        ApiObjectType or ApiUnionType => sb
            .AppendBaseType(type.BaseType ?? DocfxWellKnownBases.For(type))
            .AppendImplements(type.Interfaces),
        ApiEnumType or ApiDelegateType => sb.AppendBaseType(DocfxWellKnownBases.For(type)),
        _ => sb,
    };

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
    public static StringBuilder AppendImplements(this StringBuilder sb, ApiTypeReference[] interfaces)
    {
        if (interfaces is [])
        {
            return sb;
        }

        sb.Append("  implements:\n");
        for (var i = 0; i < interfaces.Length; i++)
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
    /// Writes the <c>derivedClasses:</c> block listing immediate
    /// subclasses found during the catalog pre-pass. No-op when the
    /// type has no derivers — the empty-list path is the singleton
    /// the shared <see cref="Array.Empty{T}"/> singleton, so the early
    /// return is branch-and-bail with no allocation.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="derived">Derived-class refs from <see cref="DocfxCatalogIndexes.GetDerived"/>.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendDerivedClasses(this StringBuilder sb, ApiTypeReference[] derived)
    {
        if (derived is [])
        {
            return sb;
        }

        sb.Append("  derivedClasses:\n");
        for (var i = 0; i < derived.Length; i++)
        {
            var reference = derived[i];
            sb.Append("  - ").AppendScalar(reference.Uid is [_, ..] uid ? DocfxCommentId.ToUid(uid) : reference.DisplayName).AppendLine();
        }

        return sb;
    }

    /// <summary>
    /// Writes the <c>inheritedMembers:</c> block — uids the catalog
    /// index pre-computed (one base level + the System.Object baseline
    /// for class types). No-op when the list is empty.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="inherited">Inherited member uids from <see cref="DocfxCatalogIndexes.GetInherited"/>.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendInheritedMembers(this StringBuilder sb, string[] inherited)
    {
        if (inherited is [])
        {
            return sb;
        }

        sb.Append("  inheritedMembers:\n");
        for (var i = 0; i < inherited.Length; i++)
        {
            sb.Append("  - ").AppendScalar(inherited[i]).AppendLine();
        }

        return sb;
    }

    /// <summary>
    /// Writes the <c>extensionMethods:</c> block — uids of static
    /// methods on other types whose first parameter targets this type.
    /// No-op when the list is empty.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="extensions">Extension members from <see cref="DocfxCatalogIndexes.GetExtensions"/>.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendExtensionMethods(this StringBuilder sb, ApiMember[] extensions)
    {
        if (extensions is [])
        {
            return sb;
        }

        sb.Append("  extensionMethods:\n");
        for (var i = 0; i < extensions.Length; i++)
        {
            sb.Append("  - ").AppendScalar(DocfxCommentId.ToUid(extensions[i].Uid)).AppendLine();
        }

        return sb;
    }

    /// <summary>
    /// Writes the C# 14 <c>extensionBlocks:</c> field — one entry
    /// per <see cref="ApiExtensionBlock"/> declared on the type. Each
    /// entry carries the receiver name + type uid plus the conceptual
    /// member uids declared inside the block. No-op when the type
    /// declares no extension blocks (the dominant case).
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="blocks">Extension blocks declared on the type.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendExtensionBlocks(this StringBuilder sb, ApiExtensionBlock[] blocks)
    {
        if (blocks is [])
        {
            return sb;
        }

        sb.Append("  extensionBlocks:\n");
        for (var i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            var receiverUid = block.Receiver is { Uid: [_, ..] uid }
                ? DocfxCommentId.ToUid(uid)
                : block.Receiver.DisplayName;
            sb.Append("  - receiverName: ").AppendScalar(block.ReceiverName).AppendLine()
                .Append("    receiverType: ").AppendScalar(receiverUid).AppendLine()
                .Append("    members:\n");
            for (var m = 0; m < block.Members.Length; m++)
            {
                sb.Append("    - ").AppendScalar(DocfxCommentId.ToUid(block.Members[m].Uid)).AppendLine();
            }
        }

        return sb;
    }

    /// <summary>
    /// Writes the <c>seealso:</c> block — one entry per cref in the
    /// type's documentation. Each entry carries the docfx-canonical
    /// <c>linkType: CRef</c> + <c>commentId</c> + <c>altText</c>
    /// triple. No-op when there are no seealso entries.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="seealso">SeeAlso cref strings from <see cref="ApiDocumentation.SeeAlso"/>.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendSeealso(this StringBuilder sb, string[] seealso)
    {
        if (seealso is [])
        {
            return sb;
        }

        sb.Append("  seealso:\n");
        for (var i = 0; i < seealso.Length; i++)
        {
            var cref = seealso[i];
            sb.Append("  - linkType: CRef\n")
                .Append("    commentId: ").AppendScalar(cref).AppendLine()
                .Append("    altText: ").AppendScalar(cref is [_, ':', ..] ? cref[2..] : cref).AppendLine();
        }

        return sb;
    }

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
        ApiObjectType o => sb.AppendObjectSyntax(o),
        _ => sb,
    };

    /// <summary>
    /// Writes the type-level <c>syntax:</c> block for class / struct
    /// / interface / record / record-struct types — the C# declaration
    /// line synthesised by <see cref="DocfxObjectSignature.Synthesise"/>,
    /// folded through <see cref="AppendSyntaxContent"/> so the
    /// surviving attributes prefix the signature inside a YAML
    /// <c>&gt;-</c> block.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">Object-shaped type to render.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendObjectSyntax(this StringBuilder sb, ApiObjectType type) => sb
        .Append("  syntax:\n")
        .AppendSyntaxContent(type.Attributes, DocfxObjectSignature.Synthesise(type), indent: "    ");

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

        sb.Append("  syntax:\n")
            .AppendSyntaxContent(type.Attributes, $"public enum {type.Name}", indent: "    ")
            .Append("    return:\n      type: ")
            .AppendScalar(type.UnderlyingType is { Uid: [_, ..] uid } ? DocfxCommentId.ToUid(uid) : type.UnderlyingType.DisplayName)
            .AppendLine()
            .Append("    parameters:\n");

        for (var i = 0; i < type.Values.Length; i++)
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
        .Append("  syntax:\n")
        .AppendSyntaxContent(type.Attributes, type.Invoke.Signature, indent: "    ")
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
        var unqualified = DocfxMemberDisplayName.Unqualified(member, type);
        var qualified = DocfxMemberDisplayName.Qualified(member, type);
        var fullyQualified = DocfxMemberDisplayName.FullyQualified(member, type);
        var memberId = MemberIdFor(member);

        // Field order mirrors docfx's own ManagedReference output so
        // the YAML diffs cleanly against `dotnet docfx metadata`. The
        // overload anchor sits AFTER the syntax block in docfx; an
        // earlier draft placed it after parent which produced a
        // diff-unfriendly drift.
        sb.Append("- uid: ").AppendScalar(DocfxCommentId.ToUid(member.Uid)).AppendLine()
            .AppendIfPresent("  commentId: ", DocfxCommentId.ForMember(member))
            .Append("  id: ").AppendScalar(memberId).AppendLine()
            .Append("  parent: ").AppendScalar(DocfxCommentId.ToUid(type.Uid)).AppendLine()
            .AppendLine("  langs:")
            .AppendLine("  - csharp")
            .Append("  name: ").AppendScalar(unqualified).AppendLine()
            .Append("  nameWithType: ").AppendScalar(qualified).AppendLine()
            .Append("  fullName: ").AppendScalar(fullyQualified).AppendLine()
            .Append("  type: ")
            .AppendLine(DocfxYamlEmitter.MemberTypeForKind(member.Kind))
            .AppendLine("  assemblies:")
            .Append("  - ").AppendScalar(type.AssemblyName).AppendLine()
            .AppendNamespace(type.Namespace)
            .AppendBlockScalar("  summary: ", member.Documentation.Summary)
            .AppendBlockScalar("  remarks: ", member.Documentation.Remarks)
            .AppendMemberSyntax(member)
            .Append("  overload: ").AppendScalar(DocfxMemberDisplayName.OverloadAnchor(DocfxCommentId.ToUid(member.Uid))).AppendLine()
            .AppendAttributes(member.Attributes);
        return sb;
    }

    /// <summary>
    /// Writes the docfx <c>attributes:</c> block — one entry per
    /// surviving attribute, with its UID and constructor arguments.
    /// Filters compiler-emitted markers (NullableContext, IsReadOnly,
    /// RefSafetyRules, etc.) via <see cref="DocfxAttributeFilter"/> so
    /// the generated YAML matches the shape docfx itself emits.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="attributes">Walker-emitted attribute list.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendAttributes(this StringBuilder sb, ApiAttribute[] attributes)
    {
        var filtered = DocfxAttributeFilter.Filter(attributes);
        if (filtered is [])
        {
            return sb;
        }

        sb.Append("  attributes:\n");
        for (var i = 0; i < filtered.Length; i++)
        {
            sb.AppendAttributeEntry(filtered[i]);
        }

        return sb;
    }

    /// <summary>Writes one attribute entry under the <c>attributes:</c> block.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="attribute">Attribute to render.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendAttributeEntry(this StringBuilder sb, ApiAttribute attribute)
    {
        var typeRef = attribute.Uid is [_, ':', ..] ? attribute.Uid[2..] : attribute.Uid;
        sb.Append("  - type: ").AppendScalar(typeRef).AppendLine();

        // Docfx renders the bound constructor uid (M: prefix stripped)
        // alongside the attribute type — gives the YAML enough fidelity
        // to differentiate `[Browsable]` from `[Browsable(false)]` at the
        // metadata level without parsing arguments.
        if (attribute.ConstructorUid is [_, ..] ctorUid)
        {
            var ctorScalar = ctorUid is [_, ':', ..] ? ctorUid[2..] : ctorUid;
            sb.Append("    ctor: ").AppendScalar(ctorScalar).AppendLine();
        }

        if (attribute.Arguments is [])
        {
            sb.Append("    arguments: []\n");
            return sb;
        }

        sb.Append("    arguments:\n");
        for (var i = 0; i < attribute.Arguments.Length; i++)
        {
            var argument = attribute.Arguments[i];
            sb.Append("    - ");
            if (argument.Name is { Length: > 0 } name)
            {
                sb.Append("name: ").AppendScalar(name).AppendLine().Append("      ");
            }

            sb.Append("value: ").AppendScalar(argument.Value).AppendLine();
        }

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
        if (left is [])
        {
            return sb.AppendScalar(right);
        }

        if (right is [])
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
    /// properties, content for fields/events. The content field uses a
    /// folded <c>&gt;-</c> block with the member's surviving attributes
    /// stacked above the signature line, mirroring docfx's own shape;
    /// when no attributes survive the filter, the original short-scalar
    /// path is taken so the fast path stays branch-free.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="member">Member whose syntax to emit.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendMemberSyntax(this StringBuilder sb, ApiMember member) => sb
        .Append("  syntax:\n")
        .AppendSyntaxContent(member.Attributes, member.Signature, indent: "    ")
        .AppendParameters(member.Parameters, indent: "    ")
        .AppendReturnIfPresent(member.ReturnType, indent: "    ");

    /// <summary>
    /// Writes the <c>content:</c> line of a syntax block. When
    /// <paramref name="attributes"/> survives the
    /// <see cref="DocfxAttributeFilter"/> denylist, the attributes
    /// render as <c>[Name(args)]</c> lines stacked above the signature
    /// inside a folded <c>&gt;-</c> block. The empty-attribute fast
    /// path preserves the previous short-scalar behaviour exactly —
    /// no extra allocation, no folded-block setup work.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="attributes">Walker-emitted attribute list (will be filtered).</param>
    /// <param name="signature">The C# source signature for the symbol.</param>
    /// <param name="indent">Indent prefix for the value lines (e.g. <c>"    "</c>).</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendSyntaxContent(
        this StringBuilder sb,
        ApiAttribute[] attributes,
        string signature,
        string indent)
    {
        var filtered = DocfxAttributeFilter.Filter(attributes);
        if (filtered is [])
        {
            // Fast path: no surviving attributes — keep the legacy
            // single-line scalar form so output diffs against the
            // pre-tier-1c baseline are zero on the dominant case.
            return sb.Append(indent).Append("content: ").AppendScalar(signature).AppendLine();
        }

        sb.Append(indent).AppendLine("content: >-");
        for (var i = 0; i < filtered.Length; i++)
        {
            sb.Append(indent).Append('[').Append(RenderAttributeUsage(filtered[i])).Append(']').AppendLine();
        }

        // Blank line between the attribute prefix and the signature so
        // a YAML folded scalar reader keeps them on separate output
        // lines (folded form joins adjacent lines with a space; an
        // empty separator line preserves the linebreak).
        return sb
            .AppendLine(indent)
            .Append(indent).AppendLine(signature);
    }

    /// <summary>
    /// Formats one attribute usage for the syntax-content prefix —
    /// <c>Name</c> when there are no arguments, <c>Name(arg, Named=val)</c>
    /// otherwise. Arguments come pre-formatted by the walker.
    /// </summary>
    /// <param name="attribute">Attribute to render.</param>
    /// <returns>The bracket-less usage string (caller adds the surrounding <c>[]</c>).</returns>
    public static string RenderAttributeUsage(ApiAttribute attribute)
    {
        if (attribute.Arguments is [])
        {
            return attribute.DisplayName;
        }

        var totalLength = ComputeAttributeUsageLength(attribute);
        return string.Create(totalLength, attribute, static (span, attr) =>
        {
            attr.DisplayName.AsSpan().CopyTo(span);
            var cursor = attr.DisplayName.Length;
            span[cursor++] = '(';
            for (var i = 0; i < attr.Arguments.Length; i++)
            {
                if (i > 0)
                {
                    ", ".AsSpan().CopyTo(span[cursor..]);
                    cursor += 2;
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
    /// Writes the parameters list under a syntax block at the supplied
    /// indent depth. No-op for parameter-less members.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="parameters">Parameter list to render.</param>
    /// <param name="indent">Indent prefix (typically <c>"    "</c>).</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendParameters(this StringBuilder sb, ApiParameter[] parameters, string indent)
    {
        if (parameters is [])
        {
            return sb;
        }

        sb.Append(indent).Append("parameters:\n");
        for (var i = 0; i < parameters.Length; i++)
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
    /// Returns the docfx <c>id:</c> field value for <paramref name="member"/>.
    /// Constructors render as <c>#ctor</c> (or <c>#cctor</c> for the
    /// static ctor) so docfx's xrefmap convention is preserved; every
    /// other kind passes its metadata name through. Roslyn surfaces
    /// constructor metadata as <c>.ctor</c>; the rewrite happens here
    /// so consumers see the docfx-convention form.
    /// </summary>
    /// <param name="member">Member whose id field to compute.</param>
    /// <returns>The id-field text, ready to feed through <see cref="AppendScalar"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string MemberIdFor(ApiMember member) => member.Name switch
    {
        ".ctor" => "#ctor",
        ".cctor" => "#cctor",
        _ => member.Name,
    };

    /// <summary>
    /// Returns the member list a type carries, or <see langword="null"/>
    /// for kinds without a flat member surface (enums and delegates).
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <returns>The member list, or <see langword="null"/>.</returns>
    internal static ApiMember[]? MembersOf(ApiType type) => type switch
    {
        ApiObjectType o => o.Members,
        ApiUnionType u => u.Members,
        _ => null,
    };

    /// <summary>Sums the final character count of a rendered attribute usage so <see cref="string.Create{TState}"/> allocates exactly the right span size.</summary>
    /// <param name="attribute">Attribute whose usage length to compute.</param>
    /// <returns>The total character count, including the surrounding parens and any <c>", "</c> / <c>"="</c> separators.</returns>
    internal static int ComputeAttributeUsageLength(ApiAttribute attribute)
    {
        var total = attribute.DisplayName.Length + 2;
        for (var i = 0; i < attribute.Arguments.Length; i++)
        {
            if (i > 0)
            {
                total += 2;
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
