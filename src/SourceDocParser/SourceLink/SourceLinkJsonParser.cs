// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;

namespace SourceDocParser.SourceLink;

/// <summary>
/// Parses the SourceLink JSON map embedded in a portable PDB into a
/// list of <see cref="SourceLinkMapEntry"/>. Lifted out of
/// <see cref="SourceLinkReader"/> so the JSON parsing rules -- the
/// <c>documents</c> object, the trailing-asterisk wildcard convention,
/// and the skip rules for malformed entries -- read at problem-domain
/// level and are testable in isolation against synthetic JSON without
/// needing a real PDB.
/// </summary>
internal static class SourceLinkJsonParser
{
    /// <summary>
    /// Parses the SourceLink JSON body into a sequence of entries in
    /// declaration order. Yields nothing when the root isn't a JSON
    /// object, when the <c>documents</c> property is missing or not an
    /// object, or when the JSON is malformed (the caller catches any
    /// thrown exceptions). Per the SourceLink spec, an entry whose
    /// local pattern and URL pattern both end in <c>*</c> is a wildcard
    /// substitution; otherwise it's an exact-match entry.
    /// </summary>
    /// <param name="utf8Json">SourceLink JSON body in UTF-8.</param>
    /// <returns>Lazy enumeration of SourceLink map entries in declaration order.</returns>
    public static IEnumerable<SourceLinkMapEntry> Parse(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (!document.RootElement.TryGetProperty("documents"u8, out var documents) || documents.ValueKind is not JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in documents.EnumerateObject())
        {
            var localPattern = property.Name;
            if (localPattern is not [_, ..]
                || property.Value.ValueKind is not JsonValueKind.String
                || property.Value.GetString() is not [_, ..] urlPattern)
            {
                continue;
            }

            if (localPattern is [.., '*'] && urlPattern is [.., '*'])
            {
                yield return new(
                    LocalPrefix: localPattern[..^1],
                    UrlPrefix: urlPattern[..^1],
                    IsWildcard: true);
            }
            else
            {
                yield return new(
                    LocalPrefix: localPattern,
                    UrlPrefix: urlPattern,
                    IsWildcard: false);
            }
        }
    }
}
