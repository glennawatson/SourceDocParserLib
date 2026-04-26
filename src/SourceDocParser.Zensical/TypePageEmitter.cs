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
internal static class TypePageEmitter
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
    public static void RenderToFile(ApiType type, string outputRoot)
    {
        var relativePath = PathFor(type);
        var fullPath = Path.Combine(outputRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Render(type));
    }

    /// <summary>
    /// Renders the supplied ApiType into a Markdown string.
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <returns>The rendered Markdown string.</returns>
    public static string Render(ApiType type)
    {
        var heading = RenderHeading(type);
        var displayName = MarkdownEscape(type.Name);
        var fullDisplayName = type.Arity > 0
            ? $"{type.FullName}<{string.Join(", ", GenericPlaceholders(type.Arity))}>"
            : type.FullName;
        var summary = type.Documentation.Summary.Length > 0
            ? type.Documentation.Summary
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
                                        Namespace: `{(type.Namespace.Length > 0 ? type.Namespace : "(global)")}`
                                        Assembly: `{type.AssemblyName}.dll`
                                        Full name: `{MarkdownEscape(fullDisplayName)}`
                                        Modifiers: `{modifiers}`

                                    ## Summary
                                    {inheritedNote}{sourceLink}
                                    {summary}

                                    """);

        AppendAppliesTo(sb, type.AppliesTo);
        AppendHierarchy(sb, type);
        AppendUnionCases(sb, type);

        if (type.Documentation.Remarks.Length > 0)
        {
            sb.Append($"""

                ## Remarks

                {type.Documentation.Remarks}

                """);
        }

        AppendExamples(sb, type.Documentation.Examples);
        AppendMembers(sb, type);
        AppendEnumValues(sb, type);
        AppendDelegateSignature(sb, type);

        _ = displayName;
        return sb.ToString();
    }

    /// <summary>
    /// Returns a relative file path for the type's page.
    /// </summary>
    /// <remarks>
    /// Generic types use curly braces for Windows safety and URL readability.
    /// </remarks>
    /// <param name="type">The type whose page path to compute.</param>
    /// <returns>The relative file path for the type's page.</returns>
    public static string PathFor(ApiType type)
    {
        var segments = type.Namespace.Length > 0
            ? type.Namespace.Split('.')
            : ["_global"];
        var name = type.Arity > 0
            ? $"{type.Name}{{{string.Join(",", GenericPlaceholders(type.Arity))}}}"
            : type.Name;

        return string.Join('/', segments) + "/" + name + FileExtension;
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
        if (type is not ApiEnumType enumType || enumType.Values.Count == 0)
        {
            return;
        }

        sb.Append("\n## Values\n\n| Name | Value | Description |\n| --- | --- | --- |\n");
        for (var i = 0; i < enumType.Values.Count; i++)
        {
            var value = enumType.Values[i];
            var summary = value.Documentation.Summary.Length > 0
                ? value.Documentation.Summary.ReplaceLineEndings(" ")
                : string.Empty;
            sb.Append("| `").Append(value.Name)
                .Append("` | `").Append(value.Value)
                .Append("` | ").Append(summary).Append(" |\n");
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

        if (invoke.Parameters.Count == 0)
        {
            return;
        }

        sb.Append("\n## Parameters\n\n| Name | Type |\n| --- | --- |\n");
        for (var i = 0; i < invoke.Parameters.Count; i++)
        {
            var p = invoke.Parameters[i];
            sb.Append("| `").Append(p.Name)
                .Append("` | `").Append(p.Type.DisplayName).Append("` |\n");
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
    private static void AppendHierarchy(StringBuilder sb, ApiType type)
    {
        if (type is ApiDelegateType or ApiEnumType)
        {
            return;
        }

        if (type.BaseType is null && type.Interfaces.Count == 0)
        {
            return;
        }

        var typeNode = MermaidNodeName(type.Name, type.Arity);
        var diagramBody = RenderDiagramBody(type, typeNode);
        var inheritsLine = type.BaseType is { } baseTypeRef
            ? $"**Inherits from:** {FormatReference(baseTypeRef)}\n\n"
            : string.Empty;
        var implementsLine = type.Interfaces.Count > 0
            ? $"**Implements:** {FormatReferenceList(type.Interfaces)}\n\n"
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
        sb.Append("    class ").Append(typeNode).Append('\n');

        if (type.BaseType is { } baseType)
        {
            var baseNode = MermaidNodeName(baseType.DisplayName);
            sb.Append("    class ").Append(baseNode).Append('\n')
                .Append("    ").Append(baseNode).Append(" <|-- ").Append(typeNode).Append('\n');
        }

        foreach (var iface in type.Interfaces)
        {
            var ifaceNode = MermaidNodeName(iface.DisplayName);
            sb.Append("    class ").Append(ifaceNode).Append(" {\n")
                .Append("        <<interface>>\n")
                .Append("    }\n")
                .Append("    ").Append(ifaceNode).Append(" <|.. ").Append(typeNode).Append('\n');
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
    private static void AppendAppliesTo(StringBuilder sb, List<string> appliesTo)
    {
        if (appliesTo.Count == 0)
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
    private static string JoinTfms(List<string> appliesTo)
    {
        var sb = new StringBuilder(capacity: appliesTo.Count * InitialModifierCapacity);
        for (var i = 0; i < appliesTo.Count; i++)
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
    private static void AppendUnionCases(StringBuilder sb, ApiType type)
    {
        if (type is not ApiUnionType union || union.Cases.Count == 0)
        {
            return;
        }

        var unionNode = MermaidNodeName(union.Name, union.Arity);
        var diagramBody = RenderUnionDiagramBody(union.Cases, unionNode);
        var caseRows = RenderUnionCaseRows(union.Cases);

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
    private static string RenderUnionDiagramBody(List<ApiTypeReference> cases, string unionNode)
    {
        var sb = new StringBuilder(capacity: cases.Count * InitialModifierCapacity);
        sb.Append("    class ").Append(unionNode).Append(" {\n")
            .Append("        <<union>>\n")
            .Append("    }\n");

        foreach (var caseRef in cases)
        {
            var caseNode = MermaidNodeName(caseRef.DisplayName);
            sb.Append("    class ").Append(caseNode).Append('\n')
                .Append("    ").Append(unionNode).Append(" o-- ").Append(caseNode).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the union-cases table.
    /// </summary>
    /// <param name="cases">The case type references.</param>
    /// <returns>The Markdown table rows for the union cases.</returns>
    private static string RenderUnionCaseRows(List<ApiTypeReference> cases)
    {
        var sb = new StringBuilder(capacity: cases.Count * InitialPageCapacity / 16);
        sb.Append("| Case | Description |\n")
            .Append("| ---- | ----------- |\n");

        foreach (var caseRef in cases)
        {
            sb.Append("| ").Append(FormatReference(caseRef)).Append(" |  |\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Joins a list of type references into a comma-separated Markdown string.
    /// </summary>
    /// <param name="references">The references to format.</param>
    /// <returns>A comma-separated list of formatted references.</returns>
    private static string FormatReferenceList(List<ApiTypeReference> references)
    {
        var sb = new StringBuilder(capacity: references.Count * InitialModifierCapacity);
        for (var i = 0; i < references.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(FormatReference(references[i]));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders an <see cref="ApiTypeReference"/> as a Markdown string.
    /// </summary>
    /// <remarks>
    /// Renders as an autorefs link if a UID is present; otherwise, as inline code.
    /// </remarks>
    /// <param name="reference">The reference to render.</param>
    /// <returns>The formatted reference string.</returns>
    private static string FormatReference(ApiTypeReference reference) =>
        reference.Uid.Length > 0
            ? $"[{reference.DisplayName}][{reference.Uid}]"
            : $"`{reference.DisplayName}`";

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
        arity == 0 ? name : $"{name}~{string.Join(",", GenericPlaceholders(arity))}~";

    /// <summary>
    /// Builds the page heading text.
    /// </summary>
    /// <param name="type">The type whose heading to format.</param>
    /// <returns>The formatted heading text.</returns>
    private static string RenderHeading(ApiType type)
    {
        var generics = type.Arity > 0
            ? $"<{string.Join(", ", GenericPlaceholders(type.Arity))}>"
            : string.Empty;

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

        return $"{type.Name}{generics} {kindLabel}";
    }

    /// <summary>
    /// Returns generic parameter placeholder names for a given arity.
    /// </summary>
    /// <param name="arity">The generic arity.</param>
    /// <returns>A list of generic parameter placeholder names.</returns>
    private static string[] GenericPlaceholders(int arity) => arity switch
    {
        1 => ["T"],
        2 => ["T1", "T2"],
        3 => ["T1", "T2", "T3"],
        4 => ["T1", "T2", "T3", "T4"],
        _ => NumberedPlaceholders(arity),
    };

    /// <summary>
    /// Returns numbered generic parameter placeholder names.
    /// </summary>
    /// <param name="arity">The generic arity.</param>
    /// <returns>A list of numbered generic parameter placeholder names.</returns>
    private static string[] NumberedPlaceholders(int arity)
    {
        var names = new string[arity];
        for (var i = 1; i <= arity; i++)
        {
            names[i - 1] = $"T{i}";
        }

        return names;
    }

    /// <summary>
    /// Appends an Examples section containing one fenced block per example.
    /// </summary>
    /// <remarks>
    /// Skipped when the type has no examples.
    /// </remarks>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="examples">The example XML fragments.</param>
    private static void AppendExamples(StringBuilder sb, List<string> examples)
    {
        if (examples.Count == 0)
        {
            return;
        }

        sb.Append("""

            ## Examples

            """);

        foreach (var example in examples)
        {
            sb.Append('\n').Append(example).Append('\n');
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

        if (members is not { Count: > 0 })
        {
            return;
        }

        // Capacity = number of ApiMemberKind values; a type rarely
        // uses every kind, but it's a tight upper bound and avoids
        // any growth.
        var byKind = new Dictionary<ApiMemberKind, List<ApiMember>>(capacity: ApiMemberKindCount);
        for (var i = 0; i < members.Count; i++)
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

        foreach (var member in entries)
        {
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
              .Append(" | ").Append(summary).Append(" |\n");
        }
    }

    /// <summary>
    /// Returns the folder name a type's member pages live in.
    /// </summary>
    /// <param name="type">The type to compute the folder for.</param>
    /// <returns>The folder name for the type's member pages.</returns>
    private static string TypeFolderName(ApiType type) => type.Arity > 0
        ? $"{type.Name}{{{string.Join(",", GenericPlaceholders(type.Arity))}}}"
        : type.Name;

    /// <summary>
    /// Strips unsafe characters from a member name for use in a filename.
    /// </summary>
    /// <param name="name">The raw member name.</param>
    /// <returns>A sanitised filename-safe string.</returns>
    private static string SanitiseForFilename(string name) => name
        .Replace('.', '_')
        .Replace('<', '{')
        .Replace('>', '}')
        .Replace(':', '_');

    /// <summary>
    /// Joins type-level modifiers into a space-separated string.
    /// </summary>
    /// <param name="type">The type whose modifiers to format.</param>
    /// <returns>A space-separated string of modifiers.</returns>
    private static string JoinModifiers(ApiType type)
    {
        var sb = new StringBuilder(capacity: InitialModifierCapacity);
        sb.Append("public");
        if (type.IsStatic)
        {
            sb.Append(" static");
        }
        else
        {
            if (type.IsAbstract)
            {
                sb.Append(" abstract");
            }

            if (type.IsSealed)
            {
                sb.Append(" sealed");
            }
        }

        return sb.ToString();
    }

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
        if (summary.Length == 0)
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
        text.Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>
    /// Escapes pipes and replaces newlines for use in a table cell.
    /// </summary>
    /// <param name="text">The cell content.</param>
    /// <returns>The escaped table cell content.</returns>
    private static string TableEscape(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');
}
