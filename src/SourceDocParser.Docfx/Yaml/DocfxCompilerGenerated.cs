// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Common;

namespace SourceDocParser.Docfx;

/// <summary>
/// Docfx-side shim over <see cref="CompilerGeneratedNames.IsCompilerGenerated"/>
/// — kept to preserve the docfx call-site naming and IDE jump-to-definition
/// inside the docfx layer; the rule itself now lives in Common so every
/// emitter shares the same heuristic.
/// </summary>
internal static class DocfxCompilerGenerated
{
    /// <summary>Tests whether <paramref name="symbolName"/> is an angle-bracket mangled identifier.</summary>
    /// <param name="symbolName">The metadata name to test.</param>
    /// <returns>True when the symbol should be skipped from the YAML output.</returns>
    public static bool IsCompilerGenerated(string symbolName) =>
        CompilerGeneratedNames.IsCompilerGenerated(symbolName);
}
