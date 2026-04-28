// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="PageWriter"/> -- the pooled, no-BOM UTF-8 page writer
/// that drains a <see cref="StringBuilder"/> directly into an unbuffered
/// <see cref="FileStream"/>. Covers the empty-builder, multi-chunk, parent
/// directory creation, async cancellation, and argument-guard branches of
/// both <see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> and
/// <see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/>.
/// </summary>
public class PageWriterTests
{
    /// <summary>Forces the StringBuilder to grow across multiple internal chunks.</summary>
    private const int MultiChunkAppendCount = 4096;

    /// <summary>The repeating payload appended to drive multi-chunk content.</summary>
    private const string MultiChunkSegment = "abcdefghijklmnopqrstuvwxyz0123456789-éñ漢";

    /// <summary><see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> writes an empty file when the builder is empty.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8EmptyBuilderWritesEmptyFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "empty.md");

        PageWriter.WriteUtf8(path, new StringBuilder());

        var bytes = await File.ReadAllBytesAsync(path);
        await Assert.That(bytes.Length).IsEqualTo(0);
    }

    /// <summary><see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> writes UTF-8 with no BOM and round-trips multi-chunk content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8MultiChunkRoundTripsAsUtf8WithoutBom()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "multi.md");
        var sb = BuildMultiChunkBuilder();
        var expected = sb.ToString();

        PageWriter.WriteUtf8(path, sb);

        var bytes = await File.ReadAllBytesAsync(path);
        await AssertNoBom(bytes);

        var roundTripped = await File.ReadAllTextAsync(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await Assert.That(roundTripped).IsEqualTo(expected);
    }

    /// <summary><see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> creates the destination directory when missing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8CreatesMissingParentDirectory()
    {
        using var temp = new TempDirectory();
        var nested = Path.Combine(temp.Path, "a", "b", "c");
        var path = Path.Combine(nested, "page.md");

        PageWriter.WriteUtf8(path, new StringBuilder("hi"));

        await Assert.That(Directory.Exists(nested)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("hi");
    }

    /// <summary><see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> tolerates a path with no directory component (root-relative file name).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8WithoutDirectoryComponentSucceeds()
    {
        using var temp = new TempDirectory();
        var fileName = "no-parent-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".md";
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(temp.Path);
        try
        {
            PageWriter.WriteUtf8(fileName, new StringBuilder("payload"));

            var fullPath = Path.Combine(temp.Path, fileName);
            await Assert.That(File.Exists(fullPath)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(fullPath)).IsEqualTo("payload");
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    /// <summary><see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> rejects null / whitespace paths and null builders.</summary>
    /// <param name="path">The candidate path under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task WriteUtf8RejectsInvalidPath(string? path)
    {
        await Assert.That(() => PageWriter.WriteUtf8(path!, new StringBuilder())).Throws<ArgumentException>();
    }

    /// <summary><see cref="PageWriter.WriteUtf8(string, StringBuilder)"/> rejects a null builder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8RejectsNullBuilder()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "x.md");
        await Assert.That(() => PageWriter.WriteUtf8(path, null!)).Throws<ArgumentNullException>();
    }

    /// <summary><see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/> writes an empty file when the builder is empty.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8AsyncEmptyBuilderWritesEmptyFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "empty-async.md");

        await PageWriter.WriteUtf8Async(path, new StringBuilder(), CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(path);
        await Assert.That(bytes.Length).IsEqualTo(0);
    }

    /// <summary><see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/> writes UTF-8 with no BOM and round-trips multi-chunk content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8AsyncMultiChunkRoundTripsAsUtf8WithoutBom()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "multi-async.md");
        var sb = BuildMultiChunkBuilder();
        var expected = sb.ToString();

        await PageWriter.WriteUtf8Async(path, sb, CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(path);
        await AssertNoBom(bytes);

        var roundTripped = await File.ReadAllTextAsync(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await Assert.That(roundTripped).IsEqualTo(expected);
    }

    /// <summary><see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/> creates the destination directory when missing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8AsyncCreatesMissingParentDirectory()
    {
        using var temp = new TempDirectory();
        var nested = Path.Combine(temp.Path, "x", "y", "z");
        var path = Path.Combine(nested, "page.md");

        await PageWriter.WriteUtf8Async(path, new StringBuilder("hi"), CancellationToken.None);

        await Assert.That(Directory.Exists(nested)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("hi");
    }

    /// <summary><see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/> honours a token cancelled before the call.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8AsyncCancelledBeforeWriteThrows()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "cancelled.md");
        var sb = BuildMultiChunkBuilder();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(() => PageWriter.WriteUtf8Async(path, sb, cts.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary><see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/> rejects null / whitespace paths.</summary>
    /// <param name="path">The candidate path under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task WriteUtf8AsyncRejectsInvalidPath(string? path)
    {
        await Assert.That(() => PageWriter.WriteUtf8Async(path!, new StringBuilder(), CancellationToken.None))
            .Throws<ArgumentException>();
    }

    /// <summary><see cref="PageWriter.WriteUtf8Async(string, StringBuilder, CancellationToken)"/> rejects a null builder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteUtf8AsyncRejectsNullBuilder()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "x.md");
        await Assert.That(() => PageWriter.WriteUtf8Async(path, null!, CancellationToken.None))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Builds a <see cref="StringBuilder"/> large enough to span more than one internal chunk.</summary>
    /// <returns>A populated <see cref="StringBuilder"/>.</returns>
    private static StringBuilder BuildMultiChunkBuilder()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < MultiChunkAppendCount; i++)
        {
            sb.Append(MultiChunkSegment);
        }

        // Sanity: the builder must actually carry more than one chunk so the
        // multi-chunk loop body is exercised.
        var chunkCount = 0;
        foreach (var chunk in sb.GetChunks())
        {
            _ = chunk;
            chunkCount++;
        }

        if (chunkCount < 2)
        {
            throw new InvalidOperationException("Test fixture failed to produce multi-chunk StringBuilder.");
        }

        return sb;
    }

    /// <summary>Asserts the bytes do not start with the UTF-8 byte-order mark.</summary>
    /// <param name="bytes">File bytes to inspect.</param>
    /// <returns>A task representing the assertion.</returns>
    private static async Task AssertNoBom(byte[] bytes)
    {
        if (bytes.Length < 3)
        {
            return;
        }

        var hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        await Assert.That(hasBom).IsFalse();
    }
}
