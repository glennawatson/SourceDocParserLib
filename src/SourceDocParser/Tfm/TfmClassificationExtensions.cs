// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.Tfm;

/// <summary>Provides predicates for classifying Target Framework Monikers (TFMs).</summary>
/// <remarks>
/// This helper centralizes TFM rules to keep call sites readable and ensures
/// rules are updated in one place as new .NET versions emerge. Methods are
/// implemented as pure-function extensions for performance and inlining.
/// </remarks>
public static class TfmClassificationExtensions
{
    /// <summary>Extension members for <c>string</c>.</summary>
    /// <param name="tfm">Target framework moniker to classify.</param>
    extension(string tfm)
    {
        /// <summary>Determines whether the specified TFM is a .NET Standard version.</summary>
        /// <returns><c>true</c> if the TFM is .NET Standard; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNetStandard() => Tfm.Parse(tfm).IsNetStandard;

        /// <summary>Determines whether the specified TFM should be used as a .NET Standard fallback.</summary>
        /// <returns><c>true</c> if the TFM is a .NET Standard fallback; otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNetStandardFallback() => tfm.IsNetStandard();
    }
}
