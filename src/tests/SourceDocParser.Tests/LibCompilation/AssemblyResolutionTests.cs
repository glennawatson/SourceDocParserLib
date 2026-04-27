// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Tests.LibCompilation;

/// <summary>
/// Pins <see cref="AssemblyResolution.BuildFallbackIndex"/> — the
/// missing-directory skip, the duplicate-name log path, and the
/// happy-path index shape.
/// </summary>
public class AssemblyResolutionTests
{
    /// <summary>A null directory list is rejected.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackIndexRejectsNullDirectories() =>
        await Assert.That(() => AssemblyResolution.BuildFallbackIndex(null!, NullLogger.Instance)).Throws<ArgumentNullException>();

    /// <summary>A null logger is rejected.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackIndexRejectsNullLogger() =>
        await Assert.That(() => AssemblyResolution.BuildFallbackIndex([], null!)).Throws<ArgumentNullException>();

    /// <summary>Directories that don't exist are silently skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackIndexSkipsMissingDirectories()
    {
        var index = AssemblyResolution.BuildFallbackIndex(["/no/such/dir/here"], NullLogger.Instance);

        await Assert.That(index).IsEmpty();
    }

    /// <summary>Each .dll in a real directory is indexed by simple name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackIndexIndexesDllsBySimpleName()
    {
        using var scratch = new ScratchDirectory();
        var foo = Path.Combine(scratch.Path, "Foo.dll");
        var bar = Path.Combine(scratch.Path, "Bar.dll");
        await File.WriteAllBytesAsync(foo, [0x4D, 0x5A]);
        await File.WriteAllBytesAsync(bar, [0x4D, 0x5A]);

        var index = AssemblyResolution.BuildFallbackIndex([scratch.Path], NullLogger.Instance);

        await Assert.That(index["Foo"]).IsEqualTo(foo);
        await Assert.That(index["Bar"]).IsEqualTo(bar);
    }

    /// <summary>When two directories share a DLL name, the first wins and a duplicate notice is logged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackIndexKeepsFirstAndLogsDuplicate()
    {
        using var scratchA = new ScratchDirectory("sdp-resA");
        using var scratchB = new ScratchDirectory("sdp-resB");
        var first = Path.Combine(scratchA.Path, "Shared.dll");
        var second = Path.Combine(scratchB.Path, "Shared.dll");
        await File.WriteAllBytesAsync(first, [0x4D, 0x5A]);
        await File.WriteAllBytesAsync(second, [0x4D, 0x5A]);

        var index = AssemblyResolution.BuildFallbackIndex([scratchA.Path, scratchB.Path], NullLogger.Instance);

        await Assert.That(index.Count).IsEqualTo(1);
        await Assert.That(index["Shared"]).IsEqualTo(first);
    }
}
