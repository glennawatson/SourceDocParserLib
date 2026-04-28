// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Parses a NuGet v3 service-index JSON document and extracts the
/// flat-container endpoint -- the URL the installer hits to
/// download <c>.nupkg</c> files. Same shape as the rest of the
/// readers (focused, testable, no NuGet.Protocol dep).
/// </summary>
internal static class NuGetServiceIndexReader
{
    /// <summary>Strict parse options -- duplicate keys throw rather than silently last-one-wins.</summary>
    private static readonly JsonDocumentOptions _strictDocOptions = new() { AllowDuplicateProperties = false };

    /// <summary>Reads the flat-container URL from <paramref name="indexJson"/>.</summary>
    /// <param name="indexJson">UTF-8 bytes of the v3 service-index document.</param>
    /// <returns>The flat-container base URL ending with <c>/</c>; null when none declared.</returns>
    public static string? ReadFlatContainerUrl(in ReadOnlyMemory<byte> indexJson)
    {
        using var doc = JsonDocument.Parse(indexJson, _strictDocOptions);
        return ReadFlatContainerUrl(doc.RootElement);
    }

    /// <summary>
    /// Reads the flat-container URL from <paramref name="indexJsonStream"/>.
    /// </summary>
    /// <param name="indexJsonStream">UTF-8 stream of the v3 service-index document.</param>
    /// <returns>The flat-container base URL ending with <c>/</c>; null when none declared.</returns>
    public static Task<string?> ReadFlatContainerUrlAsync(Stream indexJsonStream) =>
        ReadFlatContainerUrlAsync(indexJsonStream, CancellationToken.None);

    /// <summary>Reads the flat-container URL from <paramref name="indexJsonStream"/>.</summary>
    /// <param name="indexJsonStream">UTF-8 stream of the v3 service-index document.</param>
    /// <param name="cancellationToken">Token observed across the JSON parse.</param>
    /// <returns>The flat-container base URL ending with <c>/</c>; null when none declared.</returns>
    public static async Task<string?> ReadFlatContainerUrlAsync(Stream indexJsonStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexJsonStream);
        using var doc = await JsonDocument.ParseAsync(indexJsonStream, _strictDocOptions, cancellationToken).ConfigureAwait(false);
        return ReadFlatContainerUrl(doc.RootElement);
    }

    /// <summary>Extracts the flat-container URL from the parsed service-index root object.</summary>
    /// <param name="root">Parsed service-index root.</param>
    /// <returns>The flat-container base URL ending with <c>/</c>; null when none declared.</returns>
    private static string? ReadFlatContainerUrl(in JsonElement root)
    {
        if (!root.TryGetProperty("resources"u8, out var resources) || resources is not { ValueKind: JsonValueKind.Array })
        {
            return null;
        }

        foreach (var resource in resources.EnumerateArray())
        {
            if (!resource.TryGetProperty("@type"u8, out var type)
                || type is not { ValueKind: JsonValueKind.String }
                || !type.ValueEquals("PackageBaseAddress/3.0.0"u8))
            {
                continue;
            }

            if (!resource.TryGetProperty("@id"u8, out var id) || id is not { ValueKind: JsonValueKind.String })
            {
                continue;
            }

            var url = id.GetString();
            if (!TextHelpers.HasNonWhitespace(url))
            {
                continue;
            }

            return TextHelpers.EnsureTrailingSlash(url);
        }

        return null;
    }
}
