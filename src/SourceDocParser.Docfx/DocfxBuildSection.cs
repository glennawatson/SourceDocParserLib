// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SourceDocParser.Docfx;

/// <summary>
/// The docfx <c>build</c> section of the configuration file. Holds the
/// content array we patch with platform-specific entries, plus extension
/// data for everything else (resources, templates, output settings) that we
/// preserve verbatim from the template.
/// </summary>
/// <param name="Content">Ordered list of build content entries. Patched in place: previously-injected platform entries are removed and a fresh set is appended.</param>
public sealed record DocfxBuildSection(DocfxBuildContent[] Content)
{
    /// <summary>
    /// Gets a catch-all bag of non-<c>content</c> properties of the build section
    /// (resources, dest, template, globalMetadata, etc.) round-tripped unchanged.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
