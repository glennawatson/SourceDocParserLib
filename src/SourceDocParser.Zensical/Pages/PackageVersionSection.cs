// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;
using System.Text;
using SourceDocParser.Model;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Pages;

/// <summary>Shows package versions and expandable dependency tables on package landing pages.</summary>
internal static class PackageVersionSection
{
    /// <summary>Typical number of target frameworks for one package.</summary>
    private const int InitialTargetCapacity = 8;

    /// <summary>Selects only the graphs belonging to a documented package folder.</summary>
    /// <param name="folder">The generated package folder.</param>
    /// <param name="packages">Independent documentation roots.</param>
    /// <param name="routing">Configured assembly routing.</param>
    /// <returns>The matching graphs in deterministic order.</returns>
    internal static ApiPackageGraph[] ForFolder(string folder, ApiPackageGraph[] packages, PackageRoutingRule[] routing)
    {
        var selected = new List<ApiPackageGraph>(InitialTargetCapacity);
        for (var i = 0; i < packages.Length; i++)
        {
            if (BelongsToFolder(folder, packages[i], routing))
            {
                selected.Add(packages[i]);
            }
        }

        selected.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.TargetFramework, right.TargetFramework));
        return [.. selected];
    }

    /// <summary>Writes the documented package identity without repeating it for every framework.</summary>
    /// <param name="builder">Destination Markdown.</param>
    /// <param name="packages">Graphs belonging to this package page.</param>
    internal static void AppendIdentity(StringBuilder builder, ApiPackageGraph[] packages)
    {
        var identities = new HashSet<(string Id, string Version)>(packages.Length);
        for (var i = 0; i < packages.Length; i++)
        {
            var package = packages[i];
            if (!identities.Add((package.Id, package.Version)))
            {
                continue;
            }

            _ = builder.Append("**Package:** `").Append(package.Id).AppendLine("`  ");
            _ = builder.Append("**Version:** `").Append(package.Version).AppendLine("`").AppendLine();
        }
    }

    /// <summary>Writes dependency versions as plain metadata, grouped by documentation target.</summary>
    /// <param name="builder">Destination Markdown.</param>
    /// <param name="packages">Graphs belonging to this package page.</param>
    internal static void AppendDependencies(StringBuilder builder, ApiPackageGraph[] packages)
    {
        if (packages is [])
        {
            return;
        }

        _ = builder.AppendLine().AppendLine("## Dependencies").AppendLine();
        for (var i = 0; i < packages.Length; i++)
        {
            var package = packages[i];
            _ = builder.AppendLine("<details>").Append("<summary>").Append(WebUtility.HtmlEncode(package.TargetFramework));
            if (package.RuntimeIdentifier is { Length: > 0 } runtime)
            {
                _ = builder.Append(" / ").Append(WebUtility.HtmlEncode(runtime));
            }

            _ = builder.AppendLine("</summary>");
            if (package.Dependencies is [])
            {
                _ = builder.AppendLine("<p>No package dependencies.</p>").AppendLine("</details>").AppendLine();
                continue;
            }

            _ = builder.AppendLine("<table><thead><tr><th>Package</th><th>Resolved version</th></tr></thead><tbody>");
            for (var d = 0; d < package.Dependencies.Length; d++)
            {
                var dependency = package.Dependencies[d];
                _ = builder.Append("<tr><td><code>").Append(WebUtility.HtmlEncode(dependency.Id))
                    .Append("</code></td><td><code>").Append(WebUtility.HtmlEncode(dependency.Version))
                    .AppendLine("</code></td></tr>");
            }

            _ = builder.AppendLine("</tbody></table>").AppendLine("</details>").AppendLine();
        }
    }

    /// <summary>Checks which package page receives a root's documented assemblies.</summary>
    /// <param name="folder">The package folder.</param>
    /// <param name="package">The documentation root.</param>
    /// <param name="routing">Configured assembly routing.</param>
    /// <returns>Whether the graph belongs on that page.</returns>
    private static bool BelongsToFolder(string folder, ApiPackageGraph package, PackageRoutingRule[] routing)
    {
        for (var i = 0; i < package.AssemblyNames.Length; i++)
        {
            if (string.Equals(PackageRouter.ResolveFolder(package.AssemblyNames[i], routing), folder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
