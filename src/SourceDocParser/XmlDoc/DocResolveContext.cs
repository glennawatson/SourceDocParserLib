// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Walk;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Bundle of per-resolver dependencies threaded through every
/// <see cref="DocResolver"/> static helper. Mirrors the
/// <see cref="SymbolWalkContext"/> pattern so the helpers can stay
/// pure / testable while the public <see cref="DocResolver.Resolve"/>
/// seam is the only instance method.
/// </summary>
/// <param name="Compilation">Compilation used for cref resolution.</param>
/// <param name="Cache">Per-symbol documentation cache scoped to one resolver instance and never shared across parallel resolves.</param>
internal sealed record DocResolveContext(
    Microsoft.CodeAnalysis.Compilation Compilation,
    DocResolveCache Cache);
