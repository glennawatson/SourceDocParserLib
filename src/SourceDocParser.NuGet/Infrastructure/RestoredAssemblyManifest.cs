// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SourceDocParser.Model;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Transfers exact documentation groups between package acquisition and discovery.</summary>
internal static class RestoredAssemblyManifest
{
    /// <summary>The supported serialized group contract.</summary>
    private const int FormatVersion = 1;

    /// <summary>The package declaration file identifying a discovery request.</summary>
    private const string PackageManifestFileName = "nuget-packages.json";

    /// <summary>The serialized independent group collection.</summary>
    private const string GroupsProperty = "groups";

    /// <summary>The serialized assembly reference map.</summary>
    private const string ReferencesProperty = "references";

    /// <summary>Rejects duplicate JSON properties instead of silently replacing reference selections.</summary>
    private static readonly JsonDocumentOptions _readOptions = new() { AllowDuplicateProperties = false };

    /// <summary>Identifies the restored groups belonging to the current root and package declarations.</summary>
    /// <param name="rootDirectory">Directory containing package declarations.</param>
    /// <param name="apiPath">Directory containing restore outputs.</param>
    /// <param name="cancellationToken">Cancellation for reading package declarations.</param>
    /// <returns>The manifest path, or null when no package declarations exist.</returns>
    internal static async Task<string?> GetPathAsync(string rootDirectory, string apiPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(rootDirectory);
        var configPath = Path.Combine(root, PackageManifestFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }

        var config = await File.ReadAllBytesAsync(configPath, cancellationToken).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(root));
        hash.AppendData([0]);
        hash.AppendData(config);
        return Path.Combine(Path.GetFullPath(apiPath), $".restored-assemblies-{Convert.ToHexStringLower(hash.GetHashAndReset())}.json");
    }

    /// <summary>Publishes complete groups atomically without replacing a valid manifest on failure.</summary>
    /// <param name="path">The manifest path for the current input.</param>
    /// <param name="groups">Independent documentation groups and their selected references.</param>
    /// <param name="cancellationToken">Cancellation for serialization and publication.</param>
    /// <returns>A task representing manifest publication.</returns>
    internal static async Task WriteAsync(string path, IReadOnlyList<AssemblyGroup> groups, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(groups);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await SerializeAsync(temporary, groups, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Reads exact restored groups, distinguishing an empty result from a legacy fetcher without a manifest.</summary>
    /// <param name="path">The manifest path for the current input, or null when declarations are absent.</param>
    /// <param name="cancellationToken">Cancellation for reading and validating groups.</param>
    /// <returns>The restored groups, or null when the fetcher did not publish a manifest.</returns>
    /// <exception cref="JsonException">The manifest format is unsupported.</exception>
    internal static async Task<AssemblyGroup[]?> ReadAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, _readOptions, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != FormatVersion)
            {
                throw new JsonException($"Unsupported restored assembly manifest '{path}'.");
            }

            var items = root.GetProperty(GroupsProperty);
            var groups = new AssemblyGroup[items.GetArrayLength()];
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                groups[index] = ReadGroup(item);
                index++;
            }

            return groups;
        }
    }

    /// <summary>Writes groups to an unpublished file.</summary>
    /// <param name="path">The temporary manifest path.</param>
    /// <param name="groups">Groups to serialize.</param>
    /// <param name="cancellationToken">Cancellation for serialization.</param>
    /// <returns>A task representing serialization.</returns>
    private static async Task SerializeAsync(string path, IReadOnlyList<AssemblyGroup> groups, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new Utf8JsonWriter(stream);
            await using (writer.ConfigureAwait(false))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", FormatVersion);
                writer.WriteStartArray(GroupsProperty);
                for (var i = 0; i < groups.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteGroup(writer, groups[i]);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes one documentation root's independent reference graph.</summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="group">Documentation group to serialize.</param>
    private static void WriteGroup(Utf8JsonWriter writer, AssemblyGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(group.FallbackIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(group.Tfm);
        writer.WriteStartObject();
        writer.WriteString("tfm", group.Tfm);
        writer.WriteBoolean("useOnlySuppliedReferences", group.UseOnlySuppliedReferences);
        WriteStrings(writer, "assemblies", group.AssemblyPaths, paths: true);
        WriteStrings(writer, "broadcastTfms", group.BroadcastTfms, paths: false);
        writer.WriteStartObject(ReferencesProperty);
        foreach (var reference in group.FallbackIndex)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.Key);
            ValidatePath(reference.Value);
            writer.WriteString(reference.Key, reference.Value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>Reads and validates one documentation group.</summary>
    /// <param name="item">The serialized group.</param>
    /// <returns>The exact restored group.</returns>
    /// <exception cref="JsonException">An assembly name has conflicting entries.</exception>
    private static AssemblyGroup ReadGroup(JsonElement item)
    {
        var tfm = ReadString(item.GetProperty("tfm"));
        var assemblies = ReadStrings(item.GetProperty("assemblies"), paths: true);
        var broadcast = ReadStrings(item.GetProperty("broadcastTfms"), paths: false);
        Dictionary<string, string> references = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (var reference in item.GetProperty(ReferencesProperty).EnumerateObject())
        {
            var path = ReadString(reference.Value);
            ValidatePath(path);
            if (string.IsNullOrWhiteSpace(reference.Name) || !references.TryAdd(reference.Name, path))
            {
                throw new JsonException($"Duplicate restored assembly reference '{reference.Name}'.");
            }
        }

        var suppliedOnly = !item.TryGetProperty("useOnlySuppliedReferences", out var policy) || policy.GetBoolean();
        return new(tfm, assemblies, references, broadcast) { UseOnlySuppliedReferences = suppliedOnly };
    }

    /// <summary>Writes a group's assembly paths or target frameworks.</summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="name">The array property.</param>
    /// <param name="values">Values to serialize.</param>
    /// <param name="paths">Whether values must identify existing absolute files.</param>
    private static void WriteStrings(Utf8JsonWriter writer, string name, string[] values, bool paths)
    {
        ArgumentNullException.ThrowIfNull(values);
        writer.WriteStartArray(name);
        for (var i = 0; i < values.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(values[i]);
            if (paths)
            {
                ValidatePath(values[i]);
            }

            writer.WriteStringValue(values[i]);
        }

        writer.WriteEndArray();
    }

    /// <summary>Reads a group's assembly paths or target frameworks.</summary>
    /// <param name="array">The serialized array.</param>
    /// <param name="paths">Whether values must identify existing absolute files.</param>
    /// <returns>The validated values.</returns>
    private static string[] ReadStrings(JsonElement array, bool paths)
    {
        var values = new string[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var value = ReadString(item);
            if (paths)
            {
                ValidatePath(value);
            }

            values[index] = value;
            index++;
        }

        return values;
    }

    /// <summary>Rejects empty names and paths in a restored group.</summary>
    /// <param name="value">The serialized value.</param>
    /// <returns>The nonempty string.</returns>
    /// <exception cref="JsonException">The value is null, empty, or whitespace.</exception>
    private static string ReadString(JsonElement value) => value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
        ? text
        : throw new JsonException("A restored assembly manifest contains an empty name or path.");

    /// <summary>Ensures a reference identifies an existing absolute assembly path.</summary>
    /// <param name="path">The selected assembly path.</param>
    /// <exception cref="InvalidDataException">The path is relative.</exception>
    /// <exception cref="FileNotFoundException">The selected assembly is absent.</exception>
    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException($"Restored assembly path '{path}' must be absolute.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Restored assembly '{path}' is missing. Restore the documentation graph again.", path);
        }
    }
}
