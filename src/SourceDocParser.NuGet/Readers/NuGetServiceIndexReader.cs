// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;

namespace SourceDocParser.NuGet;

/// <summary>
/// Parses a NuGet v3 service-index JSON document and extracts the
/// flat-container endpoint — the URL the installer hits to
/// download <c>.nupkg</c> files. Same shape as the rest of the
/// readers (focused, testable, no NuGet.Protocol dep).
/// </summary>
internal static class NuGetServiceIndexReader
{
    /// <summary>
    /// The resource <c>@type</c> identifying the v3 flat-container
    /// download endpoint. Highest-versioned entry wins per
    /// NuGet's own client.
    /// </summary>
    private const string FlatContainerTypePrefix = "PackageBaseAddress/3.0.0";

    /// <summary>Strict parse options — duplicate keys throw rather than silently last-one-wins.</summary>
    private static readonly JsonDocumentOptions _strictDocOptions = new() { AllowDuplicateProperties = false };

    /// <summary>Reads the flat-container URL from <paramref name="indexJson"/>.</summary>
    /// <param name="indexJson">UTF-8 bytes of the v3 service-index document.</param>
    /// <returns>The flat-container base URL ending with <c>/</c>; null when none declared.</returns>
    public static string? ReadFlatContainerUrl(ReadOnlySpan<byte> indexJson)
    {
        using var doc = JsonDocument.Parse(indexJson.ToArray(), _strictDocOptions);

        if (!doc.RootElement.TryGetProperty("resources"u8, out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var resource in resources.EnumerateArray())
        {
            if (!resource.TryGetProperty("@type"u8, out var type) || type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var typeValue = type.GetString();
            if (!FlatContainerTypePrefix.Equals(typeValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!resource.TryGetProperty("@id"u8, out var id) || id.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var url = id.GetString();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            return url.EndsWith('/') ? url : url + "/";
        }

        return null;
    }
}
