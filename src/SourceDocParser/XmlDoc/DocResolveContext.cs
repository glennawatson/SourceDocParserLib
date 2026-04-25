// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Bundle of per-resolver dependencies threaded through every
/// <see cref="DocResolver"/> static helper. Mirrors the
/// <see cref="SymbolWalkContext"/> pattern so the helpers can stay
/// pure / testable while the public <see cref="DocResolver.Resolve"/>
/// seam is the only instance method.
/// </summary>
/// <param name="Compilation">Compilation used for cref resolution.</param>
/// <param name="Converter">Converter used to fold inline doc tags into Markdown.</param>
/// <param name="Cache">Per-symbol documentation cache scoped to one resolver instance.</param>
internal sealed record DocResolveContext(
    Compilation Compilation,
    IXmlDocToMarkdownConverter Converter,
    ConcurrentDictionary<ISymbol, ApiDocumentation> Cache);
