// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Decides which NuGet package ids to skip during the transitive walk.
/// Lifted out of <see cref="NuGetFetcher"/> so the user-driven exclude
/// list (exact ids + prefix list from the run config) and the built-in
/// runtime-package skips (native packages, RID-specific framework
/// packages) live in one place and can be tested in isolation.
/// </summary>
internal static class PackageExclusionFilter
{
    /// <summary>The <c>Microsoft.NET</c> prefix shared by every default-skip Microsoft package.</summary>
    private const string MicrosoftNetPrefix = "Microsoft.NET";

    /// <summary>The <c>Core.</c> infix that follows <see cref="MicrosoftNetPrefix"/> on the Core family.</summary>
    private const string MicrosoftNetCoreInfix = "Core.";

    /// <summary>ASCII bit used to fold upper-case letters to lower-case without allocations.</summary>
    private const int AsciiLowercaseBit = 0x20;

    /// <summary>
    /// Returns <see langword="true"/> when the package id matches the
    /// user's configured exclude list (exact id or prefix).
    /// </summary>
    /// <param name="id">Package identifier to test.</param>
    /// <param name="excludeIds">Exact-match exclude IDs (linear scan; expected single-digit size).</param>
    /// <param name="excludePrefixes">Prefix-match excludes, OrdinalIgnoreCase.</param>
    /// <returns><see langword="true"/> if the package should be skipped; otherwise, <see langword="false"/>.</returns>
    public static bool IsExcludedByUser(string id, string[] excludeIds, string[] excludePrefixes)
    {
        for (var i = 0; i < excludeIds.Length; i++)
        {
            if (id.Equals(excludeIds[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var idSpan = id.AsSpan();
        for (var i = 0; i < excludePrefixes.Length; i++)
        {
            if (idSpan.StartsWith(excludePrefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the transitive walk should
    /// skip <paramref name="id"/> because it's a native / RID-specific
    /// runtime package that contributes zero managed types — checked
    /// in addition to the user's own exclude list.
    /// </summary>
    /// <param name="id">Discovered transitive package ID.</param>
    /// <returns>True when the package should be skipped on transitive discovery.</returns>
    public static bool IsDefaultTransitiveSkip(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var s = id.AsSpan();

        return s.IsEmpty switch
        {
            true => false,
            _ => (s[0] | AsciiLowercaseBit) switch
            {
                'r' => s.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase),
                'm' => IsMicrosoftDefaultTransitiveSkip(s),
                _ => false,
            },
        };
    }

    /// <summary>
    /// Determines whether the specified identifier represents a Microsoft default transitive dependency
    /// that should be skipped during processing (native, RID-specific, or platform shim packages).
    /// </summary>
    /// <param name="s">The span of characters representing the identifier to evaluate.</param>
    /// <returns>True when the identifier matches one of the Microsoft default-skip patterns.</returns>
    public static bool IsMicrosoftDefaultTransitiveSkip(ReadOnlySpan<char> s)
    {
        if (!s.StartsWith(MicrosoftNetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        s = s[MicrosoftNetPrefix.Length..];

        if (s.StartsWith(".Native.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!s.StartsWith(MicrosoftNetCoreInfix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        s = s[MicrosoftNetCoreInfix.Length..];

        return s.StartsWith("Native.", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("UniversalWindowsPlatform", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Targets", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Platforms", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Jit", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Runtime.", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Portable.", StringComparison.OrdinalIgnoreCase);
    }
}
