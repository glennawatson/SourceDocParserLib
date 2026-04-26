// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SourceDocParser.Docfx;

/// <summary>
/// One entry in the docfx <c>build.content</c> array. Most entries simply
/// list the files docfx should consume; some carry additional settings
/// (such as <c>exclude</c> or <c>src</c>) that are preserved verbatim via
/// <see cref="Extra"/>.
/// </summary>
/// <param name="Files">Optional list of file globs the entry consumes. <see langword="null"/> for entries that only carry extension data.</param>
public sealed record DocfxBuildContent(string[]? Files = null)
{
    /// <summary>
    /// Gets a catch-all bag of additional properties present on the entry in the
    /// template (for example <c>exclude</c>) round-tripped unchanged so we
    /// don't have to model the full docfx schema.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
