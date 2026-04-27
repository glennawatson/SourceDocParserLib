// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using SourceDocParser.Merge;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser;

/// <summary>
/// Represents the context used in the processing and walking of loaded assembly symbols.
/// </summary>
/// <remarks>
/// The <c>WalkContext</c> serves as a container for all required dependencies and shared state
/// necessary during the walking of symbols across multiple assemblies.
/// </remarks>
/// <param name="SymbolWalker">
/// An implementation of <see cref="ISymbolWalker"/> responsible for processing symbols in each loaded assembly.
/// </param>
/// <param name="SourceLinkResolverFactory">
/// A factory method for creating per-assembly instances of <see cref="ISourceLinkResolver"/>, which resolves source-link information.
/// </param>
/// <param name="Logger">
/// An instance of <c>ILogger</c> used for logging progress updates, errors, and other diagnostic information.
/// </param>
/// <param name="Merger">
/// An instance of <see cref="StreamingTypeMerger"/> responsible for combining and merging type data across assemblies.
/// </param>
/// <param name="CatalogCount">
/// A counter tracking the number of successfully walked catalogs, wrapped in a <see cref="StrongBox{T}"/> for mutable storage.
/// </param>
/// <param name="LoadFailures">
/// A counter tracking the number of worker failures encountered during the walking process, wrapped in a <see cref="StrongBox{T}"/> for mutable storage.
/// </param>
internal sealed record WalkContext(
    ISymbolWalker SymbolWalker,
    Func<string, ISourceLinkResolver> SourceLinkResolverFactory,
    ILogger Logger,
    StreamingTypeMerger Merger,
    StrongBox<int> CatalogCount,
    StrongBox<int> LoadFailures);
