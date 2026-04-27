// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="ManagedAssemblyExtractor"/>: the entry-selection
/// predicate (path prefix + .dll extension + non-empty filename) and
/// the lazy enumerate helper. The PE managed-assembly check is
/// covered by the existing extraction integration test in NuGetFetcher.
/// </summary>
public class ManagedAssemblyExtractorTests
{
    /// <summary>Entries under the requested prefix with a .dll extension are kept.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectsRefDllsUnderPrefix()
    {
        await using var memStream = BuildArchive(("ref/net8.0/Foo.dll", []), ("ref/net8.0/Foo.xml", []));
        await using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);

        var entries = ManagedAssemblyExtractor.SelectAssemblyEntries(archive, "ref/net8.0").ToList();

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Name).IsEqualTo("Foo.dll");
    }

    /// <summary>Entries outside the requested prefix are skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipsEntriesOutsidePrefix()
    {
        await using var memStream = BuildArchive(("lib/net8.0/Bar.dll", []));
        await using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);

        var entries = ManagedAssemblyExtractor.SelectAssemblyEntries(archive, "ref/").ToList();

        await Assert.That(entries.Count).IsEqualTo(0);
    }

    /// <summary>Path prefix without a trailing slash still filters correctly.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PrefixWithoutTrailingSlashStillMatches()
    {
        await using var memStream = BuildArchive(("ref/net8.0/Foo.dll", []));
        await using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);

        var entries = ManagedAssemblyExtractor.SelectAssemblyEntries(archive, "ref/net8.0").ToList();

        await Assert.That(entries.Count).IsEqualTo(1);
    }

    /// <summary>Directory entries (<c>Name</c> is empty) are skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipsDirectoryEntries()
    {
        await using var memStream = BuildArchive(("ref/net8.0/", []), ("ref/net8.0/Foo.dll", []));
        await using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);

        var entries = ManagedAssemblyExtractor.SelectAssemblyEntries(archive, "ref/net8.0/").ToList();

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Name).IsEqualTo("Foo.dll");
    }

    /// <summary>Non-stream PE candidate that's empty bytes is rejected as not-managed (PEReader throws and returns false).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsManagedAssemblyReturnsFalseForEmptyStream()
    {
        await using var stream = new MemoryStream([]);

        await Assert.That(ManagedAssemblyExtractor.IsManagedAssembly(stream)).IsFalse();
    }

    /// <summary>SelectAssemblyEntries throws on null archive and blank prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAssemblyEntriesValidatesArguments()
    {
        await Assert.That(() => ManagedAssemblyExtractor.SelectAssemblyEntries(null!, "ref/").ToList())
            .Throws<ArgumentNullException>();

        await using var memStream = BuildArchive();
        await using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);
        await Assert.That(() => ManagedAssemblyExtractor.SelectAssemblyEntries(archive, string.Empty).ToList())
            .Throws<ArgumentException>();
    }

    /// <summary>Builds an in-memory ZIP with the supplied entries (each entry written with <see cref="ZipArchiveMode.Create"/>).</summary>
    /// <param name="entries">Entries to write — full path + raw bytes.</param>
    /// <returns>Stream positioned at 0 ready to be read.</returns>
    private static MemoryStream BuildArchive(params (string Path, byte[] Bytes)[] entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = entry.Open();
                writer.Write(bytes);
            }
        }

        ms.Position = 0;
        return ms;
    }
}
