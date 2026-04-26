// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using NuGet.Frameworks;

namespace SourceDocParser;

/// <summary>
/// Pure helpers for selecting target frameworks from package contents and
/// matching lib/ TFMs against available refs/ TFMs. All methods are
/// stateless so they can be unit-tested in isolation from any IO. No LINQ
/// in the lookups: each call is a few simple passes that early-exit on
/// match, which is both cheaper and easier to read for what's logically
/// "find the first hit".
/// </summary>
public static class TfmResolver
{
    /// <summary>
    /// Shared <see cref="FrameworkReducer"/> instance. <c>FrameworkReducer</c> is thread-safe
    /// and reusable; one instance amortises the internal lookup tables across every call.
    /// </summary>
    private static readonly FrameworkReducer _frameworkReducer = new();

    /// <summary>Version-aware comparer — embedded digits compare as numbers (net10 &gt; net9).</summary>
    private static readonly StringComparer _versionAware = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering);

    /// <summary>
    /// Selects every TFM in availableTfms that the resolver would accept.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmOverride">Optional per-package TFM override.</param>
    /// <param name="tfmPreference">Ordered list of preferred TFMs.</param>
    /// <returns>The list of supported TFMs.</returns>
    public static List<string> SelectAllSupportedTfms(
        List<string> availableTfms,
        string? tfmOverride,
        string[] tfmPreference)
    {
        ArgumentNullException.ThrowIfNull(availableTfms);
        ArgumentNullException.ThrowIfNull(tfmPreference);

        // Override path: pin to whatever SelectTfm picks.
        if (tfmOverride is not null)
        {
            return SelectTfm(availableTfms, tfmOverride, tfmPreference) switch
            {
                { } pinned => [pinned],
                _ => [],
            };
        }

        List<string> selected = [];
        for (var i = 0; i < availableTfms.Count; i++)
        {
            var available = availableTfms[i];
            if (MatchesAnyPreference(available, tfmPreference))
            {
                selected.Add(available);
            }
        }

        if (selected is [_, ..])
        {
            return selected;
        }

        // Netstandard fallback: include every netstandard variant
        // present, not just the highest, so cross-package merging
        // sees a faithful "Applies to" if a type happens to live
        // in multiple netstandard versions.
        for (var i = 0; i < availableTfms.Count; i++)
        {
            var available = availableTfms[i];
            if (available.IsNetStandardFallback())
            {
                selected.Add(available);
            }
        }

        return selected;
    }

    /// <summary>
    /// Selects the best matching TFM from availableTfms.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmOverride">Optional per-package TFM override.</param>
    /// <param name="tfmPreference">Ordered list of preferred TFMs.</param>
    /// <returns>The best matching TFM, or null if none found.</returns>
    /// <remarks>
    /// Resolution rule:
    /// 1. Honour any per-package override first (exact then prefix).
    /// 2. Walk the global preference list (exact then prefix then major version).
    /// 3. Netstandard fallback: if nothing matched but the package ships a netstandard variant, take that.
    /// </remarks>
    public static string? SelectTfm(
        List<string> availableTfms,
        string? tfmOverride,
        string[] tfmPreference)
    {
        ArgumentNullException.ThrowIfNull(availableTfms);
        ArgumentNullException.ThrowIfNull(tfmPreference);

        if (tfmOverride is not null)
        {
            if (FirstExactMatch(availableTfms, tfmOverride) is { } exact)
            {
                return exact;
            }

            if (FirstPrefixMatch(availableTfms, tfmOverride) is { } prefix)
            {
                return prefix;
            }
        }

        for (var i = 0; i < tfmPreference.Length; i++)
        {
            var pref = tfmPreference[i];
            if (FirstExactMatch(availableTfms, pref) is { } exact)
            {
                return exact;
            }
        }

        for (var i = 0; i < tfmPreference.Length; i++)
        {
            var pref = tfmPreference[i];
            if (FirstPrefixMatch(availableTfms, pref) is { } prefix)
            {
                return prefix;
            }
        }

        // Handle major version preference (e.g., net8 matching net8.0)
        for (var i = 0; i < tfmPreference.Length; i++)
        {
            var pref = tfmPreference[i];
            var prefTfm = Tfm.Parse(pref);
            for (var j = 0; j < availableTfms.Count; j++)
            {
                var available = availableTfms[j];
                var availableTfm = Tfm.Parse(available);
                if (availableTfm.Family == prefTfm.Family && availableTfm.Version.StartsWith(prefTfm.Version, StringComparison.Ordinal))
                {
                    return available;
                }
            }
        }

        // Netstandard fallback - the documented exception to the
        // supported-TFMs-only rule. Picks the highest netstandard
        // available since 2.1 supersedes 2.0 in API surface.
        string? bestNetstandard = null;
        for (var i = 0; i < availableTfms.Count; i++)
        {
            var tfm = availableTfms[i];
            if (!tfm.IsNetStandardFallback())
            {
                continue;
            }

            // NumericOrdering compares embedded version digits as
            // numbers — netstandard2.1 sorts after netstandard2.0,
            // and a hypothetical netstandard10.0 sorts after both
            // (which ordinal would get wrong).
            if (bestNetstandard is null || _versionAware.Compare(tfm, bestNetstandard) > 0)
            {
                bestNetstandard = tfm;
            }
        }

        return bestNetstandard;
    }

    /// <summary>
    /// Finds the most appropriate refs/ TFM directory to pair with a
    /// package's lib/ TFM. Strips any platform suffix from the lib TFM,
    /// prefers an exact match, falls back to a prefix match, and for
    /// netstandard libs picks the highest available modern .NET refs.
    /// Returns null when nothing in refs/ is compatible (legacy TFMs
    /// like monoandroid).
    /// </summary>
    /// <param name="libTfm">The TFM under lib/ being resolved (for example net10.0-android35.0).</param>
    /// <param name="refsTfms">All TFMs present under refs/.</param>
    /// <returns>The best matching reference TFM, or null if none found.</returns>
    public static string? FindBestRefsTfm(string libTfm, List<string> refsTfms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libTfm);
        ArgumentNullException.ThrowIfNull(refsTfms);

        if (refsTfms is [])
        {
            return null;
        }

        // Fast path 1: exact string match. Covers the dominant case where
        // a package's lib/ TFM is also present verbatim under refs/.
        for (var i = 0; i < refsTfms.Count; i++)
        {
            var raw = refsTfms[i];
            if (string.Equals(raw, libTfm, StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }
        }

        // Fast path 2: strip a single platform suffix (net10.0-android36.0
        // -> net10.0) and look for the base TFM in refs/. Avoids parsing
        // every entry into NuGetFramework for the common platform-targeted
        // libraries.
        if (libTfm.AsSpan().IndexOf('-') is > 0 and var dashIdx)
        {
            var stripped = libTfm.AsSpan(0, dashIdx);
            for (var i = 0; i < refsTfms.Count; i++)
            {
                var raw = refsTfms[i];
                if (stripped.Equals(raw, StringComparison.OrdinalIgnoreCase))
                {
                    return raw;
                }
            }
        }

        return FindBestRefsTfmSlow(libTfm, refsTfms);
    }

    /// <summary>
    /// Returns the platform classification for a TFM (one of android, ios,
    /// maccatalyst, windows) used to bucket platform-specific docs output,
    /// or null for platform-neutral TFMs. Recognises both modern
    /// (net10.0-android) and legacy (monoandroid, xamarinios, uap) forms.
    /// </summary>
    /// <param name="tfm">The TFM to classify.</param>
    /// <returns>The platform label, or null if not a platform-specific TFM.</returns>
    public static string? GetPlatformLabel(string tfm) => tfm switch
    {
        _ when tfm.Contains("-android", StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith("monoandroid", StringComparison.OrdinalIgnoreCase) => "android",
        _ when tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith("xamarinios", StringComparison.OrdinalIgnoreCase) => "ios",
        _ when tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith("xamarinmac", StringComparison.OrdinalIgnoreCase) => "maccatalyst",
        _ when tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith("uap", StringComparison.OrdinalIgnoreCase) => "windows",
        _ => null,
    };

    /// <summary>
    /// Slow path for <see cref="FindBestRefsTfm"/>: defers to
    /// <see cref="FrameworkReducer"/> for cases the string fast paths
    /// cannot answer (netstandard fallback, version downscaling).
    /// </summary>
    /// <param name="libTfm">The TFM under lib/ being resolved.</param>
    /// <param name="refsTfms">All TFMs present under refs/.</param>
    /// <returns>The best matching reference TFM, or null if none found.</returns>
    private static string? FindBestRefsTfmSlow(string libTfm, List<string> refsTfms)
    {
        var libFramework = NuGetFramework.Parse(libTfm);
        if (libFramework.IsUnsupported)
        {
            return null;
        }

        // Build a NuGetFramework -> original-string map so we can return
        // the caller's spelling of the winning TFM directory name.
        var candidates = new List<NuGetFramework>(refsTfms.Count);
        var byFramework = new Dictionary<NuGetFramework, string>(refsTfms.Count);
        for (var i = 0; i < refsTfms.Count; i++)
        {
            var raw = refsTfms[i];
            var fw = NuGetFramework.Parse(raw);
            if (fw.IsUnsupported)
            {
                continue;
            }

            candidates.Add(fw);
            byFramework.TryAdd(fw, raw);
        }

        if (_frameworkReducer.GetNearest(libFramework, candidates) is { } nearest)
        {
            return byFramework.GetValueOrDefault(nearest);
        }

        // FrameworkReducer treats netstandard as an abstract target you
        // cannot run on, so it returns null when asked which of {net8.0+}
        // is "compatible with" netstandard2.0. For docs purposes a modern
        // .NET ref pack does provide types that satisfy a netstandard
        // library's references, so fall back to the highest modern .NET
        // entry available.
        return libFramework.Framework == FrameworkConstants.FrameworkIdentifiers.NetStandard
            ? PickHighestModernNetRef(candidates, byFramework)
            : null;
    }

    /// <summary>
    /// From <paramref name="candidates"/>, returns the original string
    /// spelling of the highest .NET (5+) framework, or null when no
    /// modern .NET candidate is present.
    /// </summary>
    /// <param name="candidates">Parsed candidate frameworks.</param>
    /// <param name="byFramework">Map from framework back to its original raw string.</param>
    /// <returns>The chosen TFM string, or null.</returns>
    private static string? PickHighestModernNetRef(List<NuGetFramework> candidates, Dictionary<NuGetFramework, string> byFramework)
    {
        NuGetFramework? best = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate is { Framework: not FrameworkConstants.FrameworkIdentifiers.NetCoreApp } or { Version.Major: < 5 })
            {
                continue;
            }

            if (best is null || candidate.Version > best.Version)
            {
                best = candidate;
            }
        }

        return best is not null && byFramework.TryGetValue(best, out var pick) ? pick : null;
    }

    /// <summary>
    /// Returns true when the supplied TFM matches any entry in the preference
    /// list. Handles exact matches, prefix matches, and major version matches.
    /// </summary>
    /// <param name="tfm">TFM to test.</param>
    /// <param name="tfmPreference">Preference list.</param>
    /// <returns>True if a match is found; otherwise, false.</returns>
    private static bool MatchesAnyPreference(string tfm, string[] tfmPreference)
    {
        var testTfm = Tfm.Parse(tfm);
        for (var i = 0; i < tfmPreference.Length; i++)
        {
            var pref = tfmPreference[i];
            if (string.Equals(tfm, pref, StringComparison.OrdinalIgnoreCase)
                || tfm.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefTfm = Tfm.Parse(pref);
            if (testTfm.Family == prefTfm.Family && testTfm.Version.AsSpan().StartsWith(prefTfm.Version, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the first entry in candidates that compares ordinally
    /// equal (case-insensitive) to target, or null. Wraps the scan so
    /// callers don't repeat the loop body.
    /// </summary>
    /// <param name="candidates">Strings to scan, in iteration order.</param>
    /// <param name="target">Value to match.</param>
    /// <returns>The first exact match, or null if none found.</returns>
    private static string? FirstExactMatch(List<string> candidates, string target)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first entry in candidates that starts (case-insensitive)
    /// with target, or null.
    /// </summary>
    /// <param name="candidates">Strings to scan, in iteration order.</param>
    /// <param name="target">Prefix to match.</param>
    /// <returns>The first prefix match, or null if none found.</returns>
    private static string? FirstPrefixMatch(List<string> candidates, string target)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.AsSpan().StartsWith(target, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
