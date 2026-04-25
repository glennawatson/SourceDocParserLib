// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx;

/// <summary>
/// Strongly-typed model of the subset of <c>docfx.json</c> that the build
/// generates and rewrites. Top-level docfx properties we don't touch are
/// not modelled because the generator produces a complete file from the
/// template each run.
/// </summary>
/// <param name="Metadata">Ordered list of metadata entries — one per lib TFM that has matching reference assemblies.</param>
/// <param name="Build">The build section, copied from the template with the content array patched to include platform-specific outputs.</param>
public sealed record DocfxConfig(
    List<DocfxMetadataEntry> Metadata,
    DocfxBuildSection Build);
