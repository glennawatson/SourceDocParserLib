// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.Common;

/// <summary>
/// Mirrors docfx's own <c>IsCompilerGeneratedDisplayClass</c>
/// heuristic: any metadata name containing an angle bracket is a
/// mangled identifier (display classes, async / iterator state
/// machines, anonymous types, lambda closures, backing fields).
/// Filtering happens at the presentation layer so the walker stays
/// faithful for any other consumer.
/// </summary>
public static class CompilerGeneratedNames
{
    /// <summary>Tests whether <paramref name="symbolName"/> is an angle-bracket mangled identifier.</summary>
    /// <param name="symbolName">The metadata name to test.</param>
    /// <returns>True when the symbol should be skipped from rendered output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCompilerGenerated(string symbolName) =>
        symbolName.AsSpan().IndexOfAny('<', '>') >= 0;

    /// <summary>Span-based overload — lets call sites that already hold a span avoid the implicit AsSpan().</summary>
    /// <param name="symbolName">The metadata name to test.</param>
    /// <returns>True when the symbol should be skipped from rendered output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCompilerGenerated(ReadOnlySpan<char> symbolName) =>
        symbolName.IndexOfAny('<', '>') >= 0;
}
