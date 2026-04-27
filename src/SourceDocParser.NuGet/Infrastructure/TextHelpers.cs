// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Shared text helpers for low-allocation string checks in the NuGet package.
/// </summary>
internal static class TextHelpers
{
    /// <summary>
    /// Returns true when <paramref name="value"/> is non-null and non-empty.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value has at least one character.</returns>
    public static bool HasValue([NotNullWhen(true)] string? value) => value is [_, ..];

    /// <summary>
    /// Returns true when <paramref name="value"/> contains at least one non-whitespace character.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is not null, empty, or whitespace.</returns>
    public static bool HasNonWhitespace([NotNullWhen(true)] string? value) =>
        value is { } text && text.AsSpan().Trim() is [_, ..];

    /// <summary>
    /// Ensures the supplied URL ends with a forward slash.
    /// </summary>
    /// <param name="value">URL or path prefix.</param>
    /// <returns>The original value or a slash-suffixed copy.</returns>
    public static string EnsureTrailingSlash(string value) =>
        value.EndsWith('/') ? value : $"{value}/";
}
