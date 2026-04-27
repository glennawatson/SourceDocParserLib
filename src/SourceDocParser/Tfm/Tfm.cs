// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Tfm;

/// <summary>
/// Represents a parsed Target Framework Moniker (TFM).
/// </summary>
/// <param name="Raw">The raw TFM string.</param>
/// <param name="Family">The TFM family (e.g., net, netstandard, net4).</param>
/// <param name="Version">The major.minor version as a string.</param>
/// <param name="Platform">The platform suffix, if any.</param>
public record Tfm(string Raw, string Family, string Version, string? Platform)
{
    /// <summary>
    /// Parsed TFM cache to reduce allocations.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Tfm> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The length of the "netstandard" prefix.
    /// </summary>
    private const int NetStandardPrefixLength = 11;

    /// <summary>
    /// The length of the "net" prefix.
    /// </summary>
    private const int NetPrefixLength = 3;

    /// <summary>
    /// The base rank for modern .NET TFMs.
    /// </summary>
    private const int ModernNetBaseRank = 550;

    /// <summary>
    /// The rank for .NET Standard 2.1.
    /// </summary>
    private const int NetStandard21Rank = 510;

    /// <summary>
    /// The rank for .NET Standard 2.0.
    /// </summary>
    private const int NetStandard20Rank = 500;

    /// <summary>
    /// The default rank for .NET Standard.
    /// </summary>
    private const int NetStandardDefaultRank = 480;

    /// <summary>
    /// The rank for .NET Framework.
    /// </summary>
    private const int NetFrameworkRank = 300;

    /// <summary>
    /// The default rank for unknown TFMs.
    /// </summary>
    private const int DefaultRank = 100;

    /// <summary>
    /// The adjustment to the rank for platform-specific TFMs.
    /// </summary>
    private const int PlatformAdjustment = -5;

    /// <summary>
    /// Gets a value indicating whether this is a modern .NET TFM (net5.0+).
    /// </summary>
    public bool IsModernNet => Family == "net" && Version.AsSpan() is ['5', ..] or { Length: > 1 };

    /// <summary>
    /// Gets a value indicating whether this is a .NET Framework TFM.
    /// </summary>
    public bool IsNetFramework => Family == "net4";

    /// <summary>
    /// Gets a value indicating whether this is a .NET Standard TFM.
    /// </summary>
    public bool IsNetStandard => Family == "netstandard";

    /// <summary>
    /// Gets the pre-calculated rank for this TFM.
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// Parses a raw TFM string into a <see cref="Tfm"/> record.
    /// </summary>
    /// <param name="tfm">The raw TFM string.</param>
    /// <returns>A parsed <see cref="Tfm"/> instance.</returns>
    public static Tfm Parse(string tfm)
    {
        ArgumentNullException.ThrowIfNull(tfm);

        return _cache.GetOrAdd(tfm, static key =>
        {
            var tfmSpan = key.AsSpan();
            var dashIndex = tfmSpan.IndexOf('-');
            var baseTfm = dashIndex >= 0 ? tfmSpan[..dashIndex] : tfmSpan;
            var platform = dashIndex >= 0 ? key[(dashIndex + 1)..] : null;

            var (family, versionString) = baseTfm switch
            {
                _ when baseTfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) => ("netstandard", new(baseTfm[NetStandardPrefixLength..])),
                _ when baseTfm.StartsWith("net4", StringComparison.OrdinalIgnoreCase) => ("net4", new(baseTfm[NetPrefixLength..])),
                _ when baseTfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) => ("net", new(baseTfm[NetPrefixLength..])),
                _ => ("unknown", new string(baseTfm))
            };

            var result = new Tfm(key, family, versionString, platform);
            return result with { Rank = CalculateRank(result) };
        });
    }

    /// <summary>
    /// Calculates a numeric rank for a TFM to determine priority.
    /// </summary>
    /// <param name="tfm">The TFM to rank.</param>
    /// <returns>A numeric rank where higher values indicate higher priority.</returns>
    private static int CalculateRank(Tfm tfm)
    {
        var platformAdjust = tfm.Platform is null ? 0 : PlatformAdjustment;

        return tfm switch
        {
            _ when tfm.IsModernNet => double.TryParse(tfm.Version, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var version)
                ? (int)(version * 100) + platformAdjust
                : ModernNetBaseRank + platformAdjust,
            _ when tfm.IsNetStandard => tfm.Version switch
            {
                "2.1" => NetStandard21Rank,
                "2.0" => NetStandard20Rank,
                _ => NetStandardDefaultRank
            },
            _ when tfm.IsNetFramework => NetFrameworkRank,
            _ => DefaultRank
        };
    }
}
