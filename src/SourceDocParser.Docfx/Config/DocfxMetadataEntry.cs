// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SourceDocParser.Docfx;

/// <summary>
/// One entry in the docfx <c>metadata</c> array. Each entry produces an
/// independent compilation, which is essential for our setup because the
/// .NET Framework and modern .NET TFMs cannot share a single compilation
/// without type-system collisions (mscorlib vs. System.Runtime).
/// </summary>
/// <param name="Src">The set of source directories and files this metadata entry consumes.</param>
/// <param name="Dest">Output directory (relative to the docfx working directory) for the generated YAML.</param>
public sealed record DocfxMetadataEntry(
    DocfxMetadataSource[] Src,
    string Dest)
{
    /// <summary>
    /// Gets a catch-all bag of metadata properties other than <c>src</c> and <c>dest</c>
    /// captured from the template and re-emitted verbatim on each
    /// generated entry. Preserves user-controlled settings such as
    /// filters, references and properties without having to model them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
