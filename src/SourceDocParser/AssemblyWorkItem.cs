// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// One assembly to walk along with the TFM group it belongs to and
/// the shared <see cref="WalkContext"/> the parallel lambda needs.
/// Bundling the context onto the item lets the lambda be <c>static</c>
/// -- no captures, no per-dispatch closure allocation.
/// </summary>
/// <param name="Owner">Owning TFM group (provides fallback index and loader).</param>
/// <param name="AssemblyPath">Absolute path to the assembly to walk.</param>
/// <param name="Context">Shared dependencies + result accumulators.</param>
internal sealed record AssemblyWorkItem(TfmGroup Owner, string AssemblyPath, WalkContext Context);
