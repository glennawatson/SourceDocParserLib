// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using NuGet.Frameworks;

namespace SourceDocParser.Tfm;

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
    /// Modern .NET major version (5.0 and later).
    /// </summary>
    private const int ModernNetMajorVersion = 5;

    /// <summary>Android platform label.</summary>
    private const string AndroidPlatform = "android";

    /// <summary>Suffix for modern Android TFMs.</summary>
    private const string AndroidSuffix = "-android";

    /// <summary>Prefix for legacy MonoAndroid TFMs.</summary>
    private const string MonoAndroidPrefix = "monoandroid";

    /// <summary>iOS platform label.</summary>
    private const string IosPlatform = "ios";

    /// <summary>Suffix for modern iOS TFMs.</summary>
    private const string IosSuffix = "-ios";

    /// <summary>Prefix for legacy Xamarin.iOS TFMs.</summary>
    private const string XamarinIosPrefix = "xamarinios";

    /// <summary>MacCatalyst platform label.</summary>
    private const string MacCatalystPlatform = "maccatalyst";

    /// <summary>Suffix for modern MacCatalyst TFMs.</summary>
    private const string MacCatalystSuffix = "-maccatalyst";

    /// <summary>Prefix for legacy Xamarin.Mac TFMs.</summary>
    private const string XamarinMacPrefix = "xamarinmac";

    /// <summary>Windows platform label.</summary>
    private const string WindowsPlatform = "windows";

    /// <summary>Suffix for modern Windows TFMs.</summary>
    private const string WindowsSuffix = "-windows";

    /// <summary>Prefix for legacy UAP TFMs.</summary>
    private const string UapPrefix = "uap";

    /// <summary>Character used to separate the base TFM from the platform suffix.</summary>
    private const char PlatformSeparator = '-';

    /// <summary>
    /// Shared <see cref="FrameworkReducer"/> instance. <see cref="FrameworkReducer"/> is thread-safe
    /// and reusable; one instance amortises the internal lookup tables across every call.
    /// </summary>
    private static readonly FrameworkReducer _frameworkReducer = new();

    /// <summary>Version-aware comparer -- embedded digits compare as numbers (net10 > net9).</summary>
    private static readonly StringComparer _versionAware = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering);

    /// <summary>
    /// Lowest <see cref="System.Version"/> form of <c>net462</c> that's
    /// still in scope for modern walks -- net462 is the .NET Framework
    /// floor that supports netstandard 2.0 type forwards and ships ref
    /// packs in current SDKs. Anything strictly older is treated as
    /// legacy by <see cref="IsLegacyOnlyTfm"/>.
    /// </summary>
    private static readonly Version SupportedDotNetFrameworkFloor = new(4, 6, 2, 0);

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
    /// Returns true when every entry in <paramref name="availableTfms"/>
    /// targets a framework that modern .NET consumers can no longer
    /// run -- Mono / Xamarin / Silverlight / Windows Phone / portable
    /// profiles / .NET Framework prior to 5. Used by the fetcher to
    /// downgrade the "no supported TFM" warning on packages that
    /// genuinely have nothing for a net6+ consumer (e.g. legacy
    /// <c>System.Net.Primitives</c>, <c>System.Globalization.Extensions</c>);
    /// without the downgrade these saturate the log on every real-
    /// world ReactiveUI / Avalonia run.
    /// </summary>
    /// <param name="availableTfms">TFM directory names as shipped by the package.</param>
    /// <returns>True when no TFM is reachable from a modern .NET / netstandard target.</returns>
    public static bool HasOnlyLegacyTfms(IReadOnlyList<string> availableTfms)
    {
        ArgumentNullException.ThrowIfNull(availableTfms);

        if (availableTfms is [])
        {
            return false;
        }

        for (var i = 0; i < availableTfms.Count; i++)
        {
            if (!IsLegacyOnlyTfm(availableTfms[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns every TFM in <paramref name="availableTfms"/> that is
    /// runtime-compatible with <paramref name="targetTfm"/> per
    /// NuGet's <see cref="DefaultCompatibilityProvider"/>. This is the
    /// "what dirs should the assembly resolver fall back to" question
    /// -- a project targeting <c>net8.0</c> can pull in DLLs that ship
    /// <c>net6.0</c>, <c>netstandard2.0</c>, etc., even though
    /// <see cref="SelectAllSupportedTfms"/> won't have extracted them
    /// to the <c>net8.0/</c> bucket.
    /// </summary>
    /// <param name="targetTfm">The consuming TFM (e.g. the lib bucket the walker is currently scanning).</param>
    /// <param name="availableTfms">All TFM directory names present under <c>lib/</c>.</param>
    /// <returns>The compatible TFMs, ordered exact-match first, then by descending TFM rank (newer first).</returns>
    public static List<string> SelectCompatibleTfms(string targetTfm, List<string> availableTfms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTfm);
        ArgumentNullException.ThrowIfNull(availableTfms);

        if (availableTfms is [])
        {
            return [];
        }

        var target = NuGetFramework.Parse(targetTfm);
        if (target.IsUnsupported)
        {
            return [];
        }

        var provider = DefaultCompatibilityProvider.Instance;
        var compatible = new List<string>(availableTfms.Count);
        for (var i = 0; i < availableTfms.Count; i++)
        {
            var raw = availableTfms[i];
            var candidate = NuGetFramework.Parse(raw);
            if (candidate.IsUnsupported)
            {
                continue;
            }

            if (provider.IsCompatible(target, candidate))
            {
                compatible.Add(raw);
            }
        }

        compatible.Sort(static (a, b) =>
        {
            var aRank = Tfm.Parse(a).Rank;
            var bRank = Tfm.Parse(b).Rank;
            return bRank.CompareTo(aRank);
        });

        return compatible;
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

        if (TrySelectOverrideTfm(availableTfms, tfmOverride, out var overrideMatch))
        {
            return overrideMatch;
        }

        if (TrySelectPreferredTfm(availableTfms, tfmPreference, out var preferredMatch))
        {
            return preferredMatch;
        }

        return FindBestNetStandardFallback(availableTfms);
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
        if (libTfm.AsSpan().IndexOf(PlatformSeparator) is > 0 and var dashIdx)
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
        _ when tfm.Contains(AndroidSuffix, StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith(MonoAndroidPrefix, StringComparison.OrdinalIgnoreCase) => AndroidPlatform,
        _ when tfm.Contains(IosSuffix, StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith(XamarinIosPrefix, StringComparison.OrdinalIgnoreCase) => IosPlatform,
        _ when tfm.Contains(MacCatalystSuffix, StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith(XamarinMacPrefix, StringComparison.OrdinalIgnoreCase) => MacCatalystPlatform,
        _ when tfm.Contains(WindowsSuffix, StringComparison.OrdinalIgnoreCase)
               || tfm.StartsWith(UapPrefix, StringComparison.OrdinalIgnoreCase) => WindowsPlatform,
        _ => null,
    };

        /// <summary>
    /// Resolves a per-package override against the available TFMs.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmOverride">Optional per-package TFM override.</param>
    /// <param name="match">Resolved override match, if found.</param>
    /// <returns>True when the override matched an available TFM.</returns>
    internal static bool TrySelectOverrideTfm(List<string> availableTfms, string? tfmOverride, out string? match)
    {
        match = null;
        if (tfmOverride is not { } requestedOverride)
        {
            return false;
        }

        match = FirstExactMatch(availableTfms, requestedOverride)
            ?? FirstPrefixMatch(availableTfms, requestedOverride);
        return match is not null;
    }

    /// <summary>
    /// Resolves the first preferred TFM across exact, prefix, and major-version matches.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmPreference">Ordered list of preferred TFMs.</param>
    /// <param name="match">Resolved preferred match, if found.</param>
    /// <returns>True when a preferred TFM matched.</returns>
    internal static bool TrySelectPreferredTfm(List<string> availableTfms, string[] tfmPreference, out string? match)
    {
        match = FindPreferredExactMatch(availableTfms, tfmPreference)
            ?? FindPreferredPrefixMatch(availableTfms, tfmPreference)
            ?? FindPreferredMajorVersionMatch(availableTfms, tfmPreference);
        return match is not null;
    }

    /// <summary>
    /// Finds the first exact preferred TFM match.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmPreference">Ordered list of preferred TFMs.</param>
    /// <returns>The matched TFM, or null.</returns>
    internal static string? FindPreferredExactMatch(List<string> availableTfms, string[] tfmPreference)
    {
        for (var i = 0; i < tfmPreference.Length; i++)
        {
            if (FirstExactMatch(availableTfms, tfmPreference[i]) is { } exact)
            {
                return exact;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first prefix-based preferred TFM match.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmPreference">Ordered list of preferred TFMs.</param>
    /// <returns>The matched TFM, or null.</returns>
    internal static string? FindPreferredPrefixMatch(List<string> availableTfms, string[] tfmPreference)
    {
        for (var i = 0; i < tfmPreference.Length; i++)
        {
            if (FirstPrefixMatch(availableTfms, tfmPreference[i]) is { } prefix)
            {
                return prefix;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first preferred TFM whose family and major version match.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="tfmPreference">Ordered list of preferred TFMs.</param>
    /// <returns>The matched TFM, or null.</returns>
    internal static string? FindPreferredMajorVersionMatch(List<string> availableTfms, string[] tfmPreference)
    {
        for (var i = 0; i < tfmPreference.Length; i++)
        {
            if (FindMajorVersionMatch(availableTfms, tfmPreference[i]) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first available TFM that matches the preferred family and major version.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <param name="preferredTfm">Preferred TFM to match against.</param>
    /// <returns>The matched TFM, or null.</returns>
    internal static string? FindMajorVersionMatch(List<string> availableTfms, string preferredTfm)
    {
        var parsedPreference = Tfm.Parse(preferredTfm);
        for (var i = 0; i < availableTfms.Count; i++)
        {
            var available = availableTfms[i];
            var availableTfm = Tfm.Parse(available);
            if (availableTfm.Family == parsedPreference.Family
                && availableTfm.Version.StartsWith(parsedPreference.Version, StringComparison.Ordinal))
            {
                return available;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the highest netstandard fallback TFM, if any.
    /// </summary>
    /// <param name="availableTfms">TFMs present in the package's lib/ directory.</param>
    /// <returns>The highest netstandard fallback TFM, or null.</returns>
    internal static string? FindBestNetStandardFallback(List<string> availableTfms)
    {
        string? bestNetstandard = null;
        for (var i = 0; i < availableTfms.Count; i++)
        {
            var tfm = availableTfms[i];
            if (!tfm.IsNetStandardFallback())
            {
                continue;
            }

            if (bestNetstandard is null || _versionAware.Compare(tfm, bestNetstandard) > 0)
            {
                bestNetstandard = tfm;
            }
        }

        return bestNetstandard;
    }

    /// <summary>
    /// Slow path for <see cref="FindBestRefsTfm"/>: defers to
    /// <see cref="FrameworkReducer"/> for cases the string fast paths
    /// cannot answer (netstandard fallback, version downscaling).
    /// </summary>
    /// <param name="libTfm">The TFM under lib/ being resolved.</param>
    /// <param name="refsTfms">All TFMs present under refs/.</param>
    /// <returns>The best matching reference TFM, or null if none found.</returns>
    internal static string? FindBestRefsTfmSlow(string libTfm, List<string> refsTfms)
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
    internal static string? PickHighestModernNetRef(List<NuGetFramework> candidates, Dictionary<NuGetFramework, string> byFramework)
    {
        NuGetFramework? best = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate is { Framework: not FrameworkConstants.FrameworkIdentifiers.NetCoreApp } or { Version.Major: < ModernNetMajorVersion })
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
    /// Returns true when <paramref name="tfm"/> targets a framework
    /// that modern .NET / netstandard consumers cannot reach. Used by
    /// <see cref="HasOnlyLegacyTfms"/> to detect packages that ship
    /// only Xamarin / Mono / Silverlight / Windows Phone / pre-net462
    /// .NET Framework / portable-profile assets.
    /// </summary>
    /// <param name="tfm">TFM directory name to classify.</param>
    /// <returns>True for legacy-only TFMs; false for any supported modern .NET / netstandard / net462+ variant.</returns>
    internal static bool IsLegacyOnlyTfm(string tfm) => tfm is [_, ..] && NuGetFramework.Parse(tfm) switch
    {
        // Unparseable / unknown framework: treat as legacy so we
        // don't keep warning. The fetcher couldn't have picked
        // anything from it anyway.
        { IsUnsupported: true } => true,
        { Framework: FrameworkConstants.FrameworkIdentifiers.NetStandard } => false,
        { Framework: FrameworkConstants.FrameworkIdentifiers.NetCoreApp } => false,

        // The "Net" identifier covers BOTH .NET Framework (1.x-4.x)
        // and modern .NET (5.0+, where the moniker was reused). Modern
        // .NET is always supported (Major >= 5). On .NET Framework we
        // mark anything older than 4.6.2 as legacy -- net462 is the
        // floor that supports netstandard 2.0 and gets first-class
        // ref packs / type forwards under modern dotnet SDKs.
        { Framework: FrameworkConstants.FrameworkIdentifiers.Net } parsed => IsLegacyDotNetFramework(parsed.Version),

        // Everything else (Xamarin*, MonoAndroid, MonoTouch,
        // Silverlight, WindowsPhone*, Windows Store, UAP, portable-*).
        _ => true,
    };

    /// <summary>
    /// Returns true when <paramref name="version"/> represents a .NET
    /// Framework version older than the supported floor of net462.
    /// Modern .NET (5.0+) reuses the same identifier and is handled
    /// by the caller before this is reached.
    /// </summary>
    /// <param name="version">Version component of a parsed <c>.NETFramework</c> moniker.</param>
    /// <returns>True for net4x older than 4.6.2 and any net1x / net2x / net3x.</returns>
    internal static bool IsLegacyDotNetFramework(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        // Modern .NET (5.0+) is unconditionally supported.
        if (version.Major >= ModernNetMajorVersion)
        {
            return false;
        }

        // Pre-net462 (e.g. net20, net35, net40, net45, net451, net46, net461) is legacy.
        return version < SupportedDotNetFrameworkFloor;
    }

    /// <summary>
    /// Returns true when the supplied TFM matches any entry in the preference
    /// list. Handles exact matches, prefix matches, and major version matches.
    /// </summary>
    /// <param name="tfm">TFM to test.</param>
    /// <param name="tfmPreference">Preference list.</param>
    /// <returns>True if a match is found; otherwise, false.</returns>
    internal static bool MatchesAnyPreference(string tfm, string[] tfmPreference)
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
    internal static string? FirstExactMatch(List<string> candidates, string target)
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
    internal static string? FirstPrefixMatch(List<string> candidates, string target)
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
