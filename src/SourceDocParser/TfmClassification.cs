// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using NuGet.Frameworks;

namespace SourceDocParser;

/// <summary>
/// Provides predicates for classifying Target Framework Monikers (TFMs).
/// </summary>
/// <remarks>
/// This helper centralizes TFM rules to keep call sites readable and ensures
/// rules are updated in one place as new .NET versions emerge. Methods are
/// implemented as pure-function extensions for performance and inlining.
/// </remarks>
public static class TfmClassification
{
    /// <summary>
    /// Determines whether the specified TFM is a modern .NET version.
    /// </summary>
    /// <param name="tfm">The TFM identifier to classify.</param>
    /// <returns><c>true</c> if the TFM belongs to the modern .NET family; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Covers net5.0 and onwards, including platform-specific variants.
    /// </remarks>
    public static bool IsModernNet(this string tfm) => Tfm.Parse(tfm).IsModernNet;

    /// <summary>
    /// Determines whether the specified TFM is a .NET Framework version.
    /// </summary>
    /// <param name="tfm">The TFM identifier to classify.</param>
    /// <returns><c>true</c> if the TFM is a .NET Framework version; otherwise, <c>false</c>.</returns>
    public static bool IsNetFramework(this string tfm) => Tfm.Parse(tfm).IsNetFramework;

    /// <summary>
    /// Determines whether the specified TFM is a .NET Standard version.
    /// </summary>
    /// <param name="tfm">The TFM identifier to classify.</param>
    /// <returns><c>true</c> if the TFM is .NET Standard; otherwise, <c>false</c>.</returns>
    public static bool IsNetStandard(this string tfm) => Tfm.Parse(tfm).IsNetStandard;

    /// <summary>
    /// Determines whether the specified TFM is actively supported as a primary target.
    /// </summary>
    /// <param name="tfm">The TFM identifier to classify.</param>
    /// <returns><c>true</c> if the TFM is modern .NET or .NET Framework; otherwise, <c>false</c>.</returns>
    public static bool IsSupported(this string tfm)
    {
        var parsed = Tfm.Parse(tfm);
        return parsed.IsModernNet || parsed.IsNetFramework;
    }

    /// <summary>
    /// Determines whether the specified TFM has a platform-specific suffix.
    /// </summary>
    /// <param name="tfm">The TFM identifier to classify.</param>
    /// <returns><c>true</c> if the TFM contains a platform suffix; otherwise, <c>false</c>.</returns>
    public static bool IsPlatformSpecific(this string tfm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tfm);
        return NuGetFramework.Parse(tfm).HasPlatform;
    }

    /// <summary>
    /// Determines whether the specified TFM should be used as a .NET Standard fallback.
    /// </summary>
    /// <param name="tfm">The TFM identifier to classify.</param>
    /// <returns><c>true</c> if the TFM is a .NET Standard fallback; otherwise, <c>false</c>.</returns>
    public static bool IsNetStandardFallback(this string tfm) => tfm.IsNetStandard();
}
