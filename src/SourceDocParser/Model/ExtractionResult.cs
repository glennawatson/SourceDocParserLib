// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Model;

/// <summary>
/// Represents the result of a metadata extraction operation, encapsulating
/// information about the number of types processed, the total Markdown pages emitted,
/// the number of load failures, and source links generated during the extraction process.
/// </summary>
/// <remarks>
/// This record is immutable and is intended to provide the output summary of
/// a metadata extraction task. It includes information about cross-target framework
/// merges, documentation generation, and assembly loading issues encountered.
/// </remarks>
/// <param name="CanonicalTypes">
/// The total number of distinct types after processing and merging across different
/// target framework monikers (TFMs).
/// </param>
/// <param name="PagesEmitted">
/// The total number of Markdown pages written as part of the extraction process.
/// This includes one page per type and one per overload group.
/// </param>
/// <param name="LoadFailures">
/// The number of assemblies that failed to be loaded into a compilation during the extraction process.
/// </param>
/// <param name="SourceLinks">
/// An array containing pairs of symbol UIDs and their corresponding source URLs.
/// This array may be empty if no SourceLink data was available. The data is directly
/// passed to a validator for further processing.
/// </param>
public sealed record ExtractionResult(
    int CanonicalTypes,
    int PagesEmitted,
    int LoadFailures,
    SourceLinkEntry[] SourceLinks);
