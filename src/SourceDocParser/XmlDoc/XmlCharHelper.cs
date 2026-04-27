// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Static helpers for XML character classification.
/// </summary>
internal static class XmlCharHelper
{
    /// <summary>True for the four whitespace characters allowed inside an XML start tag.</summary>
    /// <param name="ch">Character to test.</param>
    /// <returns>True when whitespace.</returns>
    public static bool IsWhitespace(char ch) => ch is ' ' or '\t' or '\r' or '\n';
}
