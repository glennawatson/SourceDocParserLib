// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Resolves merged <see cref="ApiDocumentation"/> for a Roslyn
/// <see cref="ISymbol"/>. Implementations are expected to memoise
/// per-symbol parses so a single walk of an assembly only pays the
/// XML-parse cost once per symbol, even when many <c>inheritdoc</c>
/// chains converge on the same parent.
/// </summary>
public interface IDocResolver
{
    /// <summary>
    /// Returns the resolved documentation for <paramref name="symbol"/>,
    /// honouring explicit <c>&lt;inheritdoc/&gt;</c> tags and the
    /// auto-inheritance rule for empty-doc overrides / interface impls.
    /// </summary>
    /// <param name="symbol">Symbol whose documentation to resolve.</param>
    /// <returns>The resolved documentation, or <see cref="ApiDocumentation.Empty"/> when nothing is available.</returns>
    ApiDocumentation Resolve(ISymbol symbol);
}
