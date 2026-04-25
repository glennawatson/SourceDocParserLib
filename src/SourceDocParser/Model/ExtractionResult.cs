// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser;

/// <summary>
/// Lightweight summary of one <see cref="MetadataExtractor.RunAsync"/>
/// invocation. The Nuke build stashes this on the Build instance so
/// follow-up targets like <c>ValidateSourceLinks</c> can consume the
/// in-memory state without re-walking symbols or reading manifest
/// files from disk.
/// </summary>
/// <param name="CanonicalTypes">Number of types after the cross-TFM merge.</param>
/// <param name="PagesEmitted">Total Markdown pages written (one per type plus one per overload group).</param>
/// <param name="LoadFailures">Number of assemblies that failed to load into a Compilation.</param>
/// <param name="SourceLinks">Every (symbol UID, source URL) pair generated during the walk. Empty when no SourceLink data was available; passed straight to the validator.</param>
public sealed record ExtractionResult(
    int CanonicalTypes,
    int PagesEmitted,
    int LoadFailures,
    List<SourceLinkEntry> SourceLinks);
