// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Config;

/// <summary>
/// End-to-end pin on <see cref="DocfxConfigWriter.Write"/>: builds a
/// fixture <c>lib/</c> + <c>refs/</c> directory tree under a scratch
/// folder, runs the writer, and asserts the generated docfx.json
/// surfaces the right metadata entries (package DLLs only, ref DLLs
/// excluded), the right destination, and a parseable build section.
/// Also covers the documented failure modes — missing <c>lib/</c>,
/// empty <c>lib/</c>, and missing arguments — so a regression in the
/// argument-validation contract surfaces on its own line.
/// </summary>
public class DocfxConfigWriterTests
{
    /// <summary>The writer projects each lib TFM with a co-located refs match into one metadata entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WritesMetadataEntryPerLibTfmWithMatchingRefs()
    {
        using var scratch = new ScratchDirectory("docfxcw");
        WriteEmptyDll(scratch.Path, "lib/net8.0/MyPkg.dll");
        WriteEmptyDll(scratch.Path, "refs/net8.0/System.Runtime.dll");
        var output = Path.Combine(scratch.Path, "docfx.json");

        var written = DocfxConfigWriter.Write(scratch.Path, output);

        await Assert.That(written).IsEqualTo(output);
        await Assert.That(File.Exists(output)).IsTrue();

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(output));
        var metadata = doc.RootElement.GetProperty("metadata");
        await Assert.That(metadata.GetArrayLength()).IsEqualTo(1);
        var entry = metadata[0];
        await Assert.That(entry.GetProperty("dest").GetString()).IsEqualTo("api");
        var src = entry.GetProperty("src")[0];
        await Assert.That(src.GetProperty("src").GetString()).IsEqualTo("api/lib/net8.0");

        // The package DLL is kept; the System.Runtime ref DLL must be filtered out.
        var files = src.GetProperty("files");
        var fileNames = new List<string>();
        for (var i = 0; i < files.GetArrayLength(); i++)
        {
            fileNames.Add(files[i].GetString()!);
        }

        await Assert.That(fileNames).Contains("MyPkg.dll");
        await Assert.That(fileNames).DoesNotContain("System.Runtime.dll");
    }

    /// <summary>Lib TFMs with a platform suffix (e.g. <c>net8.0-windows</c>) project into a per-platform <c>api-{label}</c> destination.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PlatformLabelDrivesDestDirectory()
    {
        using var scratch = new ScratchDirectory("docfxcw");
        WriteEmptyDll(scratch.Path, "lib/net8.0-windows/MyPkg.dll");
        WriteEmptyDll(scratch.Path, "refs/net8.0-windows/System.Runtime.dll");
        var output = Path.Combine(scratch.Path, "docfx.json");

        DocfxConfigWriter.Write(scratch.Path, output);

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(output));
        var dest = doc.RootElement.GetProperty("metadata")[0].GetProperty("dest").GetString();
        await Assert.That(dest).IsEqualTo("api-windows");
    }

    /// <summary>Lib TFMs without a matching refs/ TFM are skipped (no metadata entry emitted).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LibTfmWithoutMatchingRefsIsSkipped()
    {
        using var scratch = new ScratchDirectory("docfxcw");

        // monoandroid is a legacy TFM that TfmResolver explicitly returns
        // null for — guarantees no refs match regardless of what we put
        // under refs/, exercising the "no matching refs" skip branch.
        WriteEmptyDll(scratch.Path, "lib/monoandroid10.0/MyPkg.dll");
        WriteEmptyDll(scratch.Path, "refs/net8.0/System.Runtime.dll");
        var output = Path.Combine(scratch.Path, "docfx.json");

        DocfxConfigWriter.Write(scratch.Path, output);

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(output));
        await Assert.That(doc.RootElement.GetProperty("metadata").GetArrayLength()).IsEqualTo(0);
    }

    /// <summary>A lib TFM containing only ref DLLs (everything filtered out) is skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LibTfmWithOnlyRefDllsIsSkipped()
    {
        using var scratch = new ScratchDirectory("docfxcw");
        WriteEmptyDll(scratch.Path, "lib/net8.0/System.Runtime.dll");
        WriteEmptyDll(scratch.Path, "refs/net8.0/System.Runtime.dll");
        var output = Path.Combine(scratch.Path, "docfx.json");

        DocfxConfigWriter.Write(scratch.Path, output);

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(output));
        await Assert.That(doc.RootElement.GetProperty("metadata").GetArrayLength()).IsEqualTo(0);
    }

    /// <summary>A missing <c>lib/</c> directory throws <see cref="DirectoryNotFoundException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MissingLibDirectoryThrows()
    {
        using var scratch = new ScratchDirectory("docfxcw");

        await Assert.That(() => DocfxConfigWriter.Write(scratch.Path, Path.Combine(scratch.Path, "docfx.json")))
            .Throws<DirectoryNotFoundException>();
    }

    /// <summary>A <c>lib/</c> directory with no TFM sub-directories throws <see cref="InvalidOperationException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyLibDirectoryThrows()
    {
        using var scratch = new ScratchDirectory("docfxcw");
        Directory.CreateDirectory(Path.Combine(scratch.Path, "lib"));

        await Assert.That(() => DocfxConfigWriter.Write(scratch.Path, Path.Combine(scratch.Path, "docfx.json")))
            .Throws<InvalidOperationException>();
    }

    /// <summary>Null/empty/whitespace argument paths throw <see cref="ArgumentException"/> before any I/O.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsBlankArguments()
    {
        await Assert.That(() => DocfxConfigWriter.Write(string.Empty, "out.json")).Throws<ArgumentException>();
        await Assert.That(() => DocfxConfigWriter.Write("api", string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => DocfxConfigWriter.Write("   ", "out.json")).Throws<ArgumentException>();
    }

    /// <summary>Creates an empty file at <paramref name="relative"/> under <paramref name="root"/>, creating parent directories as needed.</summary>
    /// <param name="root">Root directory.</param>
    /// <param name="relative">Forward-slash relative path; converted to platform-native separators.</param>
    private static void WriteEmptyDll(string root, string relative)
    {
        var native = relative.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(root, native);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, []);
    }
}
