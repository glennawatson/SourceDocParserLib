// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.TestHelpers;

/// <summary>
/// Extension helpers that normalise a string's line endings before
/// assertions so the same expected literal works whether the
/// production code's <c>StringBuilder.AppendLine</c> emitted CRLF
/// (Windows) or LF (Linux / macOS).
/// </summary>
public static class LineEndings
{
    /// <summary>Returns <paramref name="value"/> with every CRLF folded down to LF; null passes through.</summary>
    /// <param name="value">String to normalise.</param>
    /// <returns>The LF-normalised string.</returns>
    public static string Lf(this string value) =>
        value is null ? value! : value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
