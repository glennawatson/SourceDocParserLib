// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Model;

/// <summary>
/// Result of a direct-mode extraction — the merged
/// <see cref="ApiType"/> array is returned to the caller in memory
/// instead of being handed to an <see cref="IDocumentationEmitter"/>.
/// </summary>
/// <remarks>
/// Use this when the consumer renders pages itself (e.g. a static-
/// site pipeline that wants the structured API model rather than a
/// pre-rendered Markdown round-trip). The standard
/// <see cref="ExtractionResult"/> shape (page-count summary) is
/// still produced by <see cref="IMetadataExtractor.RunAsync(IAssemblySource, string, IDocumentationEmitter)"/>;
/// this record is the parallel return for
/// <see cref="IMetadataExtractor.ExtractAsync(IAssemblySource)"/>.
/// </remarks>
/// <param name="CanonicalTypes">
/// The merged canonical types after the per-TFM walk and cross-TFM
/// merge. Treat as immutable; the array is sized to the merged count
/// so consumers can index it without copying.
/// </param>
/// <param name="LoadFailures">
/// The number of assemblies that failed to load into a Roslyn
/// compilation during the walk; non-fatal (the extractor proceeds
/// with whatever loaded), but worth surfacing in build logs.
/// </param>
/// <param name="SourceLinks">
/// SourceLink audit pairs (UID + resolved URL) collected from the
/// merged catalog; suitable input for an external link checker.
/// </param>
public sealed record DirectExtractionResult(
    ApiType[] CanonicalTypes,
    int LoadFailures,
    SourceLinkEntry[] SourceLinks);
