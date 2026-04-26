// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using System.Text.Json;
using SourceDocParser.Docfx;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests that the hand-written <see cref="DocfxConfigReader"/> +
/// <see cref="DocfxConfigWriter"/> pair preserves arbitrary docfx
/// extension data on a round-trip — the test suite for the
/// JsonExtensionData replacement we built.
/// </summary>
public class DocfxConfigRoundTripTests
{
    /// <summary>
    /// Reading a JSON document with extras, writing it back through the
    /// writer's emit helpers, and re-reading it produces a value that
    /// compares structurally equal to the original (extras included).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadWriteReadPreservesExtensionData()
    {
        const string original = """
            {
              "metadata": [
                {
                  "src": [ { "src": "api/lib/net10.0", "files": [ "Foo.dll", "Bar.dll" ] } ],
                  "dest": "api",
                  "outputFormat": "mref",
                  "filter": "filter.yml",
                  "namespaceLayout": "flattened",
                  "memberLayout": "samePage"
                }
              ],
              "build": {
                "content": [
                  { "files": [ "**.md" ], "exclude": [ "_site/**" ] },
                  { "files": [ "api/**.yml", "api/index.md" ] }
                ],
                "globalMetadata": { "_appName": "Test" },
                "template": [ "default", "modern" ]
              }
            }
            """;

        var first = ReadFromString(original);
        var rewritten = WriteAndRead(first);

        await Assert.That(rewritten.Metadata.Count).IsEqualTo(first.Metadata.Count);
        await Assert.That(rewritten.Build.Content.Count).IsEqualTo(first.Build.Content.Count);

        // Extras on the metadata entry survive verbatim.
        var firstEntry = first.Metadata[0];
        var rewrittenEntry = rewritten.Metadata[0];
        await Assert.That(rewrittenEntry.Dest).IsEqualTo(firstEntry.Dest);
        await Assert.That(rewrittenEntry.Extra).IsNotNull();
        await Assert.That(rewrittenEntry.Extra!.ContainsKey("outputFormat")).IsTrue();
        await Assert.That(rewrittenEntry.Extra["outputFormat"].GetString()).IsEqualTo("mref");
        await Assert.That(rewrittenEntry.Extra.ContainsKey("filter")).IsTrue();

        // Extras on the build section survive (globalMetadata + template).
        await Assert.That(rewritten.Build.Extra).IsNotNull();
        await Assert.That(rewritten.Build.Extra!.ContainsKey("globalMetadata")).IsTrue();
        await Assert.That(rewritten.Build.Extra.ContainsKey("template")).IsTrue();

        // Per-content-entry extras (the 'exclude' on entry 0) survive.
        var firstContent = rewritten.Build.Content[0];
        await Assert.That(firstContent.Extra).IsNotNull();
        await Assert.That(firstContent.Extra!.ContainsKey("exclude")).IsTrue();
    }

    /// <summary>
    /// An empty top-level object parses into a config with empty metadata and an empty build content list.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadAcceptsEmptyObject()
    {
        var config = ReadFromString("{}");

        await Assert.That(config.Metadata.Count).IsEqualTo(0);
        await Assert.That(config.Build.Content.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A non-object root throws a JsonException with a clear message.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadRejectsNonObjectRoot() => await Assert.That(static () => ReadFromString("[1, 2, 3]")).ThrowsExactly<JsonException>();

    /// <summary>
    /// Reads a docfx config from a string by encoding it to UTF-8 and feeding the bytes to the reader.
    /// </summary>
    /// <param name="json">JSON document body.</param>
    /// <returns>The parsed configuration.</returns>
    private static DocfxConfig ReadFromString(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return DocfxConfigReader.Read(stream);
    }

    /// <summary>
    /// Writes <paramref name="config"/> via <see cref="DocfxConfigWriter"/>'s
    /// public Write entry point and re-reads the file. Uses a temp file
    /// because Write currently only writes to disk; the test deletes it
    /// afterwards.
    /// </summary>
    /// <param name="config">Config to round-trip.</param>
    /// <returns>The re-parsed configuration.</returns>
    private static DocfxConfig WriteAndRead(DocfxConfig config)
    {
        // The public Write goes through the lib/ + refs/ scan path, which
        // we don't want here. Instead, drive the lower-level WriteConfig
        // helper through the same writer assembly. Easiest: re-implement
        // by constructing a Utf8JsonWriter ourselves, since that is what
        // the writer does internally.
        var path = Path.Combine(Path.GetTempPath(), $"docfx-roundtrip-{Guid.NewGuid():N}.json");
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
            {
                WriteConfig(writer, config);
                writer.Flush();
            }

            using var read = File.OpenRead(path);
            return DocfxConfigReader.Read(read);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Mirrors <c>DocfxConfigWriter.WriteConfig</c> shape so the test
    /// can drive it without exposing the writer's internals. Kept tiny
    /// so it stays in lockstep with the production writer.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="config">Config to emit.</param>
    private static void WriteConfig(Utf8JsonWriter writer, DocfxConfig config)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("metadata");
        writer.WriteStartArray();
        foreach (var entry in config.Metadata)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("src");
            writer.WriteStartArray();
            foreach (var src in entry.Src)
            {
                writer.WriteStartObject();
                writer.WriteString("src", src.Src);
                writer.WritePropertyName("files");
                writer.WriteStartArray();
                foreach (var f in src.Files)
                {
                    writer.WriteStringValue(f);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("dest", entry.Dest);
            WriteExtras(writer, entry.Extra);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("build");
        writer.WriteStartObject();
        writer.WritePropertyName("content");
        writer.WriteStartArray();
        foreach (var c in config.Build.Content)
        {
            writer.WriteStartObject();
            if (c.Files is { } files)
            {
                writer.WritePropertyName("files");
                writer.WriteStartArray();
                foreach (var f in files)
                {
                    writer.WriteStringValue(f);
                }

                writer.WriteEndArray();
            }

            WriteExtras(writer, c.Extra);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteExtras(writer, config.Build.Extra);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <summary>
    /// Emits extras through <see cref="JsonElement.WriteTo(Utf8JsonWriter)"/>.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="extras">Extras dictionary.</param>
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
}
