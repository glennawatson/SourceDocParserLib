// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Pages;

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
    [SuppressMessage("Critical Code Smell", "S2339:Public constant members should not be used", Justification = "Default value is not secret.")]
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
    /// Minimum retained prefix before using a space-delimited summary cut.
    /// </summary>
    private const int MinimumSummaryWordBoundary = SummaryMaxLength / 2;

    /// <summary>
    /// Length of the XML-doc cref prefix (for example <c>T:</c> or <c>M:</c>).
    /// </summary>
    private const int CrefPrefixLength = 2;

    /// <summary>
    /// Mermaid class declaration prefix including indentation.
    /// </summary>
    private const string MermaidClassDeclarationPrefix = "    class ";

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
    public static void RenderToFile(ApiType type, string outputRoot, ZensicalEmitterOptions options) =>
        RenderToFile(type, outputRoot, options, ZensicalCatalogIndexes.Empty);

    /// <summary>
    /// Catalog-aware <see cref="RenderToFile(ApiType, string, ZensicalEmitterOptions)"/>
    /// -- threads <paramref name="indexes"/> through to the page render
    /// so the type page picks up the "Derived types", "Inherited
    /// members", and "Extension members" sections.
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <param name="indexes">Pre-built catalog rollups.</param>
    public static void RenderToFile(ApiType type, string outputRoot, ZensicalEmitterOptions options, ZensicalCatalogIndexes indexes) =>
        RenderToFile(type, outputRoot, BuildDefaultConverter(), options, indexes);

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
    public static string Render(ApiType type, ZensicalEmitterOptions options) =>
        Render(type, options, ZensicalCatalogIndexes.Empty);

    /// <summary>
    /// Catalog-aware render: in addition to the per-type metadata,
    /// renders "Derived types", "Inherited members", and
    /// "Extension members" sections pulled from
    /// <paramref name="indexes"/> when the type has matching entries.
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <param name="indexes">Catalog rollups; pass <see cref="ZensicalCatalogIndexes.Empty"/> to skip them.</param>
    /// <returns>The rendered Markdown string.</returns>
    public static string Render(ApiType type, ZensicalEmitterOptions options, ZensicalCatalogIndexes indexes) =>
        Render(type, BuildDefaultConverter(), options, indexes);

    /// <summary>
    /// Returns a relative file path for the type's page (legacy
    /// flat-namespace layout -- no per-package folder routing).
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
        return packageFolder is null ? basePath : packageFolder + "/" + basePath;
    }

    /// <summary>
    /// Render-and-write entry point used by
    /// <see cref="ZensicalDocumentationEmitter"/>. Consumes raw walker
    /// output; XML->Markdown conversion runs lazily through
    /// <see cref="RenderedDoc"/> so per-symbol fields are materialised
    /// at most once even if multiple sections (or per-overload pages)
    /// read them.
    /// </summary>
    /// <param name="type">Type to render -- raw walker output.</param>
    /// <param name="outputRoot">Markdown output root.</param>
    /// <param name="context">Render context built once per emit run.</param>
    internal static void RenderToFile(ApiType type, string outputRoot, ZensicalEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderToFile(type, outputRoot, context.Converter, context.Options, context.Indexes);
    }

    /// <summary>
    /// Renders the "Derived types" section as a bullet list of
    /// autoref links. No-op when the type has no derivers -- the empty-
    /// array check is branch-and-bail with no allocation.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="derived">Derived class refs from <see cref="ZensicalCatalogIndexes.GetDerived"/>.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    internal static void AppendDerivedTypes(StringBuilder sb, ApiTypeReference[] derived, ZensicalEmitterOptions options)
    {
        if (derived is [])
        {
            return;
        }

        sb.Append("\n## Derived types\n\n");
        for (var i = 0; i < derived.Length; i++)
        {
            sb.Append("- ").AppendLine(CrossLinkRouter.Format(derived[i], options));
        }
    }

    /// <summary>
    /// Renders an "Inherited members" collapsible admonition. No-op
    /// when there are no inherited entries -- the empty-array path
    /// allocates nothing. Each row is routed through
    /// <see cref="CrossLinkRouter"/> so BCL members
    /// (System.Object.ToString, etc.) become Microsoft Learn links
    /// instead of broken autorefs.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="inherited">Inherited member uids from <see cref="ZensicalCatalogIndexes.GetInherited"/>.</param>
    /// <param name="options">Routing + cross-link tunables (carries the run's resolver).</param>
    internal static void AppendInheritedMembers(StringBuilder sb, string[] inherited, ZensicalEmitterOptions options)
    {
        if (inherited is [])
        {
            return;
        }

        sb.Append("\n??? abstract \"Inherited members\"\n");
        for (var i = 0; i < inherited.Length; i++)
        {
            var uid = inherited[i];
            var label = uid is [_, ':', ..] ? uid[CrefPrefixLength..] : uid;
            sb.Append("    - ").AppendLine(CrossLinkRouter.Format(new($"`{label}`", uid), options));
        }
    }

    /// <summary>
    /// Renders the "Extension members" section listing static methods
    /// from other types whose first parameter targets this type. The
    /// markdown intentionally uses the unified C# 14 term "extension
    /// members" rather than "extension methods" so the heading stays
    /// consistent with the C# 14 extension-block section that follows.
    /// No-op when there are none.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="extensions">Extension members from <see cref="ZensicalCatalogIndexes.GetExtensions"/>.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    internal static void AppendExtensionMembers(StringBuilder sb, ApiMember[] extensions, ZensicalEmitterOptions options)
    {
        if (extensions is [])
        {
            return;
        }

        sb.Append("\n## Extension members\n\n");
        for (var i = 0; i < extensions.Length; i++)
        {
            var member = extensions[i];
            var label = $"`{member.ContainingTypeName}.{member.Name}`";
            sb.Append("- ").AppendLine(CrossLinkRouter.Format(new(label, member.Uid), options));
        }
    }

    /// <summary>
    /// Renders the "See also" section listing each cref from the
    /// type's documentation as an autoref link. No-op when empty.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="seealso">SeeAlso cref strings from <see cref="ApiDocumentation.SeeAlso"/>.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    internal static void AppendSeeAlso(StringBuilder sb, string[] seealso, ZensicalEmitterOptions options)
    {
        if (seealso is [])
        {
            return;
        }

        sb.Append("\n## See also\n\n");
        for (var i = 0; i < seealso.Length; i++)
        {
            var cref = seealso[i];
            var displayName = cref is [_, ':', ..] ? cref[CrefPrefixLength..] : cref;
            sb.Append("- ").AppendLine(CrossLinkRouter.Format(new(displayName, cref), options));
        }
    }

    /// <summary>
    /// Renders the "Extension blocks" section listing each C# 14
    /// <c>extension(T receiver)</c> block declared on the type.
    /// Each block surfaces under a <c>### extension(Type receiver)</c>
    /// subheading with its conceptual members listed as autoref
    /// links. No-op when the type declares no blocks.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="blocks">Extension blocks declared on the type.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    internal static void AppendExtensionBlocks(StringBuilder sb, ApiExtensionBlock[] blocks, ZensicalEmitterOptions options)
    {
        if (blocks is [])
        {
            return;
        }

        sb.Append("\n## Extension blocks\n\n");
        for (var i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            sb.Append("### extension(").Append(CrossLinkRouter.Format(block.Receiver, options))
              .Append(' ').Append(block.ReceiverName).Append(")\n\n");
            for (var m = 0; m < block.Members.Length; m++)
            {
                var member = block.Members[m];
                sb.Append("- ").AppendLine(CrossLinkRouter.Format(new($"`{member.Name}`", member.Uid), options));
            }

            sb.Append('\n');
        }
    }

    /// <summary>
    /// Renders the enum value table for an <see cref="ApiEnumType"/>.
    /// Skipped for any other kind. The values come straight off the
    /// structured <see cref="ApiEnumType.Values"/> list -- no per-value
    /// markdown page is produced (those would explode the file count
    /// for icon-font enums).
    /// </summary>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="type">The type whose enum values to emit.</param>
    /// <param name="converter">XML->Markdown converter for the value summaries.</param>
    private static void AppendEnumValues(StringBuilder sb, ApiType type, XmlDocToMarkdown converter)
    {
        if (type is not ApiEnumType { Values: [_, ..] } enumType)
        {
            return;
        }

        sb.Append("\n## Values\n\n| Name | Value | Description |\n| --- | --- | --- |\n");
        for (var i = 0; i < enumType.Values.Length; i++)
        {
            var value = enumType.Values[i];
            var summary = converter.Convert(value.Documentation.Summary) is [_, ..] documentedSummary
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
        sb.Append(MermaidClassDeclarationPrefix).AppendLine(typeNode);

        if (type.BaseType is { } baseType)
        {
            var baseNode = MermaidNodeName(baseType.DisplayName);
            sb.Append(MermaidClassDeclarationPrefix).AppendLine(baseNode)
                .Append("    ").Append(baseNode).Append(" <|-- ").AppendLine(typeNode);
        }

        for (var i = 0; i < type.Interfaces.Length; i++)
        {
            var iface = type.Interfaces[i];
            var ifaceNode = MermaidNodeName(iface.DisplayName);
            sb.Append(MermaidClassDeclarationPrefix).Append(ifaceNode).AppendLine(" {")
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
        sb.Append(MermaidClassDeclarationPrefix).Append(unionNode).AppendLine(" {")
            .AppendLine("        <<union>>")
            .AppendLine("    }");

        for (var i = 0; i < cases.Length; i++)
        {
            var caseRef = cases[i];
            var caseNode = MermaidNodeName(caseRef.DisplayName);
            sb.Append(MermaidClassDeclarationPrefix).AppendLine(caseNode)
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
    /// Renders an <see cref="ApiTypeReference"/> as a Markdown string --
    /// autoref key for primary-package types, Microsoft Learn URL
    /// for BCL types, inline code as the final fallback.
    /// </summary>
    /// <param name="reference">The reference to render.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The formatted reference string.</returns>
    private static string FormatReference(ApiTypeReference reference, ZensicalEmitterOptions options) =>
        CrossLinkRouter.Format(reference, options);

    /// <summary>
    /// Renders the danger-style admonition shown when a symbol is
    /// decorated with <c>[Obsolete]</c>. Returns the empty string for
    /// non-obsolete symbols so it can be unconditionally interpolated
    /// into the page template.
    /// </summary>
    /// <param name="isObsolete">Whether the symbol is obsolete.</param>
    /// <param name="message">Optional message from <c>[Obsolete(...)]</c>.</param>
    /// <returns>The deprecation admonition with a leading and trailing blank line, or empty.</returns>
    private static string RenderDeprecationAdmonition(bool isObsolete, string? message)
    {
        if (!isObsolete)
        {
            return string.Empty;
        }

        var body = message is { Length: > 0 } detail ? detail : "This API is obsolete and may be removed in a future release.";
        return $"\n!!! danger \"Deprecated\"\n    {body}\n\n";
    }

    /// <summary>
    /// Renders the inline attributes line (e.g. <c>**Attributes:** [Foo] [Bar(true)]</c>)
    /// that sits under the type heading. Compiler-emitted markers are
    /// dropped via <see cref="AttributeFilter"/>; if nothing survives,
    /// returns empty so no section is emitted at all.
    /// </summary>
    /// <param name="attributes">Attributes attached to the symbol.</param>
    /// <returns>The attributes line with a trailing blank line, or empty.</returns>
    private static string RenderAttributesLine(ApiAttribute[] attributes)
    {
        var rendered = AttributeFilter.RenderInlineList(attributes);
        return rendered is { Length: > 0 } ? $"\n**Attributes:** {rendered}\n\n" : string.Empty;
    }

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

        return ZensicalEmitterHelpers.EscapeMermaidText(name);
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
        if (examples is [])
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
    /// <param name="converter">XML->Markdown converter for the per-member table summaries.</param>
    private static void AppendMembers(StringBuilder sb, ApiType type, XmlDocToMarkdown converter)
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

        AppendMemberSection(sb, "Constructors", byKind, ApiMemberKind.Constructor, type, converter);
        AppendMemberSection(sb, "Properties", byKind, ApiMemberKind.Property, type, converter);
        AppendMemberSection(sb, "Fields", byKind, ApiMemberKind.Field, type, converter);
        AppendMemberSection(sb, "Methods", byKind, ApiMemberKind.Method, type, converter);
        AppendMemberSection(sb, "Operators", byKind, ApiMemberKind.Operator, type, converter);
        AppendMemberSection(sb, "Events", byKind, ApiMemberKind.Event, type, converter);
    }

    /// <summary>
    /// Writes a single members table for one kind.
    /// </summary>
    /// <param name="sb">The destination string builder.</param>
    /// <param name="title">The section heading text.</param>
    /// <param name="byKind">The pre-grouped members lookup.</param>
    /// <param name="kind">The kind to emit.</param>
    /// <param name="containingType">The type the members belong to.</param>
    /// <param name="converter">XML->Markdown converter for the per-row summaries.</param>
    private static void AppendMemberSection(
        StringBuilder sb,
        string title,
        Dictionary<ApiMemberKind, List<ApiMember>> byKind,
        ApiMemberKind kind,
        ApiType containingType,
        XmlDocToMarkdown converter)
    {
        if (!byKind.TryGetValue(kind, out var entries) || entries.Count is 0)
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
            var summary = TableEscape(OneLineSummary(converter.Convert(member.Documentation.Summary)));
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
        var oneLine = ZensicalEmitterHelpers.FirstParagraphAsSingleLine(summary);

        if (oneLine.Length <= SummaryMaxLength)
        {
            return oneLine;
        }

        // Cut at the last word boundary that keeps us under the limit;
        // fall back to a hard cut if there's no space in range.
        var lastSpace = oneLine.LastIndexOf(' ', SummaryMaxLength - 1);
        return lastSpace > MinimumSummaryWordBoundary
            ? oneLine[..lastSpace] + "..."
            : oneLine[..SummaryMaxLength] + "...";
    }

    /// <summary>
    /// Escapes Markdown metacharacters in inline text.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped Markdown text.</returns>
    private static string MarkdownEscape(string text) => ZensicalEmitterHelpers.EscapeInlinePipes(text);

    /// <summary>
    /// Escapes pipes and replaces newlines for use in a table cell.
    /// </summary>
    /// <param name="text">The cell content.</param>
    /// <returns>The escaped table cell content.</returns>
    private static string TableEscape(string text) => ZensicalEmitterHelpers.EscapeTableCell(text);

    /// <summary>
    /// Constructs the default per-call <see cref="XmlDocToMarkdown"/> the
    /// converter-less Render overloads use. Production emit paths
    /// supply their own resolver-aware converter.
    /// </summary>
    /// <returns>A fresh converter; cheap, single-purpose, not cached.</returns>
    private static XmlDocToMarkdown BuildDefaultConverter() => new(DefaultCrefResolver.Instance);

    /// <summary>
    /// Production render path that takes raw walker output and folds
    /// XML to Markdown conversion lazily through <see cref="RenderedDoc"/>.
    /// Each non-empty doc field is materialised exactly once when a
    /// section actually reads it; undocumented members never allocate
    /// converted strings.
    /// </summary>
    /// <param name="type">The type to render -- raw walker output.</param>
    /// <param name="converter">Markdown converter wired with the emitter's <see cref="ICrefResolver"/>.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <param name="indexes">Pre-built catalog rollups.</param>
    /// <returns>The rendered Markdown string.</returns>
    private static string Render(ApiType type, XmlDocToMarkdown converter, ZensicalEmitterOptions options, ZensicalCatalogIndexes indexes)
    {
        using var rental = PageBuilderPool.Rent(InitialPageCapacity);
        BuildPage(rental.Builder, type, converter, options, indexes);
        return rental.Builder.ToString();
    }

    /// <summary>Composes the type page into <paramref name="sb"/>.</summary>
    /// <param name="sb">Destination builder; appended to in place.</param>
    /// <param name="type">The type to render.</param>
    /// <param name="converter">XML to Markdown converter.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <param name="indexes">Pre-built catalog rollups.</param>
    private static void BuildPage(StringBuilder sb, ApiType type, XmlDocToMarkdown converter, ZensicalEmitterOptions options, ZensicalCatalogIndexes indexes)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(indexes);
        var doc = new RenderedDoc(type.Documentation, converter);
        var heading = RenderHeading(type);
        var fullDisplayName = ZensicalEmitterHelpers.FormatDisplayTypeName(type.FullName, type.Arity);
        var summary = doc.Summary is [_, ..] documentedSummary
            ? documentedSummary
            : "_No description provided._";
        var inheritedNote = doc.InheritedFrom is { Length: > 0 } inheritedFrom
            ? $"\n!!! note \"Inherited documentation\"\n    These docs were inherited from `{inheritedFrom}`.\n\n"
            : string.Empty;
        var sourceLink = type.SourceUrl is { Length: > 0 } sourceUrl
            ? $"\n[:material-source-branch: View source]({sourceUrl})\n"
            : string.Empty;
        var modifiers = JoinModifiers(type);

        var deprecation = RenderDeprecationAdmonition(type.IsObsolete, type.ObsoleteMessage);
        var attributesLine = RenderAttributesLine(type.Attributes);
        PageFrontmatter.AppendForType(sb, type, options);
        sb.Append($"""
            # {heading}
            {deprecation}{attributesLine}
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

        if (doc.Remarks is [_, ..] remarks)
        {
            sb.Append($"""

                ## Remarks

                {remarks}

                """);
        }

        AppendExamples(sb, doc.Examples);
        AppendMembers(sb, type, converter);
        AppendEnumValues(sb, type, converter);
        AppendDelegateSignature(sb, type);
        AppendDerivedTypes(sb, indexes.GetDerived(type.Uid), options);
        AppendInheritedMembers(sb, indexes.GetInherited(type.Uid), options);
        AppendExtensionMembers(sb, indexes.GetExtensions(type.Uid), options);
        AppendExtensionBlocks(sb, type is ApiObjectType obj ? obj.ExtensionBlocks : [], options);
        AppendSeeAlso(sb, doc.SeeAlso, options);
    }

    /// <summary>
    /// Lower-level render-and-write that takes the converter directly.
    /// Used by the context overload and by the converter-less public
    /// overload (which builds a default converter for tests).
    /// </summary>
    /// <param name="type">The type to render.</param>
    /// <param name="outputRoot">Output root.</param>
    /// <param name="converter">XML->Markdown converter.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <param name="indexes">Pre-built catalog rollups.</param>
    private static void RenderToFile(ApiType type, string outputRoot, XmlDocToMarkdown converter, ZensicalEmitterOptions options, ZensicalCatalogIndexes indexes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(indexes);
        using var rental = PageBuilderPool.Rent(InitialPageCapacity);
        BuildPage(rental.Builder, type, converter, options, indexes);
        PageWriter.WriteUtf8(Path.Combine(outputRoot, PathFor(type, options)), rental.Builder);
    }
}
