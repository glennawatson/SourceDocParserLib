// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Helpers for building the fallback assembly index that an
/// <see cref="IAssemblySource"/> hands to the parser through
/// <see cref="AssemblyGroup.FallbackIndex"/>.
/// </summary>
public static partial class AssemblyResolution
{
    /// <summary>File pattern used when scanning for fallback assemblies.</summary>
    private const string DllPattern = "*.dll";

    /// <summary>
    /// Indexes every DLL in the supplied directories by its filename
    /// (without extension), so the parser's resolver can fall back to a
    /// direct file lookup when the standard reference resolver can't
    /// place a transitive reference. Duplicates are first-write-wins;
    /// later collisions are logged at trace level.
    /// </summary>
    /// <param name="directories">Directories to index. Missing directories are skipped silently.</param>
    /// <param name="logger">Logger for duplicate-name notices.</param>
    /// <returns>Filename to absolute path map (case-insensitive).</returns>
    public static Dictionary<string, string> BuildFallbackIndex(List<string> directories, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(logger);

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < directories.Count; i++)
        {
            var dir = directories[i];
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var dll in Directory.EnumerateFiles(dir, DllPattern, SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(dll.AsSpan()).ToString();
                if (!index.TryAdd(name, dll))
                {
                    LogFallbackDuplicate(logger, name, index[name], dll);
                }
            }
        }

        return index;
    }

    /// <summary>Logs that two DLLs in the supplied search paths share a simple name.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="name">Simple assembly name (no extension).</param>
    /// <param name="kept">Path retained in the index.</param>
    /// <param name="ignored">Path discarded.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "  fallback duplicate '{Name}': keeping {Kept}, ignoring {Ignored}")]
    private static partial void LogFallbackDuplicate(ILogger logger, string name, string kept, string ignored);
}
