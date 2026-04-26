// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx;

/// <summary>
/// Internal docfx helpers shared across config generation and YAML emission.
/// Exposed internally so the behavior-heavy pieces can be unit tested directly.
/// </summary>
internal static class DocfxInternalHelpers
{
    /// <summary>File pattern used to discover assemblies.</summary>
    private const string DllPattern = "*.dll";

    /// <summary>
    /// Returns the names of every immediate sub-directory of <paramref name="root"/> that contains at least one DLL.
    /// </summary>
    /// <param name="root">Directory to enumerate.</param>
    /// <returns>Sorted list of TFM directory names.</returns>
    public static List<string> DiscoverTfms(string root)
    {
        var tfms = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            using var enumerator = Directory.EnumerateFiles(dir, DllPattern).GetEnumerator();
            if (enumerator.MoveNext())
            {
                tfms.Add(Path.GetFileName(dir.AsSpan()).ToString());
            }
        }

        tfms.Sort(StringComparer.Ordinal);
        return tfms;
    }

    /// <summary>
    /// Returns a case-insensitive set of DLL filenames in <paramref name="dir"/> (or empty if the directory does not exist).
    /// </summary>
    /// <param name="dir">Directory to scan.</param>
    /// <returns>Set of DLL filenames.</returns>
    public static HashSet<string> CollectDllNames(string dir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
        {
            return names;
        }

        foreach (var file in Directory.EnumerateFiles(dir, DllPattern))
        {
            names.Add(Path.GetFileName(file.AsSpan()).ToString());
        }

        return names;
    }

    /// <summary>
    /// Returns the cached DLL filename set for a refs/ TFM, populating it on the first request.
    /// </summary>
    /// <param name="cache">Per-run cache keyed by refs/ TFM.</param>
    /// <param name="tfm">refs/ TFM whose DLL names are needed.</param>
    /// <param name="dir">Directory to scan on a cache miss.</param>
    /// <returns>Case-insensitive set of DLL filenames.</returns>
    public static HashSet<string> GetOrAddDllNames(
        Dictionary<string, HashSet<string>> cache,
        string tfm,
        string dir)
    {
        if (cache.TryGetValue(tfm, out var cached))
        {
            return cached;
        }

        var names = CollectDllNames(dir);
        cache[tfm] = names;
        return names;
    }

    /// <summary>
    /// Lists the package DLL filenames in <paramref name="libTfmDir"/>, excluding any that match a co-located reference assembly.
    /// </summary>
    /// <param name="libTfmDir">Per-TFM lib directory.</param>
    /// <param name="refDllNames">Filenames of reference assemblies to exclude.</param>
    /// <returns>Sorted list of package DLL filenames.</returns>
    public static List<string> CollectPackageDllNames(string libTfmDir, HashSet<string> refDllNames)
    {
        var packageDlls = new List<string>();

        foreach (var dll in Directory.EnumerateFiles(libTfmDir, DllPattern))
        {
            var fileName = Path.GetFileName(dll.AsSpan()).ToString();
            if (!refDllNames.Contains(fileName))
            {
                packageDlls.Add(fileName);
            }
        }

        packageDlls.Sort(StringComparer.Ordinal);
        return packageDlls;
    }

    /// <summary>
    /// Removes previously-injected platform <c>api-*</c> content entries and appends a fresh set.
    /// </summary>
    /// <param name="template">Build section from the template.</param>
    /// <param name="platformLabels">Platform labels to inject content entries for, in deterministic order.</param>
    /// <returns>The patched build section.</returns>
    public static DocfxBuildSection PatchBuildSection(DocfxBuildSection template, string[] platformLabels)
    {
        var content = new List<DocfxBuildContent>(template.Content.Length + platformLabels.Length);
        for (var i = 0; i < template.Content.Length; i++)
        {
            var item = template.Content[i];
            if (!IsInjectedPlatformEntry(item))
            {
                content.Add(item);
            }
        }

        for (var i = 0; i < platformLabels.Length; i++)
        {
            var label = platformLabels[i];
            content.Add(new(Files: [$"api-{label}/**.yml", $"api-{label}/index.md"]));
        }

        return template with { Content = [.. content] };
    }

    /// <summary>
    /// Detects content entries previously injected by the config writer.
    /// </summary>
    /// <param name="entry">A build content entry from the template.</param>
    /// <returns>True if the entry was previously injected.</returns>
    public static bool IsInjectedPlatformEntry(DocfxBuildContent entry)
    {
        if (entry.Files is not { Length: > 0 } files)
        {
            return false;
        }

        var firstFile = files[0];
        return firstFile.StartsWith("api-", StringComparison.Ordinal)
            && !firstFile.StartsWith("api/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Sanitises a UID into a filesystem-safe stem with an early return for strings that need no substitutions.
    /// </summary>
    /// <param name="value">UID or full name to sanitise.</param>
    /// <returns>The filesystem-safe stem.</returns>
    public static string SanitiseFileStem(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '<' or '>' or '/' or '\\' or '*' or '?' or '|' or ':' or '"')
            {
                return SanitiseFileStemSlow(value);
            }
        }

        return value;
    }

    /// <summary>
    /// Slow path of <see cref="SanitiseFileStem"/> for values that contain characters needing replacement.
    /// </summary>
    /// <param name="value">String to sanitise.</param>
    /// <returns>Sanitised copy.</returns>
    private static string SanitiseFileStemSlow(string value) =>
        string.Create(
            value.Length,
            value,
            static (dest, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var c = source[i];
                    dest[i] = c is '<' or '>' or '/' or '\\' or '*' or '?' or '|' or ':' or '"'
                        ? '_'
                        : c;
                }
            });
}
