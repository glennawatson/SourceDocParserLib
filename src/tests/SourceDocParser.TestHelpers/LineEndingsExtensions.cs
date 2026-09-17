// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.TestHelpers;

/// <summary>
/// Extension helpers that normalise a string's line endings before
/// assertions so the same expected literal works whether the
/// production code's <c>StringBuilder.AppendLine</c> emitted CRLF
/// (Windows) or LF (Linux / macOS).
/// </summary>
public static class LineEndingsExtensions
{
    /// <summary>Extension members for <c>string</c>.</summary>
    /// <param name="value">Text whose line endings are normalized.</param>
    extension(string value)
    {
        /// <summary>Returns <paramref name="value"/> with every CRLF folded down to LF; null passes through.</summary>
        /// <returns>The LF-normalised string.</returns>
        public string Lf() =>
            value is null ? value! : value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
