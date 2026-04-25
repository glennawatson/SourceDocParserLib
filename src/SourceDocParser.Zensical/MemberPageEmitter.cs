// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Zensical;

/// <summary>
/// Renders a single overload-group page for a documented type's
/// member. Mirrors the Microsoft Learn convention: <c>Foo.md</c>
/// holds every overload of <c>Foo</c> on a given type, with each
/// overload getting its own signature block, parameter table,
/// returns description and remarks.
///
/// Uses raw string literals for the larger Markdown chunks so the
/// page layout is readable when reviewing this code; per-overload
/// detail is appended via small helpers that keep StringBuilder
/// growth bounded.
/// </summary>
internal static class MemberPageEmitter
{
    /// <summary>
    /// Initial StringBuilder capacity for a member-group page. Pages
    /// scale with overload count — most are 2–6 KB total — so 4 KB
    /// covers the common case without over-allocating for the long
    /// tail of single-overload members.
    /// </summary>
    private const int InitialPageCapacity = 4096;

    /// <summary>
    /// Renders the Markdown for a set of overloads.
    /// </summary>
    /// <param name="containingType">The declaring type.</param>
    /// <param name="memberName">The member name.</param>
    /// <param name="overloads">The overloads to render.</param>
    /// <returns>The rendered Markdown.</returns>
    public static string Render(ApiType containingType, string memberName, List<ApiMember> overloads)
    {
        var first = overloads[0];
        var heading = $"{containingType.Name}.{memberName}";
        var kindLabel = MemberKindLabel(first.Kind);
        var typePagePath = TypePageEmitter.PathFor(containingType);
        var typeName = containingType.Arity > 0
            ? $"{containingType.Name}<{string.Join(", ", GenericPlaceholders(containingType.Arity))}>"
            : containingType.Name;

        var sb = new StringBuilder(capacity: InitialPageCapacity);

        sb.Append($"""
            # {heading} {kindLabel}

            !!! info "Defined in"
                Type: [{typeName}](../{Path.GetFileName(typePagePath)})
                Namespace: `{(containingType.Namespace.Length > 0 ? containingType.Namespace : "(global)")}`
                Assembly: `{containingType.AssemblyName}.dll`

            """);

        if (containingType.AppliesTo.Count > 0)
        {
            sb.Append("!!! tip \"Applies to\"\n    ");
            for (var i = 0; i < containingType.AppliesTo.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('`').Append(containingType.AppliesTo[i]).Append('`');
            }

            sb.Append("\n\n");
        }

        if (overloads.Count == 1)
        {
            AppendSingleOverload(sb, overloads[0]);
        }
        else
        {
            AppendOverloadList(sb, overloads);
            for (var i = 0; i < overloads.Count; i++)
            {
                AppendNumberedOverload(sb, overloads[i], i + 1);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the documentation path for a member.
    /// </summary>
    /// <param name="containingType">The declaring type.</param>
    /// <param name="memberName">The member name.</param>
    /// <returns>The relative path.</returns>
    public static string PathFor(ApiType containingType, string memberName)
    {
        var nsSegments = containingType.Namespace.Length > 0
            ? containingType.Namespace.Split('.')
            : ["_global"];
        var typeFolder = containingType.Arity > 0
            ? $"{containingType.Name}{{{string.Join(",", GenericPlaceholders(containingType.Arity))}}}"
            : containingType.Name;
        var sanitised = SanitiseForFilename(memberName);
        return string.Join('/', nsSegments) + "/" + typeFolder + "/" + sanitised + TypePageEmitter.FileExtension;
    }

    /// <summary>
    /// Convenience: render and write the page to disk under the
    /// supplied output root, creating intermediate directories.
    /// </summary>
    /// <param name="containingType">Type the overloads are declared on.</param>
    /// <param name="memberName">Shared overload group name.</param>
    /// <param name="overloads">The overloads to render.</param>
    /// <param name="outputRoot">Directory that contains the api/ tree.</param>
    public static void RenderToFile(ApiType containingType, string memberName, List<ApiMember> overloads, string outputRoot)
    {
        var relativePath = PathFor(containingType, memberName);
        var fullPath = Path.Combine(outputRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Render(containingType, memberName, overloads));
    }

    /// <summary>
    /// Appends the body for a single (non-overloaded) member: signature
    /// block followed by the standard sections.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="member">Member to render.</param>
    private static void AppendSingleOverload(StringBuilder sb, ApiMember member)
    {
        AppendSignatureBlock(sb, member);
        AppendSections(sb, member);
    }

    /// <summary>
    /// For multi-overload pages, prepends a quick-jump list so readers
    /// can skim the available shapes before diving into per-overload
    /// detail. Each entry is a Markdown anchor link; the per-overload
    /// section uses <c>### N. signature</c> headings that GFM auto-
    /// links match against.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="overloads">The overloads.</param>
    private static void AppendOverloadList(StringBuilder sb, List<ApiMember> overloads)
    {
        sb.Append("""

            ## Overloads

            """);

        for (var i = 0; i < overloads.Count; i++)
        {
            sb.Append(i + 1).Append(". `").Append(overloads[i].Signature).Append("`\n");
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Appends one numbered overload section: <c>### N. signature</c>
    /// heading followed by the signature block and the standard
    /// sections.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="member">Overload to render.</param>
    /// <param name="ordinal">1-based overload index for the heading.</param>
    private static void AppendNumberedOverload(StringBuilder sb, ApiMember member, int ordinal)
    {
        sb.Append("\n### ").Append(ordinal).Append(". Overload\n\n");
        AppendSignatureBlock(sb, member);
        AppendSections(sb, member);
    }

    /// <summary>
    /// Emits the fenced csharp signature block for a member, plus a
    /// trailing "[View source]" link when the symbol resolved to a
    /// SourceLink-backed URL. The link sits on its own line so it
    /// doesn't disrupt the signature copy/paste.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="member">Member to render.</param>
    private static void AppendSignatureBlock(StringBuilder sb, ApiMember member)
    {
        sb.Append("```csharp\n").Append(member.Signature).Append("\n```\n\n");

        if (member.SourceUrl is not { Length: > 0 } url)
        {
            return;
        }

        sb.Append("[:material-source-branch: View source](").Append(url).Append(")\n\n");
    }

    /// <summary>
    /// Appends the standard per-overload sections in conventional
    /// order: summary → type parameters → parameters → returns →
    /// remarks → exceptions → examples → see also. Each section is
    /// only emitted when it has content.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="member">Member to render.</param>
    private static void AppendSections(StringBuilder sb, ApiMember member)
    {
        var doc = member.Documentation;

        if (doc.InheritedFrom is { Length: > 0 } inheritedFrom)
        {
            sb.Append($"""
                !!! note "Inherited documentation"
                    These docs were inherited from `{inheritedFrom}`. The member doesn't override them on this type.


                """);
        }

        if (doc.Summary.Length > 0)
        {
            sb.Append("**Summary:** ").Append(doc.Summary).Append("\n\n");
        }

        if (member.TypeParameters.Count > 0)
        {
            AppendTypeParametersSection(sb, member, doc);
        }

        if (member.Parameters.Count > 0)
        {
            AppendParametersSection(sb, member, doc);
        }

        if (member.ReturnType is { } returnType)
        {
            AppendReturnsSection(sb, returnType, doc.Returns);
        }

        if (doc.Value.Length > 0)
        {
            sb.Append("**Value:** ").Append(doc.Value).Append("\n\n");
        }

        if (doc.Remarks.Length > 0)
        {
            sb.Append("**Remarks**\n\n").Append(doc.Remarks).Append("\n\n");
        }

        if (doc.Exceptions.Count > 0)
        {
            AppendExceptionsSection(sb, doc.Exceptions);
        }

        if (doc.Examples.Count > 0)
        {
            AppendExamplesSection(sb, doc.Examples);
        }

        if (doc.SeeAlso.Count == 0)
        {
            return;
        }

        AppendSeeAlsoSection(sb, doc.SeeAlso);
    }

    /// <summary>
    /// Renders a Type parameters table — one row per declared type
    /// parameter with whatever description the doc author wrote (or
    /// an em-dash placeholder when undocumented).
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="member">Member with the type parameters.</param>
    /// <param name="doc">Member documentation containing the typeparam descriptions.</param>
    private static void AppendTypeParametersSection(StringBuilder sb, ApiMember member, ApiDocumentation doc)
    {
        sb.Append("**Type parameters**\n\n| Name | Description |\n| ---- | ----------- |\n");
        for (var i = 0; i < member.TypeParameters.Count; i++)
        {
            var name = member.TypeParameters[i];
            sb.Append("| `").Append(name).Append("` | ").Append(TableEscape(LookupDescription(doc.TypeParameters, name))).Append(" |\n");
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Renders a Parameters table including each parameter's modifiers
    /// (ref/out/in/params), type, default value when applicable, and
    /// the doc description matched up by name.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="member">Member with the parameters.</param>
    /// <param name="doc">Member documentation containing the param descriptions.</param>
    private static void AppendParametersSection(StringBuilder sb, ApiMember member, ApiDocumentation doc)
    {
        sb.Append("**Parameters**\n\n| Name | Type | Description |\n| ---- | ---- | ----------- |\n");
        for (var i = 0; i < member.Parameters.Count; i++)
        {
            var p = member.Parameters[i];
            var modifier = ModifierLabel(p);
            sb.Append("| `").Append(modifier).Append(p.Name);
            if (p is { IsOptional: true, DefaultValue: { } def })
            {
                sb.Append(" = ").Append(def);
            }

            sb.Append("` | ").Append(FormatTypeReference(p.Type))
              .Append(" | ").Append(TableEscape(LookupDescription(doc.Parameters, p.Name))).Append(" |\n");
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Renders the Returns section: type then optional description.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="returnType">The return type reference.</param>
    /// <param name="returnsDoc">The &lt;returns&gt; doc text, or empty.</param>
    private static void AppendReturnsSection(StringBuilder sb, ApiTypeReference returnType, string returnsDoc)
    {
        sb.Append("**Returns:** ").Append(FormatTypeReference(returnType));
        if (returnsDoc.Length > 0)
        {
            sb.Append(" — ").Append(returnsDoc);
        }

        sb.Append("\n\n");
    }

    /// <summary>
    /// Renders the Exceptions table.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="exceptions">Exception entries.</param>
    private static void AppendExceptionsSection(StringBuilder sb, List<DocEntry> exceptions)
    {
        sb.Append("**Exceptions**\n\n| Type | Condition |\n| ---- | --------- |\n");
        for (var i = 0; i < exceptions.Count; i++)
        {
            var (name, value) = exceptions[i];
            sb.Append("| ").Append(FormatXref(name)).Append(" | ").Append(TableEscape(value)).Append(" |\n");
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Renders the Examples section.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="examples">Example bodies.</param>
    private static void AppendExamplesSection(StringBuilder sb, List<string> examples)
    {
        sb.Append("**Examples**\n\n");
        for (var i = 0; i < examples.Count; i++)
        {
            sb.Append(examples[i]).Append("\n\n");
        }
    }

    /// <summary>
    /// Renders the See also list.
    /// </summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="seeAlso">Related symbol UIDs.</param>
    private static void AppendSeeAlsoSection(StringBuilder sb, List<string> seeAlso)
    {
        sb.Append("**See also**\n\n");
        for (var i = 0; i < seeAlso.Count; i++)
        {
            sb.Append("- ").Append(FormatXref(seeAlso[i])).Append('\n');
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Returns the modifier keyword.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The modifier string.</returns>
    private static string ModifierLabel(ApiParameter parameter)
    {
        if (parameter.IsParams)
        {
            return "params ";
        }

        if (parameter.IsIn)
        {
            return "in ";
        }

        if (parameter.IsOut)
        {
            return "out ";
        }

        if (parameter.IsRef)
        {
            return "ref ";
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the member kind label.
    /// </summary>
    /// <param name="kind">The member kind.</param>
    /// <returns>The label.</returns>
    private static string MemberKindLabel(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Constructor => "constructor",
        ApiMemberKind.Property => "property",
        ApiMemberKind.Field => "field",
        ApiMemberKind.Method => "method",
        ApiMemberKind.Operator => "operator",
        ApiMemberKind.Event => "event",
        ApiMemberKind.EnumValue => "enum value",
        _ => "member",
    };

    /// <summary>
    /// Looks up a description by name.
    /// </summary>
    /// <param name="entries">Documentation entries.</param>
    /// <param name="name">Name to look up.</param>
    /// <returns>The description text.</returns>
    private static string LookupDescription(List<DocEntry> entries, string name)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Name, name, StringComparison.Ordinal))
            {
                return entries[i].Value;
            }
        }

        return "—";
    }

    /// <summary>
    /// Renders a type reference.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The rendered text.</returns>
    private static string FormatTypeReference(ApiTypeReference reference) =>
        reference.Uid.Length > 0
            ? $"[`{reference.DisplayName}`][{reference.Uid}]"
            : $"`{reference.DisplayName}`";

    /// <summary>
    /// Renders a cross-reference.
    /// </summary>
    /// <param name="cref">The cref UID.</param>
    /// <returns>The rendered link.</returns>
    private static string FormatXref(string cref) =>
        cref.Length > 2 && cref[1] == ':'
            ? $"[`{cref[2..]}`][{cref}]"
            : $"`{cref}`";

    /// <summary>
    /// Sanitises a name for use in a filename.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The sanitised name.</returns>
    private static string SanitiseForFilename(string name) => name
        .Replace('.', '_')
        .Replace('<', '{')
        .Replace('>', '}')
        .Replace(':', '_');

    /// <summary>
    /// Gets generic placeholders.
    /// </summary>
    /// <param name="arity">The arity.</param>
    /// <returns>The placeholders.</returns>
    private static List<string> GenericPlaceholders(int arity) => arity switch
    {
        1 => ["T"],
        2 => ["T1", "T2"],
        3 => ["T1", "T2", "T3"],
        4 => ["T1", "T2", "T3", "T4"],
        _ => NumberedPlaceholders(arity),
    };

    /// <summary>
    /// Gets numbered placeholders.
    /// </summary>
    /// <param name="arity">The arity.</param>
    /// <returns>The placeholders.</returns>
    private static List<string> NumberedPlaceholders(int arity)
    {
        var names = new List<string>(capacity: arity);
        for (var i = 1; i <= arity; i++)
        {
            names.Add($"T{i}");
        }

        return names;
    }

    /// <summary>
    /// Escapes text for a Markdown table.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    private static string TableEscape(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');
}
