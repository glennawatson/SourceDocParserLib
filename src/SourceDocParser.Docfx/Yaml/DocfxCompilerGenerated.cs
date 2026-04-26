// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx;

/// <summary>
/// Mirrors docfx's own <c>IsCompilerGeneratedDisplayClass</c>
/// heuristic: any metadata name containing an angle bracket is a
/// mangled identifier (display classes, async / iterator state
/// machines, anonymous types, lambda closures, backing fields).
/// Filtering happens at the docfx presentation layer so the walker
/// can stay faithful for any other consumer.
/// </summary>
internal static class DocfxCompilerGenerated
{
    /// <summary>Tests whether <paramref name="symbolName"/> is an angle-bracket mangled identifier.</summary>
    /// <param name="symbolName">The metadata name to test.</param>
    /// <returns>True when the symbol should be skipped from the YAML output.</returns>
    public static bool IsCompilerGenerated(string symbolName) =>
        symbolName.AsSpan().IndexOfAny('<', '>') >= 0;
}
