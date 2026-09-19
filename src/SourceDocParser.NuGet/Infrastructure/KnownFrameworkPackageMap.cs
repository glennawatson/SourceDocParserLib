// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Maps known projection assembly names to the NuGet packages that publish them.</summary>
internal static class KnownFrameworkPackageMap
{
    /// <summary>NuGet package ID for the Windows App SDK family.</summary>
    private const string WindowsAppSdkPackageId = "Microsoft.WindowsAppSDK";

    /// <summary>NuGet package ID for the standalone WebView2 family.</summary>
    private const string WebView2PackageId = "Microsoft.Web.WebView2";

    /// <summary>
    /// Single source of truth for "this synthetic ref name -> this NuGet package
    /// ID". Add entries here whenever a new platform projection becomes a doc
    /// surface in the wild.
    /// </summary>
    private static readonly FrozenDictionary<string, string> RefToPackage = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Windows App SDK (WinUI 3 + projections + WebView2 projection).
        ["Microsoft.WinUI"] = WindowsAppSdkPackageId,
        ["WinRT.Runtime"] = WindowsAppSdkPackageId,
        ["Microsoft.Windows.SDK.NET"] = WindowsAppSdkPackageId,
        ["Microsoft.InteractiveExperiences.Projection"] = WindowsAppSdkPackageId,
        ["Microsoft.Web.WebView2.Core.Projection"] = WindowsAppSdkPackageId,
        ["Microsoft.Windows.UI.Xaml"] = WindowsAppSdkPackageId,
        ["Microsoft.Windows.AppLifecycle.Projection"] = WindowsAppSdkPackageId,

        // Standalone WebView2 (NuGet, ships separately from WindowsAppSDK).
        ["Microsoft.Web.WebView2.Core"] = WebView2PackageId,
        ["Microsoft.Web.WebView2.Wpf"] = WebView2PackageId,
        ["Microsoft.Web.WebView2.WinForms"] = WebView2PackageId,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Returns the NuGet package ID that ships <paramref name="referenceName"/>,
    /// or <see langword="null"/> when no mapping exists. Match is
    /// case-sensitive (assembly names are conventionally cased).
    /// </summary>
    /// <param name="referenceName">Simple assembly name (no extension).</param>
    /// <returns>The package ID, or null when not mapped.</returns>
    internal static string? TryGetPackageId(string referenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
        return RefToPackage.GetValueOrDefault(referenceName);
    }

    /// <summary>
    /// Projects a list of unresolved-reference names onto the NuGet
    /// package IDs that should be added to a fetch run to satisfy
    /// them. De-duplicates: the same package ID won't appear twice
    /// even when several refs map to it (e.g. multiple WinUI
    /// projections all collapse to <c>Microsoft.WindowsAppSDK</c>).
    /// </summary>
    /// <param name="unresolvedRefs">Reference names that didn't resolve through normal package fetch.</param>
    /// <returns>The de-duplicated package IDs in first-seen order.</returns>
    internal static List<string> AdditionalNuGetPackagesFor(IEnumerable<string> unresolvedRefs)
    {
        ArgumentNullException.ThrowIfNull(unresolvedRefs);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var refName in unresolvedRefs)
        {
            if (refName is not [_, ..])
            {
                continue;
            }

            if (RefToPackage.TryGetValue(refName, out var packageId) && seen.Add(packageId))
            {
                ordered.Add(packageId);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Snapshot of every reference name currently known to map to a
    /// NuGet package. Returned as a fresh array so callers can mutate
    /// freely; the underlying map is private.
    /// </summary>
    /// <returns>The mapped reference names.</returns>
    internal static string[] KnownReferenceNames()
    {
        var names = new string[RefToPackage.Count];
        RefToPackage.Keys.CopyTo(names, 0);
        return names;
    }
}
