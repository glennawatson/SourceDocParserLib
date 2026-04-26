// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace SourceDocParser.Zensical;

/// <summary>
/// Emits a navigation tree fragment (YAML for mkdocs / Zensical
/// mkdocs-style configs, TOML for Zensical project configs) covering
/// the API pages that <see cref="ZensicalDocumentationEmitter"/>
/// produced. Tree shape is package -> namespace -> type, with
/// alphabetic ordering at each level. Page URLs use forward slashes
/// regardless of host OS so the same fragment is portable.
/// </summary>
public sealed class NavigationEmitter
{
    /// <summary>Characters that force a YAML scalar to be quoted.</summary>
    private static readonly SearchValues<char> _yamlReservedChars = SearchValues.Create(":#'\"[]{},&*!|>%@`");

    /// <summary>Routing options — drive the package-folder grouping.</summary>
    private readonly ZensicalEmitterOptions _options;

    /// <summary>Initializes a new instance of the <see cref="NavigationEmitter"/> class.</summary>
    /// <param name="options">Routing + cross-link tunables (only the routing rules are used here).</param>
    public NavigationEmitter(ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Emits the API nav as a YAML fragment ready to paste under a
    /// top-level <c>nav:</c> entry in <c>mkdocs.yml</c>. The
    /// fragment is a single sequence item titled <c>API</c> with a
    /// nested package -> namespace -> type tree.
    /// </summary>
    /// <param name="types">Types that received pages.</param>
    /// <returns>The YAML fragment, terminated by a newline.</returns>
    public string EmitYaml(ApiType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var tree = BuildTree(types);
        var sb = new StringBuilder(capacity: types.Length * 64);
        sb.AppendLine("- API:");
        foreach (var package in tree)
        {
            sb.Append("  - ").Append(YamlScalar(package.Key)).AppendLine(":");
            foreach (var ns in package.Value)
            {
                sb.Append("    - ").Append(YamlScalar(ns.Key)).AppendLine(":");
                foreach (var entry in ns.Value)
                {
                    sb.Append("      - ").Append(YamlScalar(entry.Title)).Append(": ").AppendLine(entry.Path);
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits the API nav as a TOML fragment for Zensical project
    /// configs. Produces an inline-table array under
    /// <c>[[project.nav]]</c> with nested <c>nav</c> arrays for the
    /// package -> namespace -> type hierarchy.
    /// </summary>
    /// <param name="types">Types that received pages.</param>
    /// <returns>The TOML fragment, terminated by a newline.</returns>
    public string EmitToml(ApiType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var tree = BuildTree(types);
        var sb = new StringBuilder(capacity: types.Length * 96)
            .AppendLine("[[project.nav]]")
            .AppendLine("title = \"API\"")
            .Append("nav = [");

        var firstPackage = true;
        foreach (var package in tree)
        {
            sb.Append(firstPackage ? "\n  " : ",\n  ");
            firstPackage = false;
            sb.Append("{ title = ").Append(TomlString(package.Key)).Append(", nav = [");

            var firstNs = true;
            foreach (var ns in package.Value)
            {
                sb.Append(firstNs ? "\n    " : ",\n    ");
                firstNs = false;
                sb.Append("{ title = ").Append(TomlString(ns.Key)).Append(", nav = [");

                var firstEntry = true;
                foreach (var entry in ns.Value)
                {
                    sb.Append(firstEntry ? "\n      " : ",\n      ");
                    firstEntry = false;
                    sb.Append("{ title = ").Append(TomlString(entry.Title))
                      .Append(", path = ").Append(TomlString(entry.Path)).Append(" }");
                }

                sb.Append("\n    ] }");
            }

            sb.Append("\n  ] }");
        }

        sb.AppendLine("\n]");
        return sb.ToString();
    }

    /// <summary>Normalises a path to forward slashes.</summary>
    /// <param name="path">A path produced by <see cref="Path.Combine(string, string)"/>.</param>
    /// <returns>The path with backslashes rewritten as forward slashes.</returns>
    private static string ToPosixPath(string path) =>
        path.IndexOf('\\') < 0 ? path : path.Replace('\\', '/');

    /// <summary>Quotes a YAML scalar key when it contains characters that need quoting.</summary>
    /// <param name="value">The raw scalar.</param>
    /// <returns>Either the bare scalar or a double-quoted form.</returns>
    private static string YamlScalar(string value) =>
        value.AsSpan().IndexOfAny(_yamlReservedChars) < 0
            ? value
            : string.Create(CultureInfo.InvariantCulture, $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");

    /// <summary>Quotes a TOML string literal.</summary>
    /// <param name="value">The raw string.</param>
    /// <returns>The TOML double-quoted form.</returns>
    private static string TomlString(string value) =>
        string.Create(CultureInfo.InvariantCulture, $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"");

    /// <summary>
    /// Sorts the supplied types into an ordered package -&gt;
    /// namespace -&gt; entry tree using ordinal alphabetic ordering at
    /// each level.
    /// </summary>
    /// <param name="types">Source types.</param>
    /// <returns>The ordered tree.</returns>
    private SortedDictionary<string, SortedDictionary<string, List<NavEntry>>> BuildTree(ApiType[] types)
    {
        var tree = new SortedDictionary<string, SortedDictionary<string, List<NavEntry>>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var package = PackageRouter.ResolveFolder(type.AssemblyName, _options.PackageRouting);
            if (package is null)
            {
                continue;
            }

            var ns = type.Namespace is [_, ..] ? type.Namespace : "(global)";

            if (!tree.TryGetValue(package, out var nsBucket))
            {
                nsBucket = new SortedDictionary<string, List<NavEntry>>(StringComparer.Ordinal);
                tree[package] = nsBucket;
            }

            if (!nsBucket.TryGetValue(ns, out var entries))
            {
                entries = [];
                nsBucket[ns] = entries;
            }

            entries.Add(new NavEntry(
                Title: ZensicalEmitterHelpers.FormatDisplayTypeName(type.Name, type.Arity),
                Path: ToPosixPath(TypePageEmitter.PathFor(type, _options))));
        }

        foreach (var nsBucket in tree.Values)
        {
            foreach (var entries in nsBucket.Values)
            {
                entries.Sort(static (a, b) => string.CompareOrdinal(a.Title, b.Title));
            }
        }

        return tree;
    }

    /// <summary>One leaf in the nav tree — display title plus the relative page path.</summary>
    /// <param name="Title">The display title (formatted type name with generics).</param>
    /// <param name="Path">The relative page path with forward slashes.</param>
    private readonly record struct NavEntry(string Title, string Path);
}
