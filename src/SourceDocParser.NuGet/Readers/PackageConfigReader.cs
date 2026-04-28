// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Reader for <c>nuget-packages.json</c>. Streams the file as UTF-8
/// bytes into a single <see cref="JsonDocument"/> and walks each known
/// property by name, so no <see cref="JsonSerializer"/> reflection or
/// source-generator infrastructure is needed.
/// </summary>
internal static class PackageConfigReader
{
    /// <summary>Strict parse options -- duplicate keys throw rather than silently last-one-wins.</summary>
    private static readonly JsonDocumentOptions _strictOptions = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// Reads <c>nuget-packages.json</c> at <paramref name="path"/> and
    /// materialises a <see cref="PackageConfig"/>.
    /// </summary>
    /// <param name="path">Absolute path to the configuration file.</param>
    /// <returns>The populated <see cref="PackageConfig"/>.</returns>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is null, empty, or whitespace.</exception>
    /// <exception cref="JsonException">When the document is not a JSON object or required arrays are malformed.</exception>
    public static PackageConfig Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
        using var doc = JsonDocument.Parse(stream, _strictOptions);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected the root of {path} to be a JSON object.");
        }

        return new(
            NugetPackageOwners: ReadStringArray(root, "nugetPackageOwners"u8),
            TfmPreference: ReadStringArray(root, "tfmPreference"u8),
            AdditionalPackages: ReadAdditionalPackages(root),
            ExcludePackages: ReadStringArray(root, "excludePackages"u8),
            ExcludePackagePrefixes: ReadStringArray(root, "excludePackagePrefixes"u8),
            ReferencePackages: ReadReferencePackages(root),
            TfmOverrides: ReadStringDictionary(root, "tfmOverrides"u8));
    }

    /// <summary>
    /// Reads an array of strings under <paramref name="propertyName"/>.
    /// Missing / null properties resolve to an empty array so callers can
    /// rely on a non-null collection without per-call null checks.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <param name="propertyName">UTF-8 encoded property name to read.</param>
    /// <returns>The string values, in document order.</returns>
    private static string[] ReadStringArray(in JsonElement root, in ReadOnlySpan<byte> propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var result = new string[element.GetArrayLength()];
        var array = element.EnumerateArray();
        var i = 0;
        foreach (var item in array)
        {
            result[i++] = item.GetString() ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// Reads <c>additionalPackages</c> as an array of <see cref="AdditionalPackage"/>.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <returns>The additional packages, in document order.</returns>
    private static AdditionalPackage[] ReadAdditionalPackages(in JsonElement root)
    {
        if (!root.TryGetProperty("additionalPackages"u8, out var element) || element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var result = new AdditionalPackage[element.GetArrayLength()];
        var array = element.EnumerateArray();
        var i = 0;
        foreach (var item in array)
        {
            result[i++] = new(
                Id: GetRequiredString(item, "id"u8, "additionalPackages"u8),
                Version: GetOptionalString(item, "version"u8));
        }

        return result;
    }

    /// <summary>
    /// Reads <c>referencePackages</c> as an array of <see cref="ReferencePackage"/>.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <returns>The reference packages, in document order.</returns>
    private static ReferencePackage[] ReadReferencePackages(in JsonElement root)
    {
        if (!root.TryGetProperty("referencePackages"u8, out var element) || element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var result = new ReferencePackage[element.GetArrayLength()];
        var array = element.EnumerateArray();
        var i = 0;
        foreach (var item in array)
        {
            result[i++] = new(
                Id: GetRequiredString(item, "id"u8, "referencePackages"u8),
                Version: GetOptionalString(item, "version"u8),
                TargetTfm: GetOptionalString(item, "targetTfm"u8) ?? string.Empty,
                PathPrefix: GetOptionalString(item, "pathPrefix"u8) ?? "ref");
        }

        return result;
    }

    /// <summary>
    /// Reads a string-to-string dictionary under <paramref name="propertyName"/>.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <param name="propertyName">UTF-8 encoded property name to read.</param>
    /// <returns>An ordinal dictionary keyed by JSON property name.</returns>
    private static Dictionary<string, string> ReadStringDictionary(in JsonElement root, in ReadOnlySpan<byte> propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element is not { ValueKind: JsonValueKind.Object })
        {
            return new(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in element.EnumerateObject())
        {
            result[entry.Name] = entry.Value.GetString() ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// Returns the string value of a required property, throwing when it is missing or not a string.
    /// </summary>
    /// <param name="element">Object containing the property.</param>
    /// <param name="propertyName">UTF-8 encoded property name to read.</param>
    /// <param name="parentPath">Parent property name, included in the exception message for context.</param>
    /// <returns>The required string value.</returns>
    /// <exception cref="JsonException">When the property is missing, null, or not a string.</exception>
    private static string GetRequiredString(in JsonElement element, in ReadOnlySpan<byte> propertyName, in ReadOnlySpan<byte> parentPath)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value is not { ValueKind: JsonValueKind.String })
        {
            throw new JsonException($"Required string property '{GetDisplayName(propertyName)}' missing or not a string under '{GetDisplayName(parentPath)}'.");
        }

        return value.GetString() ?? throw new JsonException($"Required string property '{GetDisplayName(propertyName)}' under '{GetDisplayName(parentPath)}' was null.");
    }

    /// <summary>
    /// Returns the string value of an optional property, or null if the property is absent or not a string.
    /// </summary>
    /// <param name="element">Object containing the property.</param>
    /// <param name="propertyName">UTF-8 encoded property name to read.</param>
    /// <returns>The string value, or null.</returns>
    private static string? GetOptionalString(in JsonElement element, in ReadOnlySpan<byte> propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value is { ValueKind: JsonValueKind.String }
            ? value.GetString()
            : null;

    /// <summary>
    /// Gets a human-readable display name for a UTF-8 property name.
    /// </summary>
    /// <param name="propertyName">The UTF-8 property name.</param>
    /// <returns>A string representation of the property name.</returns>
    private static string GetDisplayName(in ReadOnlySpan<byte> propertyName)
    {
        if (TryGetPackagePropertyDisplayName(propertyName, out var displayName))
        {
            return displayName;
        }

        if (TryGetTopLevelPropertyDisplayName(propertyName, out displayName))
        {
            return displayName;
        }

        return "unknown";
    }

    /// <summary>
    /// Tries to map a package-object property name to its display name.
    /// </summary>
    /// <param name="propertyName">UTF-8 property name.</param>
    /// <param name="displayName">Mapped display name.</param>
    /// <returns>True when the property name is recognised.</returns>
    private static bool TryGetPackagePropertyDisplayName(in ReadOnlySpan<byte> propertyName, out string displayName)
    {
        displayName = propertyName switch
        {
            _ when propertyName.SequenceEqual("id"u8) => "id",
            _ when propertyName.SequenceEqual("version"u8) => "version",
            _ when propertyName.SequenceEqual("targetTfm"u8) => "targetTfm",
            _ when propertyName.SequenceEqual("pathPrefix"u8) => "pathPrefix",
            _ => string.Empty,
        };

        return displayName.Length > 0;
    }

    /// <summary>
    /// Tries to map a top-level config property name to its display name.
    /// </summary>
    /// <param name="propertyName">UTF-8 property name.</param>
    /// <param name="displayName">Mapped display name.</param>
    /// <returns>True when the property name is recognised.</returns>
    private static bool TryGetTopLevelPropertyDisplayName(in ReadOnlySpan<byte> propertyName, out string displayName)
    {
        displayName = propertyName switch
        {
            _ when propertyName.SequenceEqual("additionalPackages"u8) => "additionalPackages",
            _ when propertyName.SequenceEqual("referencePackages"u8) => "referencePackages",
            _ when propertyName.SequenceEqual("nugetPackageOwners"u8) => "nugetPackageOwners",
            _ when propertyName.SequenceEqual("tfmPreference"u8) => "tfmPreference",
            _ when propertyName.SequenceEqual("excludePackages"u8) => "excludePackages",
            _ when propertyName.SequenceEqual("excludePackagePrefixes"u8) => "excludePackagePrefixes",
            _ when propertyName.SequenceEqual("tfmOverrides"u8) => "tfmOverrides",
            _ => string.Empty,
        };

        return displayName.Length > 0;
    }
}
