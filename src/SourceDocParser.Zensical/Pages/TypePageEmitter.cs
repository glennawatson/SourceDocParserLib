// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Zensical;

/// <summary>
/// Renders an ApiType as a Zensical-flavoured Markdown page.
/// </summary>
/// <remarks>
/// Cross-refs render as <c>[text][uid]</c> links for Zensical's autorefs plugin.
/// </remarks>
public static class TypePageEmitter
{
    /// <summary>
    /// Default output filename suffix.
    /// </summary>
    public const string FileExtension = ".md";

    /// <summary>
    /// Number of distinct values in <see cref="ApiMemberKind"/>.
    /// </summary>
    /// <remarks>
    /// Used to pre-size the kind-grouping dictionary.
    /// </remarks>
    private const int ApiMemberKindCount = 7;

    /// <summary>
    /// Initial StringBuilder capacity for a rendered page.
    /// </summary>
    private const int InitialPageCapacity = 4096;

    /// <summary>
    /// Initial StringBuilder capacity for the modifier list.
    /// </summary>
    private const int InitialModifierCapacity = 32;

    /// <summary>
    /// Maximum length of a member-table summary before truncation.
    /// </summary>
    private const int SummaryMaxLength = 200;

    /// <summary>
    /// Renders and writes the page to disk under the supplied output root.
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    public static void RenderToFile(ApiType type, string outputRoot) =>
        RenderToFile(type, outputRoot, ZensicalEmitterOptions.Default);

    /// <summary>
    /// Renders and writes the page to disk under <paramref name="outputRoot"/>,
    /// honouring per-package routing rules from <paramref name="options"/>.
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    public static void RenderToFile(ApiType type, string outputRoot, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var relativePath = PathFor(type, options);
        var fullPath = Path.Combine(outputRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Render(type, options));
    }

    /// <summary>
    /// Renders the supplied ApiType into a Markdown string with
    /// the legacy default cross-link routing (autoref UID
    /// everywhere, no Microsoft Learn redirects).
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <returns>The rendered Markdown string.</returns>
    public static string Render(ApiType type) => Render(type, ZensicalEmitterOptions.Default);

    /// <summary>
    /// Renders the supplied ApiType into a Markdown string,
    /// honouring the cross-link routing rules in <paramref name="options"/>
    /// (BCL types redirect to Microsoft Learn).
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The rendered Markdown string.</returns>
    public static string Render(ApiType type, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        var heading = RenderHeading(type);
        var fullDisplayName = ZensicalEmitterHelpers.FormatDisplayTypeName(type.FullName, type.Arity);
        var summary = type.Documentation.Summary is [_, ..] documentedSummary
            ? documentedSummary
            : "_No description provided._";
        var inheritedNote = type.Documentation.InheritedFrom is { Length: > 0 } inheritedFrom
            ? $"\n!!! note \"Inherited documentation\"\n    These docs were inherited from `{inheritedFrom}`.\n\n"
            : string.Empty;
        var sourceLink = type.SourceUrl is { Length: > 0 } sourceUrl
            ? $"\n[:material-source-branch: View source]({sourceUrl})\n"
            : string.Empty;
        var modifiers = JoinModifiers(type);

        var sb = new StringBuilder($"""
            # {heading}

            !!! info "Defined in"
                Namespace: `{(type.Namespace is [_, ..] ns ? ns : "(global)")}`
                Assembly: `{type.AssemblyName}.dll`
                Full name: `{MarkdownEscape(fullDisplayName)}`
                Modifiers: `{modifiers}`

            ## Summary
            {inheritedNote}{sourceLink}
            {summary}

            """);

        AppendAppliesTo(sb, type.AppliesTo);
        AppendHierarchy(sb, type, options);
        AppendUnionCases(sb, type, options);

        if (type.Documentation.Remarks is [_, ..] remarks)
        {
            sb.Append($"""

                ## Remarks

                {remarks}

                """);
        }

        AppendExamples(sb, type.Documentation.Examples);
        AppendMembers(sb, type);
        AppendEnumValues(sb, type);
        AppendDelegateSignature(sb, type);

        return sb.ToString();
    }

    /// <summary>
    /// Returns a relative file path for the type's page (legacy
    /// flat-namespace layout — no per-package folder routing).
    /// </summary>
    /// <param name="type">The type whose page path to compute.</param>
    /// <returns>The relative file path for the type's page.</returns>
    public static string PathFor(ApiType type) => PathFor(type, ZensicalEmitterOptions.Default);

    /// <summary>
    /// Returns a relative file path for the type's page, prefixed by
    /// the package folder when <paramref name="options"/> declares a
    /// matching <see cref="PackageRoutingRule"/>.
    /// </summary>
    /// <param name="type">The type whose page path to compute.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The relative file path for the type's page.</returns>
    public static string PathFor(ApiType type, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        var basePath = ZensicalEmitterHelpers.BuildTypePath(type.Namespace, type.Name, type.Arity, FileExtension);
        var packageFolder = PackageRouter.ResolveFolder(type.AssemblyName, options.PackageRouting);
        return packageFolder is null ? basePath : Path.Combine(packageFolder, basePath);
    }

    /// <summary>
    /// Renders the enum value table for an <see cref="ApiEnumType"/>.
    /// Skipped for any other kind. The values come straight off the
    /// structured <see cref="ApiEnumType.Values"/> list — no per-value
    /// markdown page is produced (those would explode the file count
    /// for icon-font enums).
    /// </summary>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="type">The type whose enum values to emit.</param>
    private static void AppendEnumValues(StringBuilder sb, ApiType type)
    {
        if (type is not ApiEnumType { Values: [_, ..] } enumType)
        {
            return;
        }

        sb.Append("\n## Values\n\n| Name | Value | Description |\n| --- | --- | --- |\n");
        for (var i = 0; i < enumType.Values.Length; i++)
        {
            var value = enumType.Values[i];
            var summary = value.Documentation.Summary is [_, ..] documentedSummary
                ? documentedSummary.ReplaceLineEndings(" ")
                : string.Empty;
            sb.Append("| `").Append(value.Name)
                .Append("` | `").Append(value.Value)
                .Append("` | ").Append(summary).AppendLine(" |");
        }
    }

    /// <summary>
    /// Renders the Invoke signature for an <see cref="ApiDelegateType"/>.
    /// Skipped for any other kind. Surfaces return type, parameters,
    /// and any generic type parameters declared on the delegate.
    /// </summary>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="type">The type whose delegate signature to emit.</param>
    private static void AppendDelegateSignature(StringBuilder sb, ApiType type)
    {
        if (type is not ApiDelegateType delegateType)
        {
            return;
        }

        var invoke = delegateType.Invoke;
        sb.Append("\n## Signature\n\n```csharp\n").Append(invoke.Signature).Append("\n```\n");

        if (invoke.Parameters is [])
        {
            return;
        }

        sb.Append("\n## Parameters\n\n| Name | Type |\n| --- | --- |\n");
        for (var i = 0; i < invoke.Parameters.Length; i++)
        {
            var p = invoke.Parameters[i];
            sb.Append("| `").Append(p.Name)
                .Append("` | `").Append(p.Type.DisplayName).AppendLine("` |");
        }
    }

    /// <summary>
    /// Emits a collapsible class-hierarchy admonition.
    /// </summary>
    /// <remarks>
    /// Uses a Mermaid classDiagram to show the type's immediate hierarchy.
    /// </remarks>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="type">The type to diagram.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    private static void AppendHierarchy(StringBuilder sb, ApiType type, ZensicalEmitterOptions options)
    {
        if (type is ApiDelegateType or ApiEnumType)
        {
            return;
        }

        if (type.BaseType is null && type.Interfaces is [])
        {
            return;
        }

        var typeNode = MermaidNodeName(type.Name, type.Arity);
        var diagramBody = RenderDiagramBody(type, typeNode);
        var inheritsLine = type.BaseType is { } baseTypeRef
            ? $"**Inherits from:** {FormatReference(baseTypeRef, options)}\n\n"
            : string.Empty;
        var implementsLine = type.Interfaces is [_, ..]
            ? $"**Implements:** {FormatReferenceList(type.Interfaces, options)}\n\n"
            : string.Empty;

        sb.Append($"""

            ??? abstract "Class hierarchy"

                ```mermaid
                classDiagram
            {diagramBody}    ```

            {inheritsLine}{implementsLine}
            """);
    }

    /// <summary>
    /// Builds the body lines of the Mermaid classDiagram.
    /// </summary>
    /// <param name="type">The type whose hierarchy to diagram.</param>
    /// <param name="typeNode">The pre-formatted Mermaid node name for the type.</param>
    /// <returns>The diagram body lines.</returns>
    private static string RenderDiagramBody(ApiType type, string typeNode)
    {
        var sb = new StringBuilder(capacity: InitialModifierCapacity * 4);
        sb.Append("    class ").AppendLine(typeNode);

        if (type.BaseType is { } baseType)
        {
            var baseNode = MermaidNodeName(baseType.DisplayName);
            sb.Append("    class ").AppendLine(baseNode)
                .Append("    ").Append(baseNode).Append(" <|-- ").AppendLine(typeNode);
        }

        for (var i = 0; i < type.Interfaces.Length; i++)
        {
            var iface = type.Interfaces[i];
            var ifaceNode = MermaidNodeName(iface.DisplayName);
            sb.Append("    class ").Append(ifaceNode).AppendLine(" {")
                .AppendLine("        <<interface>>")
                .AppendLine("    }")
                .Append("    ").Append(ifaceNode).Append(" <|.. ").AppendLine(typeNode);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends the "Applies to" admonition listing every TFM the merged canonical type appears in.
    /// </summary>
    /// <remarks>
    /// Skipped when the list is empty.
    /// </remarks>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="appliesTo">The ordered TFM list.</param>
    private static void AppendAppliesTo(StringBuilder sb, string[] appliesTo)
    {
        if (appliesTo is [])
        {
            return;
        }

        var joined = JoinTfms(appliesTo);

        sb.Append($"""

            !!! tip "Applies to"
                {joined}

            """);
    }

    /// <summary>
    /// Joins the TFM list into a comma-separated string of inline-code spans.
    /// </summary>
    /// <param name="appliesTo">The TFMs to join.</param>
    /// <returns>A comma-separated list of formatted TFMs.</returns>
    private static string JoinTfms(string[] appliesTo)
    {
        var sb = new StringBuilder(capacity: appliesTo.Length * InitialModifierCapacity);
        for (var i = 0; i < appliesTo.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append('`').Append(appliesTo[i]).Append('`');
        }

        return sb.ToString();
    }

    /// <summary>
    /// For C# 15+ union types, renders a "Cases" section.
    /// </summary>
    /// <remarks>
    /// Includes a Markdown table and a Mermaid diagram showing the union shape.
    /// </remarks>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="type">The type to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    private static void AppendUnionCases(StringBuilder sb, ApiType type, ZensicalEmitterOptions options)
    {
        if (type is not ApiUnionType { Cases: [_, ..] } union)
        {
            return;
        }

        var unionNode = MermaidNodeName(union.Name, union.Arity);
        var diagramBody = RenderUnionDiagramBody(union.Cases, unionNode);
        var caseRows = RenderUnionCaseRows(union.Cases, options);

        sb.Append($"""

            ## Cases

            {caseRows}

            ??? abstract "Union shape"

                ```mermaid
                classDiagram
            {diagramBody}    ```

            """);
    }

    /// <summary>
    /// Renders the body of the union Mermaid diagram.
    /// </summary>
    /// <remarks>
    /// Each case is a node with a composition arrow from the union.
    /// </remarks>
    /// <param name="cases">The case type references.</param>
    /// <param name="unionNode">The pre-formatted Mermaid node name for the union.</param>
    /// <returns>The union diagram body lines.</returns>
    private static string RenderUnionDiagramBody(ApiTypeReference[] cases, string unionNode)
    {
        var sb = new StringBuilder(capacity: cases.Length * InitialModifierCapacity);
        sb.Append("    class ").Append(unionNode).AppendLine(" {")
            .AppendLine("        <<union>>")
            .AppendLine("    }");

        for (var i = 0; i < cases.Length; i++)
        {
            var caseRef = cases[i];
            var caseNode = MermaidNodeName(caseRef.DisplayName);
            sb.Append("    class ").AppendLine(caseNode)
                .Append("    ").Append(unionNode).Append(" o-- ").AppendLine(caseNode);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the union-cases table.
    /// </summary>
    /// <param name="cases">The case type references.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The Markdown table rows for the union cases.</returns>
    private static string RenderUnionCaseRows(ApiTypeReference[] cases, ZensicalEmitterOptions options)
    {
        var sb = new StringBuilder(capacity: cases.Length * InitialPageCapacity / 16);
        sb.Append("| Case | Description |\n| ---- | ----------- |\n");

        for (var i = 0; i < cases.Length; i++)
        {
            sb.Append("| ").Append(FormatReference(cases[i], options)).AppendLine(" |  |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Joins a list of type references into a comma-separated Markdown string.
    /// </summary>
    /// <param name="references">The references to format.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>A comma-separated list of formatted references.</returns>
    private static string FormatReferenceList(ApiTypeReference[] references, ZensicalEmitterOptions options)
    {
        var sb = new StringBuilder(capacity: references.Length * InitialModifierCapacity);
        for (var i = 0; i < references.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(FormatReference(references[i], options));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders an <see cref="ApiTypeReference"/> as a Markdown string —
    /// autoref key for primary-package types, Microsoft Learn URL
    /// for BCL types, inline code as the final fallback.
    /// </summary>
    /// <param name="reference">The reference to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The formatted reference string.</returns>
    private static string FormatReference(ApiTypeReference reference, ZensicalEmitterOptions options) =>
        CrossLinkRouter.Format(reference, options);

    /// <summary>
    /// Builds a Mermaid-safe node name from a type display name.
    /// </summary>
    /// <remarks>
    /// Substitutes angle brackets with tildes and strips namespaces.
    /// </remarks>
    /// <param name="displayName">The type display name.</param>
    /// <returns>A Mermaid-safe node name.</returns>
    private static string MermaidNodeName(string displayName)
    {
        // Drop namespace prefix if any - MinimallyQualifiedFormat is
        // already terse but can include qualifiers in some cases.
        var name = displayName;
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0 && !name.AsSpan(lastDot).StartsWith(".<"))
        {
            name = name[(lastDot + 1)..];
        }

        return name.Replace('<', '~').Replace('>', '~');
    }

    /// <summary>
    /// Mermaid node name for the type being documented.
    /// </summary>
    /// <param name="name">The simple type name.</param>
    /// <param name="arity">The generic arity.</param>
    /// <returns>A Mermaid-safe node name including generic placeholders.</returns>
    private static string MermaidNodeName(string name, int arity) =>
        ZensicalEmitterHelpers.FormatMermaidTypeName(name, arity);

    /// <summary>
    /// Builds the page heading text.
    /// </summary>
    /// <param name="type">The type whose heading to format.</param>
    /// <returns>The formatted heading text.</returns>
    private static string RenderHeading(ApiType type)
    {
        var kindLabel = type switch
        {
            ApiObjectType { Kind: ApiObjectKind.Class } => "class",
            ApiObjectType { Kind: ApiObjectKind.Struct } => "struct",
            ApiObjectType { Kind: ApiObjectKind.Interface } => "interface",
            ApiObjectType { Kind: ApiObjectKind.Record } => "record",
            ApiObjectType { Kind: ApiObjectKind.RecordStruct } => "record struct",
            ApiEnumType => "enum",
            ApiDelegateType => "delegate",
            ApiUnionType => "union",
            _ => "type",
        };

        return $"{ZensicalEmitterHelpers.FormatDisplayTypeName(type.Name, type.Arity)} {kindLabel}";
    }

    /// <summary>
    /// Appends an Examples section containing one fenced block per example.
    /// </summary>
    /// <remarks>
    /// Skipped when the type has no examples.
    /// </remarks>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="examples">The example XML fragments.</param>
    private static void AppendExamples(StringBuilder sb, string[] examples)
    {
        if (examples.Length == 0)
        {
            return;
        }

        sb.Append("""

            ## Examples

            """);

        for (var i = 0; i < examples.Length; i++)
        {
            sb.AppendLine().AppendLine(examples[i]);
        }
    }

    /// <summary>
    /// Appends Members sections grouped by kind.
    /// </summary>
    /// <remarks>
    /// Follows conventional .NET docs order.
    /// </remarks>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="type">The type whose documented members to emit.</param>
    private static void AppendMembers(StringBuilder sb, ApiType type)
    {
        var members = type switch
        {
            ApiObjectType o => o.Members,
            ApiUnionType u => u.Members,
            _ => null,
        };

        if (members is not { Length: > 0 })
        {
            return;
        }

        // Capacity = number of ApiMemberKind values; a type rarely
        // uses every kind, but it's a tight upper bound and avoids
        // any growth.
        var byKind = new Dictionary<ApiMemberKind, List<ApiMember>>(capacity: ApiMemberKindCount);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!byKind.TryGetValue(member.Kind, out var bucket))
            {
                bucket = [];
                byKind[member.Kind] = bucket;
            }

            bucket.Add(member);
        }

        AppendMemberSection(sb, "Constructors", byKind, ApiMemberKind.Constructor, type);
        AppendMemberSection(sb, "Properties", byKind, ApiMemberKind.Property, type);
        AppendMemberSection(sb, "Fields", byKind, ApiMemberKind.Field, type);
        AppendMemberSection(sb, "Methods", byKind, ApiMemberKind.Method, type);
        AppendMemberSection(sb, "Operators", byKind, ApiMemberKind.Operator, type);
        AppendMemberSection(sb, "Events", byKind, ApiMemberKind.Event, type);
    }

    /// <summary>
    /// Writes a single members table for one kind.
    /// </summary>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="title">The section heading text.</param>
    /// <param name="byKind">The pre-grouped members lookup.</param>
    /// <param name="kind">The kind to emit.</param>
    /// <param name="containingType">The type the members belong to.</param>
    private static void AppendMemberSection(
        StringBuilder sb,
        string title,
        Dictionary<ApiMemberKind, List<ApiMember>> byKind,
        ApiMemberKind kind,
        ApiType containingType)
    {
        if (!byKind.TryGetValue(kind, out var entries) || entries.Count == 0)
        {
            return;
        }

        // Group entries by name so the table shows one row per overload
        // group with a link to the dedicated overload page; preserves
        // declaration order via foreach over the original list.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var typeFolder = TypeFolderName(containingType);

        sb.Append($"""

                    ## {title}

                    | Name | Summary |
                    | ---- | ------- |
                    """)
            .AppendLine();

        for (var i = 0; i < entries.Count; i++)
        {
            var member = entries[i];
            if (!seen.Add(member.Name))
            {
                continue;
            }

            var name = MarkdownEscape(member.Name);
            var staticPrefix = member.IsStatic ? "_static_ " : string.Empty;
            var memberFile = SanitiseForFilename(member.Name) + FileExtension;
            var summary = TableEscape(OneLineSummary(member.Documentation.Summary));
            sb.Append("| ").Append(staticPrefix)
              .Append('[').Append(name).Append("](").Append(typeFolder).Append('/').Append(memberFile).Append(')')
              .Append(" | ").Append(summary).AppendLine(" |");
        }
    }

    /// <summary>
    /// Returns the folder name a type's member pages live in.
    /// </summary>
    /// <param name="type">The type to compute the folder for.</param>
    /// <returns>The folder name for the type's member pages.</returns>
    private static string TypeFolderName(ApiType type) => type.Arity > 0
        ? ZensicalEmitterHelpers.FormatPathTypeName(type.Name, type.Arity)
        : type.Name;

    /// <summary>
    /// Strips unsafe characters from a member name for use in a filename.
    /// </summary>
    /// <param name="name">The raw member name.</param>
    /// <returns>A sanitised filename-safe string.</returns>
    private static string SanitiseForFilename(string name) => ZensicalEmitterHelpers.SanitiseForFilename(name);

    /// <summary>
    /// Joins type-level modifiers into a space-separated string.
    /// </summary>
    /// <param name="type">The type whose modifiers to format.</param>
    /// <returns>A space-separated string of modifiers.</returns>
    private static string JoinModifiers(ApiType type) => type switch
    {
        { IsStatic: true } => "public static",
        { IsAbstract: true, IsSealed: true } => "public abstract sealed",
        { IsAbstract: true } => "public abstract",
        { IsSealed: true } => "public sealed",
        _ => "public",
    };

    /// <summary>
    /// Returns a one-line summary suitable for a member table.
    /// </summary>
    /// <remarks>
    /// Truncates at a word boundary near <see cref="SummaryMaxLength"/>.
    /// </remarks>
    /// <param name="summary">The markdown summary text.</param>
    /// <returns>A truncated, one-line summary.</returns>
    private static string OneLineSummary(string summary)
    {
        if (summary is not [_, ..])
        {
            return string.Empty;
        }

        var trimmed = summary.AsSpan().Trim();
        var paragraphBreak = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
        var firstParagraph = paragraphBreak >= 0 ? trimmed[..paragraphBreak] : trimmed;

        // Flatten any remaining single newlines to spaces so the
        // result fits in a single table cell.
        var oneLine = firstParagraph.ToString()
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        if (oneLine.Length <= SummaryMaxLength)
        {
            return oneLine;
        }

        // Cut at the last word boundary that keeps us under the limit;
        // fall back to a hard cut if there's no space in range.
        var lastSpace = oneLine.LastIndexOf(' ', SummaryMaxLength - 1);
        return lastSpace > SummaryMaxLength / 2
            ? oneLine[..lastSpace] + "..."
            : oneLine[..SummaryMaxLength] + "...";
    }

    /// <summary>
    /// Escapes Markdown metacharacters in inline text.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped Markdown text.</returns>
    private static string MarkdownEscape(string text) =>
        text.IndexOf('|') < 0
            ? text
            : text.Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>
    /// Escapes pipes and replaces newlines for use in a table cell.
    /// </summary>
    /// <param name="text">The cell content.</param>
    /// <returns>The escaped table cell content.</returns>
    private static string TableEscape(string text) =>
        text.AsSpan().IndexOfAny(['|', '\n', '\r']) < 0
            ? text
            : text.Replace("|", "\\|", StringComparison.Ordinal)
                .Replace('\n', ' ')
                .Replace('\r', ' ');
}
