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
/// page. Tags follow the <c>kind/&lt;label&gt;</c>,
/// <c>namespace/&lt;ns&gt;</c>, <c>assembly/&lt;name&gt;</c>,
/// <c>package/&lt;folder&gt;</c> convention so the Material/Zensical
/// tags plugin renders a usable index without per-site mapping.
/// The package tag is only emitted when it differs from the
/// assembly tag (i.e. a routing rule renamed it).
/// </summary>
internal static class PageFrontmatter
{
    /// <summary>
    /// Renders the type-page frontmatter block for <paramref name="type"/>,
    /// terminated by the closing <c>---</c> and a blank line.
    /// </summary>
    /// <param name="type">The type whose page is about to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The frontmatter block.</returns>
    public static string ForType(ApiType type, ZensicalEmitterOptions options)
    {
        var assembly = type.AssemblyName;
        var package = PackageRouter.ResolveFolder(assembly, options.PackageRouting) ?? assembly;
        var kind = KindLabel(type);
        var ns = type.Namespace is [_, ..] ? type.Namespace : "(global)";

        return BuildBlock(kind: kind, ns: ns, assembly: assembly, package: package, isObsolete: type.IsObsolete);
    }

    /// <summary>
    /// Renders the member-page frontmatter block, scoping the kind tag
    /// to the member's <see cref="ApiMemberKind"/>.
    /// </summary>
    /// <param name="containingType">The declaring type.</param>
    /// <param name="member">The first overload in the group; supplies kind and obsolete state.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The frontmatter block.</returns>
    public static string ForMember(ApiType containingType, ApiMember member, ZensicalEmitterOptions options)
    {
        var assembly = containingType.AssemblyName;
        var package = PackageRouter.ResolveFolder(assembly, options.PackageRouting) ?? assembly;
        var kind = MemberKindLabel(member.Kind);
        var ns = containingType.Namespace is [_, ..] ? containingType.Namespace : "(global)";

        return BuildBlock(kind: kind, ns: ns, assembly: assembly, package: package, isObsolete: member.IsObsolete);
    }

    /// <summary>Produces the frontmatter YAML for the supplied tag values.</summary>
    /// <param name="kind">Short kind label (e.g. class, struct, method).</param>
    /// <param name="ns">Namespace (or "(global)").</param>
    /// <param name="assembly">Assembly name.</param>
    /// <param name="package">Package folder name.</param>
    /// <param name="isObsolete">Whether to emit the <c>obsolete</c> tag.</param>
    /// <returns>The frontmatter block including the closing delimiter and a trailing blank line.</returns>
    private static string BuildBlock(string kind, string ns, string assembly, string package, bool isObsolete)
    {
        var sb = new StringBuilder(capacity: 128)
            .AppendLine("---")
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

        return sb.AppendLine("---").AppendLine().ToString();
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
