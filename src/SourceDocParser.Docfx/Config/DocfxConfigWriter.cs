// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.Docfx.Common;
using SourceDocParser.Tfm;

namespace SourceDocParser.Docfx.Config;

/// <summary>
/// Generates a docfx configuration file by patching an embedded
/// template's <c>metadata</c> and <c>build.content</c> sections to
/// reflect the lib/ and refs/ TFM directories present under the
/// supplied API root.
/// </summary>
public static partial class DocfxConfigWriter
{
    /// <summary>Logical name of the embedded docfx template resource.</summary>
    private const string TemplateResourceName = "SourceDocParser.Docfx.docfx.template.json";

    /// <summary>JSON output options matching docfx's expected camelCase + indented form.</summary>
    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
    };

    /// <summary>
    /// Reads the embedded template, patches metadata/build sections to
    /// reflect the discovered TFMs, and writes the result.
    /// </summary>
    /// <param name="apiPath">API root containing <c>lib/</c> and (optionally) <c>refs/</c> sub-directories.</param>
    /// <param name="outputPath">File to write the generated configuration to.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <returns>The same <paramref name="outputPath"/>, for fluent use by the caller.</returns>
    /// <exception cref="ArgumentException">When <paramref name="apiPath"/> or <paramref name="outputPath"/> is null, empty, or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">When <c>lib/</c> does not exist under <paramref name="apiPath"/>.</exception>
    /// <exception cref="InvalidOperationException">When no TFM directories with DLLs are found.</exception>
    public static string Write(string apiPath, string outputPath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        logger ??= NullLogger.Instance;
        var libDir = Path.Combine(apiPath, "lib");
        var refsDir = Path.Combine(apiPath, "refs");

        if (!Directory.Exists(libDir))
        {
            throw new DirectoryNotFoundException($"No lib/ directory found at {libDir}");
        }

        var libTfms = DocfxInternalHelpers.DiscoverTfms(libDir);
        if (libTfms.Count is 0)
        {
            throw new InvalidOperationException("No TFM directories with DLLs found in lib/");
        }

        var refsTfms = Directory.Exists(refsDir) ? DocfxInternalHelpers.DiscoverTfms(refsDir) : [];
        var refDllNameCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        LogDiscoveredLibTfms(logger, libTfms);
        LogDiscoveredRefsTfms(logger, refsTfms);

        var template = ReadTemplate();
        var sharedExtra = ExtractSharedMetadataExtras(template.Metadata is [var firstMetadata, ..] ? firstMetadata : null);

        var metadataEntries = new List<DocfxMetadataEntry>(libTfms.Count);
        var platformLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < libTfms.Count; i++)
        {
            var tfm = libTfms[i];
            var bestRef = TfmResolver.FindBestRefsTfm(tfm, refsTfms);
            if (bestRef is null)
            {
                LogNoMatchingRefs(logger, tfm);
                continue;
            }

            var platformLabel = TfmResolver.GetPlatformLabel(tfm);
            var dest = platformLabel is not null ? $"api-{platformLabel}" : "api";
            if (platformLabel is not null)
            {
                platformLabels.Add(platformLabel);
            }

            var refDir = Path.Combine(refsDir, bestRef);
            var refDllNames = DocfxInternalHelpers.GetOrAddDllNames(refDllNameCache, bestRef, refDir);
            var packageDlls = DocfxInternalHelpers.CollectPackageDllNames(Path.Combine(libDir, tfm), refDllNames);

            if (packageDlls.Count is 0)
            {
                LogNoPackageDlls(logger, tfm);
                continue;
            }

            metadataEntries.Add(new(
                Src: [new($"api/lib/{tfm}", [.. packageDlls])],
                Dest: dest)
            {
                Extra = sharedExtra,
            });

            LogMetadataEntry(logger, tfm, packageDlls.Count, bestRef, dest);
        }

        var orderedPlatforms = new List<string>(platformLabels);
        orderedPlatforms.Sort(StringComparer.Ordinal);

        var patchedBuild = DocfxInternalHelpers.PatchBuildSection(template.Build, [.. orderedPlatforms]);
        var generated = new DocfxConfig([.. metadataEntries], patchedBuild);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        WriteConfig(generated, outputPath);

        LogWroteConfig(logger, metadataEntries.Count, outputPath);
        return outputPath;
    }

    /// <summary>
    /// Reads and parses the docfx template embedded in this assembly via <see cref="DocfxConfigReader"/>.
    /// </summary>
    /// <returns>The parsed template configuration.</returns>
    /// <exception cref="InvalidOperationException">When the resource is missing.</exception>
    private static DocfxConfig ReadTemplate()
    {
        var assembly = typeof(DocfxConfigWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded docfx template '{TemplateResourceName}' not found in {assembly.FullName}");

        return DocfxConfigReader.Read(stream);
    }

    /// <summary>
    /// Returns the extension data of the template's first metadata entry,
    /// minus the <c>references</c> key (each generated entry supplies its
    /// own resolution via co-located ref DLLs).
    /// </summary>
    /// <param name="templateEntry">First metadata entry from the template, if any.</param>
    /// <returns>A copy of the extension data, or <see langword="null"/> when nothing remains to preserve.</returns>
    private static Dictionary<string, JsonElement>? ExtractSharedMetadataExtras(DocfxMetadataEntry? templateEntry)
    {
        if (templateEntry?.Extra is not { Count: > 0 } extra)
        {
            return null;
        }

        var copy = new Dictionary<string, JsonElement>(extra);
        copy.Remove("references");
        return copy.Count is 0 ? null : copy;
    }

    /// <summary>
    /// Serialises <paramref name="config"/> to <paramref name="outputPath"/>
    /// via a hand-driven <see cref="Utf8JsonWriter"/>. JsonElement extras
    /// flow through verbatim using <see cref="JsonElement.WriteTo(Utf8JsonWriter)"/>.
    /// </summary>
    /// <param name="config">Patched configuration to serialise.</param>
    /// <param name="outputPath">Destination file path.</param>
    private static void WriteConfig(DocfxConfig config, string outputPath)
    {
        using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var writer = new Utf8JsonWriter(stream, _writerOptions);

        writer.WriteStartObject();

        writer.WritePropertyName("metadata"u8);
        writer.WriteStartArray();
        for (var i = 0; i < config.Metadata.Length; i++)
        {
            WriteMetadataEntry(writer, config.Metadata[i]);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("build"u8);
        WriteBuildSection(writer, config.Build);

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>
    /// Writes one metadata entry: the typed src+dest properties, then any
    /// round-tripped extras.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="entry">Metadata entry to write.</param>
    private static void WriteMetadataEntry(Utf8JsonWriter writer, DocfxMetadataEntry entry)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("src"u8);
        writer.WriteStartArray();
        for (var i = 0; i < entry.Src.Length; i++)
        {
            WriteMetadataSource(writer, entry.Src[i]);
        }

        writer.WriteEndArray();

        writer.WriteString("dest"u8, entry.Dest);

        WriteExtras(writer, entry.Extra);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes one metadata src record (src directory + file list).
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="source">Source record to write.</param>
    private static void WriteMetadataSource(Utf8JsonWriter writer, DocfxMetadataSource source)
    {
        writer.WriteStartObject();
        writer.WriteString("src"u8, source.Src);
        writer.WritePropertyName("files"u8);
        WriteStringArray(writer, source.Files);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the build section: typed content array, then any round-tripped extras.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="build">Build section to write.</param>
    private static void WriteBuildSection(Utf8JsonWriter writer, DocfxBuildSection build)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("content"u8);
        writer.WriteStartArray();
        for (var i = 0; i < build.Content.Length; i++)
        {
            WriteBuildContent(writer, build.Content[i]);
        }

        writer.WriteEndArray();

        WriteExtras(writer, build.Extra);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes one build content entry: the optional files array, then any round-tripped extras.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="entry">Build content entry.</param>
    private static void WriteBuildContent(Utf8JsonWriter writer, DocfxBuildContent entry)
    {
        writer.WriteStartObject();

        if (entry.Files is { } files)
        {
            writer.WritePropertyName("files"u8);
            WriteStringArray(writer, files);
        }

        WriteExtras(writer, entry.Extra);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes <paramref name="values"/> as a JSON string array.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="values">Strings to emit.</param>
    private static void WriteStringArray(Utf8JsonWriter writer, string[] values)
    {
        writer.WriteStartArray();
        for (var i = 0; i < values.Length; i++)
        {
            writer.WriteStringValue(values[i]);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Emits each entry in <paramref name="extras"/> via
    /// <see cref="JsonElement.WriteTo(Utf8JsonWriter)"/> so the original
    /// JSON value (object, array, primitive) round-trips byte-for-byte.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="extras">Round-trip extension data, or null.</param>
    private static void WriteExtras(Utf8JsonWriter writer, Dictionary<string, JsonElement>? extras)
    {
        if (extras is null)
        {
            return;
        }

        foreach (var entry in extras)
        {
            writer.WritePropertyName(entry.Key);
            entry.Value.WriteTo(writer);
        }
    }

    /// <summary>Logs the discovered lib/ TFMs.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfms">Discovered lib/ TFM directory names.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered lib/ TFMs: {Tfms}")]
    private static partial void LogDiscoveredLibTfms(ILogger logger, List<string> tfms);

    /// <summary>Logs the discovered refs/ TFMs.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfms">Discovered refs/ TFM directory names.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered refs/ TFMs: {Tfms}")]
    private static partial void LogDiscoveredRefsTfms(ILogger logger, List<string> tfms);

    /// <summary>Logs that no refs/ TFM matched a lib/ TFM (entry skipped).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfm">The lib/ TFM with no refs/ match.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "No matching refs for lib/{Tfm}, skipping metadata entry")]
    private static partial void LogNoMatchingRefs(ILogger logger, string tfm);

    /// <summary>Logs that a lib/ TFM directory contained no package DLLs (entry skipped).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfm">The lib/ TFM that yielded no package DLLs.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "No package DLLs in lib/{Tfm} (only refs), skipping")]
    private static partial void LogNoPackageDlls(ILogger logger, string tfm);

    /// <summary>Logs a generated metadata entry with its DLL count and refs source.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfm">lib/ TFM the entry was generated for.</param>
    /// <param name="dllCount">Number of package DLLs in the entry.</param>
    /// <param name="refsTfm">refs/ TFM supplying reference assemblies.</param>
    /// <param name="dest">Destination directory for the generated YAML.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Metadata entry: lib/{Tfm} ({DllCount} DLLs, refs from {RefsTfm}) -> {Dest}")]
    private static partial void LogMetadataEntry(ILogger logger, string tfm, int dllCount, string refsTfm, string dest);

    /// <summary>Logs the final write of the generated docfx config.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="metadataEntryCount">Number of metadata entries written.</param>
    /// <param name="outputPath">Path the configuration was written to.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote generated docfx config with {MetadataEntryCount} metadata entries to {OutputPath}")]
    private static partial void LogWroteConfig(ILogger logger, int metadataEntryCount, string outputPath);
}
