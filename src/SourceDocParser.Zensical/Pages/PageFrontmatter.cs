// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Model;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Builds the YAML frontmatter block that prefixes every emitted
/// page. Tags follow the <c>kind/{label}</c>,
/// <c>namespace/{ns}</c>, <c>assembly/{name}</c>,
/// <c>package/{folder}</c> convention so the Material/Zensical
/// tags plugin renders a usable index without per-site mapping.
/// The package tag is only emitted when it differs from the
/// assembly tag (i.e. a routing rule renamed it).
/// </summary>
internal static class PageFrontmatter
{
    /// <summary>
    /// Renders the type-page frontmatter block for <paramref name="type"/>,
    /// terminated by the closing <c>---</c>, a hidden mkdocs-autorefs
    /// anchor for the type's UID, and a blank line. The anchor lets
    /// other pages link to this type via the
    /// <c>[Name][T:Full.Type.Name]</c> autoref form.
    /// </summary>
    /// <param name="type">The type whose page is about to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The frontmatter block plus the type's UID anchor.</returns>
    public static string ForType(ApiType type, ZensicalEmitterOptions options)
    {
        var sb = new StringBuilder(capacity: 192);
        AppendForType(sb, type, options);
        return sb.ToString();
    }

    /// <summary>Same content as <see cref="ForType(ApiType, ZensicalEmitterOptions)"/> but appended to the caller's <paramref name="sb"/>.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="type">The type whose page is about to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendForType(StringBuilder sb, ApiType type, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(type);
        var assembly = type.AssemblyName;
        var package = PackageRouter.ResolveFolder(assembly, options.PackageRouting) ?? assembly;
        var kind = KindLabel(type);
        var ns = type.Namespace is [_, ..] ? type.Namespace : "(global)";

        AppendBlock(sb, kind: kind, ns: ns, assembly: assembly, package: package, isObsolete: type.IsObsolete);
        AppendUidAnchor(sb, type.Uid);
        AppendMemberUidAnchors(sb, type);
        sb.AppendLine();
        return sb;
    }

    /// <summary>
    /// Renders the member-page frontmatter block, scoping the kind tag
    /// to the member's <see cref="ApiMemberKind"/>. Followed by hidden
    /// mkdocs-autorefs anchors -- one per overload -- so cross-references
    /// to specific overload UIDs resolve to this page.
    /// </summary>
    /// <param name="containingType">The declaring type.</param>
    /// <param name="member">The first overload in the group; supplies kind and obsolete state.</param>
    /// <param name="overloads">All overloads in the group; one anchor per overload UID.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The frontmatter block plus a UID anchor per overload.</returns>
    public static string ForMember(ApiType containingType, ApiMember member, ApiMember[] overloads, ZensicalEmitterOptions options)
    {
        var sb = new StringBuilder(capacity: 192 + ((overloads?.Length ?? 0) * 32));
        AppendForMember(sb, containingType, member, overloads!, options);
        return sb.ToString();
    }

    /// <summary>Same content as <see cref="ForMember(ApiType, ApiMember, ApiMember[], ZensicalEmitterOptions)"/> but appended to the caller's <paramref name="sb"/>.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="containingType">The declaring type.</param>
    /// <param name="member">The first overload in the group; supplies kind and obsolete state.</param>
    /// <param name="overloads">All overloads in the group; one anchor per overload UID.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder AppendForMember(StringBuilder sb, ApiType containingType, ApiMember member, ApiMember[] overloads, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(containingType);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(overloads);
        var assembly = containingType.AssemblyName;
        var package = PackageRouter.ResolveFolder(assembly, options.PackageRouting) ?? assembly;
        var kind = MemberKindLabel(member.Kind);
        var ns = containingType.Namespace is [_, ..] ? containingType.Namespace : "(global)";

        AppendBlock(sb, kind: kind, ns: ns, assembly: assembly, package: package, isObsolete: member.IsObsolete);
        for (var i = 0; i < overloads.Length; i++)
        {
            AppendUidAnchor(sb, overloads[i].Uid);
        }

        sb.AppendLine();
        return sb;
    }

    /// <summary>Appends the frontmatter YAML for the supplied tag values to <paramref name="sb"/>.</summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="kind">Short kind label (e.g. class, struct, method).</param>
    /// <param name="ns">Namespace (or "(global)").</param>
    /// <param name="assembly">Assembly name.</param>
    /// <param name="package">Package folder name.</param>
    /// <param name="isObsolete">Whether to emit the <c>obsolete</c> tag.</param>
    private static void AppendBlock(StringBuilder sb, string kind, string ns, string assembly, string package, bool isObsolete)
    {
        sb.AppendLine("---")
            .AppendLine("tags:")
            .Append("  - kind/").AppendLine(kind)
            .Append("  - namespace/").AppendLine(ns)
            .Append("  - assembly/").AppendLine(assembly);

        if (!string.Equals(package, assembly, StringComparison.Ordinal))
        {
            sb.Append("  - package/").AppendLine(package);
        }

        if (isObsolete)
        {
            sb.AppendLine("  - obsolete");
        }

        sb.AppendLine("---");
    }

    /// <summary>
    /// Appends a hidden mkdocs-autorefs anchor for <paramref name="uid"/>
    /// -- the <c>[](){#UID}</c> form picks up as a cross-reference
    /// target without rendering anything visible. No-op for empty UIDs.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="uid">The UID to register as an anchor.</param>
    private static void AppendUidAnchor(StringBuilder sb, string? uid)
    {
        if (uid is not { Length: > 0 })
        {
            return;
        }

        // The anchor id MUST go through the same autoref-id transform
        // the resolver applies to its references, otherwise references
        // carrying arity-backticks (translated to hyphens by the
        // resolver) won't find the anchor (which would still spell the
        // backtick literally).
        sb.Append("[](){#").Append(UidNormaliser.ToAutorefId(uid)).AppendLine("}");
    }

    /// <summary>
    /// Appends hidden anchors for the type's contained members so cross-
    /// references to a specific member resolve to the type page when
    /// the emitter doesn't produce a separate per-overload page --
    /// enum values fall into this bucket. Object/union member UIDs
    /// already get their own anchors on the per-overload page; we
    /// don't duplicate them here.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="type">The owning type.</param>
    private static void AppendMemberUidAnchors(StringBuilder sb, ApiType type)
    {
        if (type is not ApiEnumType { Values: var values })
        {
            return;
        }

        for (var i = 0; i < values.Length; i++)
        {
            AppendUidAnchor(sb, values[i].Uid);
        }
    }

    /// <summary>Maps an <see cref="ApiType"/> to its short kind label.</summary>
    /// <param name="type">The type to label.</param>
    /// <returns>The short kind label.</returns>
    private static string KindLabel(ApiType type) => type switch
    {
        ApiObjectType { Kind: ApiObjectKind.Class } => "class",
        ApiObjectType { Kind: ApiObjectKind.Struct } => "struct",
        ApiObjectType { Kind: ApiObjectKind.Interface } => "interface",
        ApiObjectType { Kind: ApiObjectKind.Record } => "record",
        ApiObjectType { Kind: ApiObjectKind.RecordStruct } => "record-struct",
        ApiEnumType => "enum",
        ApiDelegateType => "delegate",
        ApiUnionType => "union",
        _ => "type",
    };

    /// <summary>Maps an <see cref="ApiMemberKind"/> to its short tag label.</summary>
    /// <param name="kind">The member kind.</param>
    /// <returns>The short kind label.</returns>
    private static string MemberKindLabel(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Constructor => "constructor",
        ApiMemberKind.Property => "property",
        ApiMemberKind.Field => "field",
        ApiMemberKind.Method => "method",
        ApiMemberKind.Operator => "operator",
        ApiMemberKind.Event => "event",
        ApiMemberKind.EnumValue => "enum-value",
        _ => "member",
    };
}
