// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;

namespace SourceDocParser.NuGet;

/// <summary>
/// Reader for <c>nuget-packages.json</c>. Streams the file as UTF-8
/// bytes into a single <see cref="JsonDocument"/> and walks each known
/// property by name, so no <see cref="JsonSerializer"/> reflection or
/// source-generator infrastructure is needed.
/// </summary>
internal static class PackageConfigReader
{
    /// <summary>JSON parse options that match the previous deserialiser behaviour.</summary>
    private static readonly JsonDocumentOptions _docOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

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

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var doc = JsonDocument.Parse(stream, _docOptions);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected the root of {path} to be a JSON object.");
        }

        return new(
            NugetPackageOwners: ReadStringArray(root, "nugetPackageOwners"),
            TfmPreference: ReadStringArray(root, "tfmPreference"),
            AdditionalPackages: ReadAdditionalPackages(root),
            ExcludePackages: ReadStringArray(root, "excludePackages"),
            ExcludePackagePrefixes: ReadStringArray(root, "excludePackagePrefixes"),
            ReferencePackages: ReadReferencePackages(root),
            TfmOverrides: ReadStringDictionary(root, "tfmOverrides"));
    }

    /// <summary>
    /// Reads an array of strings under <paramref name="propertyName"/>.
    /// Missing / null properties resolve to an empty array so callers can
    /// rely on a non-null collection without per-call null checks.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <param name="propertyName">Property to read.</param>
    /// <returns>The string values, in document order.</returns>
    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new string[element.GetArrayLength()];
        var i = 0;
        foreach (var item in element.EnumerateArray())
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
    private static AdditionalPackage[] ReadAdditionalPackages(JsonElement root)
    {
        if (!root.TryGetProperty("additionalPackages", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new AdditionalPackage[element.GetArrayLength()];
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            result[i++] = new(
                Id: GetRequiredString(item, "id", "additionalPackages"),
                Version: GetOptionalString(item, "version"));
        }

        return result;
    }

    /// <summary>
    /// Reads <c>referencePackages</c> as an array of <see cref="ReferencePackage"/>.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <returns>The reference packages, in document order.</returns>
    private static ReferencePackage[] ReadReferencePackages(JsonElement root)
    {
        if (!root.TryGetProperty("referencePackages", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new ReferencePackage[element.GetArrayLength()];
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            result[i++] = new(
                Id: GetRequiredString(item, "id", "referencePackages"),
                Version: GetOptionalString(item, "version"),
                TargetTfm: GetOptionalString(item, "targetTfm") ?? string.Empty,
                PathPrefix: GetOptionalString(item, "pathPrefix") ?? "ref");
        }

        return result;
    }

    /// <summary>
    /// Reads a string-to-string dictionary under <paramref name="propertyName"/>.
    /// </summary>
    /// <param name="root">Root JSON object.</param>
    /// <param name="propertyName">Property to read.</param>
    /// <returns>An ordinal dictionary keyed by JSON property name.</returns>
    private static Dictionary<string, string> ReadStringDictionary(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
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
    /// <param name="propertyName">Property to read.</param>
    /// <param name="parentPath">Parent property name, included in the exception message for context.</param>
    /// <returns>The required string value.</returns>
    /// <exception cref="JsonException">When the property is missing, null, or not a string.</exception>
    private static string GetRequiredString(JsonElement element, string propertyName, string parentPath)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Required string property '{propertyName}' missing or not a string under '{parentPath}'.");
        }

        return value.GetString() ?? throw new JsonException($"Required string property '{propertyName}' under '{parentPath}' was null.");
    }

    /// <summary>
    /// Returns the string value of an optional property, or null if the property is absent or not a string.
    /// </summary>
    /// <param name="element">Object containing the property.</param>
    /// <param name="propertyName">Property to read.</param>
    /// <returns>The string value, or null.</returns>
    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
