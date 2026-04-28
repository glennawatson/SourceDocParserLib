// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Reflection.PortableExecutable;
using SamplePdb;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests.SourceLink;

/// <summary>
/// Pins the missing-file and corrupt-file branches of
/// <see cref="StandalonePdbOpener"/>, neither of which is reachable
/// via SourceLinkReader's embedded-PDB happy path.
/// </summary>
public class StandalonePdbOpenerTests
{
    /// <summary>A path that doesn't exist returns false with null outs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryOpenReturnsFalseForMissingFile()
    {
        var ok = StandalonePdbOpener.TryOpen("/no/such/path/missing.pdb", out var provider, out var reader);

        await Assert.That(ok).IsFalse();
        await Assert.That(provider).IsNull();
        await Assert.That(reader).IsNull();
    }

    /// <summary>A file that exists but isn't a portable PDB returns false with null outs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryOpenReturnsFalseForCorruptFile()
    {
        var tempPdb = Path.Combine(Path.GetTempPath(), $"sdp-bad-{Guid.NewGuid():N}.pdb");
        try
        {
            await File.WriteAllBytesAsync(tempPdb, [0x00, 0x01, 0x02, 0x03]);

            var ok = StandalonePdbOpener.TryOpen(tempPdb, out var provider, out var reader);

            await Assert.That(ok).IsFalse();
            await Assert.That(provider).IsNull();
            await Assert.That(reader).IsNull();
        }
        finally
        {
            if (File.Exists(tempPdb))
            {
                File.Delete(tempPdb);
            }
        }
    }

    /// <summary>
    /// Extracts the SamplePdb fixture's embedded PDB to a temp file,
    /// then opens it standalone -- pins the success path (read-stream,
    /// reader, ownership transfer to outs) that the corrupt / missing
    /// tests don't exercise.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryOpenLoadsValidPortablePdbFromDisk()
    {
        var assemblyPath = typeof(SamplePdbAnchor).Assembly.Location;
        var tempPdb = Path.Combine(Path.GetTempPath(), $"sdp-good-{Guid.NewGuid():N}.pdb");
        try
        {
            // Materialise the assembly's embedded PDB blob to disk so
            // the standalone opener has a real file to consume.
            byte[] pdbBytes = ExtractEmbeddedPdbBytes(assemblyPath);
            await File.WriteAllBytesAsync(tempPdb, pdbBytes);

            var ok = StandalonePdbOpener.TryOpen(tempPdb, out var providerOut, out var readerOut);

            await Assert.That(ok).IsTrue();
            await Assert.That(providerOut).IsNotNull();
            await Assert.That(readerOut).IsNotNull();
            providerOut?.Dispose();
        }
        finally
        {
            if (File.Exists(tempPdb))
            {
                File.Delete(tempPdb);
            }
        }
    }

    /// <summary>
    /// Returns the raw bytes of an assembly's embedded portable PDB.
    /// The embedded debug directory entry stores the PDB as the
    /// 4-byte <c>MPDB</c> signature, the 4-byte uncompressed length
    /// in little-endian, then a DEFLATE payload. We unpack it here
    /// rather than via <c>ReadEmbeddedPortablePdbDebugDirectoryData</c>
    /// to avoid touching the unmanaged metadata pointer.
    /// </summary>
    /// <param name="assemblyPath">Path to the .dll.</param>
    /// <returns>The PDB stream bytes, ready to write to disk as a standalone .pdb.</returns>
    private static byte[] ExtractEmbeddedPdbBytes(string assemblyPath)
    {
        using var peStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(peStream);
        var entry = peReader.ReadDebugDirectory()
            .First(static e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        var rawSpan = peReader.GetSectionData(entry.DataRelativeVirtualAddress).GetContent(0, entry.DataSize).AsSpan();

        // Strip the 4-byte "MPDB" signature + 4-byte uncompressed length, decompress the rest.
        var uncompressedLength = BitConverter.ToInt32(rawSpan.Slice(4, 4));
        var compressed = rawSpan[8..];
        var output = new byte[uncompressedLength];
        using var ms = new MemoryStream(compressed.ToArray());
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        var read = 0;
        while (read < uncompressedLength)
        {
            var n = deflate.Read(output, read, uncompressedLength - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return output;
    }
}
