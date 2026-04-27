// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;
using System.Text;
using SamplePdb;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests.SourceLink;

/// <summary>
/// Pins the malformed-blob and empty-map branches of
/// <see cref="SourceLinkBlobParser"/> that the integration test
/// against the SamplePdb fixture cannot reach.
/// </summary>
public class SourceLinkBlobParserTests
{
    /// <summary>A valid SourceLink JSON with one mapping returns a populated map.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseReturnsMapForValidJson()
    {
        const string json = """{"documents":{"C:\\src\\*":"https://example/raw/*"}}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var map = SourceLinkBlobParser.TryParse(bytes);

        await Assert.That(map).IsNotNull();
        await Assert.That(map!.TryResolve(@"C:\src\foo.cs")).IsEqualTo("https://example/raw/foo.cs");
    }

    /// <summary>A SourceLink JSON with no document entries returns null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseReturnsNullForEmptyMap()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"documents":{}}""");

        var map = SourceLinkBlobParser.TryParse(bytes);

        await Assert.That(map).IsNull();
    }

    /// <summary>Malformed JSON returns null instead of throwing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseReturnsNullForMalformedJson()
    {
        var bytes = Encoding.UTF8.GetBytes("{not json");

        var map = SourceLinkBlobParser.TryParse(bytes);

        await Assert.That(map).IsNull();
    }

    /// <summary>A completely empty buffer returns null without throwing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseReturnsNullForEmptyBuffer()
    {
        var map = SourceLinkBlobParser.TryParse(ReadOnlyMemory<byte>.Empty);

        await Assert.That(map).IsNull();
    }

    /// <summary>FindAndParse rejects null reader.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindAndParseRejectsNullReader() =>
        await Assert.That(() => SourceLinkBlobParser.FindAndParse(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// FindAndParse against the SamplePdb fixture's real embedded PDB
    /// finds the SourceLink record and parses it into a usable map —
    /// pins the success path (foreach-match-then-decode) of the
    /// helper that the synthetic-bytes tests can't reach.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindAndParseLocatesEmbeddedSourceLinkInSamplePdb()
    {
        var assemblyPath = typeof(SamplePdbAnchor).Assembly.Location;
        await using var peStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(peStream);
        var debugEntry = peReader.ReadDebugDirectory()
            .First(static e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        using var pdbProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(debugEntry);
        var pdbReader = pdbProvider.GetMetadataReader();

        var map = SourceLinkBlobParser.FindAndParse(pdbReader);

        await Assert.That(map).IsNotNull();
    }

    /// <summary>
    /// Calling <c>FindAndParse</c> with a non-matching GUID walks
    /// every custom-debug record, hits <c>continue</c> on each, and
    /// falls through to the closing <c>return null</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindAndParseReturnsNullWhenGuidNeverMatches()
    {
        var assemblyPath = typeof(SamplePdbAnchor).Assembly.Location;
        await using var peStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(peStream);
        var debugEntry = peReader.ReadDebugDirectory()
            .First(static e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        using var pdbProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(debugEntry);
        var pdbReader = pdbProvider.GetMetadataReader();

        var map = SourceLinkBlobParser.FindAndParse(pdbReader, recordGuid: Guid.NewGuid());

        await Assert.That(map).IsNull();
    }
}
