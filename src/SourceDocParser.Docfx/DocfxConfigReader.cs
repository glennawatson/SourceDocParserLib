// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;

namespace SourceDocParser.Docfx;

/// <summary>
/// Hand-written reader that materialises a <see cref="DocfxConfig"/>
/// from a UTF-8 docfx configuration stream. Walks <see cref="JsonDocument"/>
/// elements directly so no reflection or source-generated serialiser
/// is in the path.
/// </summary>
public static class DocfxConfigReader
{
    /// <summary>JSON parse options matching docfx's tolerant style.</summary>
    private static readonly JsonDocumentOptions _docOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses a docfx config object from <paramref name="utf8Stream"/>.
    /// </summary>
    /// <param name="utf8Stream">UTF-8 JSON stream positioned at the document start.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="utf8Stream"/> is null.</exception>
    /// <exception cref="JsonException">When the document is not a JSON object or required arrays are malformed.</exception>
    public static DocfxConfig Read(Stream utf8Stream)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);

        using var doc = JsonDocument.Parse(utf8Stream, _docOptions);
        var root = doc.RootElement;

        if (root is not { ValueKind: JsonValueKind.Object })
        {
            throw new JsonException("Expected the root of the docfx config to be a JSON object.");
        }

        return new(
            Metadata: ReadMetadataArray(root),
            Build: ReadBuildSection(root));
    }

    /// <summary>
    /// Reads the <c>metadata</c> array (or returns an empty list when absent).
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <returns>Ordered metadata entries.</returns>
    private static DocfxMetadataEntry[] ReadMetadataArray(in JsonElement root)
    {
        if (!root.TryGetProperty("metadata"u8, out var array) || array is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var entries = new DocfxMetadataEntry[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item is not { ValueKind: JsonValueKind.Object })
            {
                throw new JsonException("Each metadata entry must be a JSON object.");
            }

            var dest = item.TryGetProperty("dest"u8, out var destEl) && destEl is { ValueKind: JsonValueKind.String }
                ? destEl.GetString() ?? string.Empty
                : string.Empty;
            entries[index++] = new(ReadMetadataSources(item), dest)
            {
                Extra = ReadExtra(item, IsKnownMetadataProperty),
            };
        }

        return entries;
    }

    /// <summary>
    /// Reads the <c>src</c> array on a metadata entry as <see cref="DocfxMetadataSource"/> records.
    /// </summary>
    /// <param name="entry">Metadata entry.</param>
    /// <returns>Ordered source records.</returns>
    private static DocfxMetadataSource[] ReadMetadataSources(in JsonElement entry)
    {
        if (!entry.TryGetProperty("src"u8, out var array) || array is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var sources = new DocfxMetadataSource[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item is not { ValueKind: JsonValueKind.Object })
            {
                throw new JsonException("Each metadata src entry must be a JSON object.");
            }

            var src = item.TryGetProperty("src"u8, out var srcEl) && srcEl is { ValueKind: JsonValueKind.String }
                ? srcEl.GetString() ?? string.Empty
                : string.Empty;
            sources[index++] = new(src, ReadStringArray(item, "files"u8));
        }

        return sources;
    }

    /// <summary>
    /// Reads the <c>build</c> section (or returns an empty section when absent).
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <returns>Parsed build section.</returns>
    private static DocfxBuildSection ReadBuildSection(in JsonElement root)
    {
        if (!root.TryGetProperty("build"u8, out var build) || build is not { ValueKind: JsonValueKind.Object })
        {
            return new([]);
        }

        return new(ReadBuildContent(build))
        {
            Extra = ReadExtra(build, IsKnownBuildProperty),
        };
    }

    /// <summary>
    /// Reads the <c>content</c> array inside the build section.
    /// </summary>
    /// <param name="build">Build section element.</param>
    /// <returns>Ordered content entries.</returns>
    private static DocfxBuildContent[] ReadBuildContent(in JsonElement build)
    {
        if (!build.TryGetProperty("content"u8, out var array) || array is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var entries = new DocfxBuildContent[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item is not { ValueKind: JsonValueKind.Object })
            {
                throw new JsonException("Each build content entry must be a JSON object.");
            }

            var files = item.TryGetProperty("files"u8, out var filesEl) && filesEl is { ValueKind: JsonValueKind.Array }
                ? ReadStringList(filesEl)
                : null;

            entries[index++] = new(files)
            {
                Extra = ReadExtra(item, IsKnownBuildContentProperty),
            };
        }

        return entries;
    }

    /// <summary>
    /// Reads an array of strings under <paramref name="propertyName"/> as a list.
    /// </summary>
    /// <param name="element">Containing object.</param>
    /// <param name="propertyName">UTF-8 encoded property name to read.</param>
    /// <returns>String values in document order.</returns>
    private static string[] ReadStringArray(in JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        return ReadStringList(array);
    }

    /// <summary>
    /// Materialises a JSON array element as a list of strings.
    /// </summary>
    /// <param name="array">JSON array element.</param>
    /// <returns>String values in document order.</returns>
    private static string[] ReadStringList(in JsonElement array)
    {
        var values = new string[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            values[index++] = item.GetString() ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// Collects every property of <paramref name="element"/> not present in
    /// <paramref name="isKnownProperty"/> into a round-trip dictionary. Each value is
    /// cloned so it survives disposal of the source <see cref="JsonDocument"/>.
    /// </summary>
    /// <param name="element">Object whose properties to scan.</param>
    /// <param name="isKnownProperty">Predicate for property names that the typed model handles directly.</param>
    /// <returns>The extension data, or null when there is nothing to round-trip.</returns>
    private static Dictionary<string, JsonElement>? ReadExtra(in JsonElement element, Func<string, bool> isKnownProperty)
    {
        Dictionary<string, JsonElement>? extra = null;

        foreach (var prop in element.EnumerateObject())
        {
            var name = prop.Name;
            if (isKnownProperty(name))
            {
                continue;
            }

            extra ??= new(StringComparer.Ordinal);
            extra[name] = prop.Value.Clone();
        }

        return extra;
    }

    /// <summary>
    /// Checks if the property name is a known metadata property.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if known, false otherwise.</returns>
    private static bool IsKnownMetadataProperty(string propertyName) =>
        propertyName is "src" or "dest";

    /// <summary>
    /// Checks if the property name is a known build property.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if known, false otherwise.</returns>
    private static bool IsKnownBuildProperty(string propertyName) =>
        propertyName is "content";

    /// <summary>
    /// Checks if the property name is a known build content property.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if known, false otherwise.</returns>
    private static bool IsKnownBuildContentProperty(string propertyName) =>
        propertyName is "files";
}
