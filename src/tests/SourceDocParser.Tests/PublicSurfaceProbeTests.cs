// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SamplePdb;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="PublicSurfaceProbe"/> against the real SamplePdb
/// fixture assembly so the metadata-token walk, the nested-public
/// chaining, and the compiler-generated filter all exercise their
/// production paths. Silent-skip cases (non-managed, missing files)
/// are covered with synthetic on-disk fixtures via <see cref="TempDirectory"/>.
/// </summary>
public class PublicSurfaceProbeTests
{
    /// <summary>The probe surfaces every top-level public type declared in SamplePdb.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeSurfacesTopLevelPublicTypes()
    {
        var uids = PublicSurfaceProbe.ProbePublicTypeUids([GetSamplePdbPath()]);

        await Assert.That(uids).Contains("T:SamplePdb.SamplePdbAnchor");
        await Assert.That(uids).Contains("T:SamplePdb.Outer");
        await Assert.That(uids).Contains("T:SamplePdb.Player");
    }

    /// <summary>Nested public types are walked through their declaring chain into a dotted UID.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeIncludesNestedPublicTypesWithDottedFullName()
    {
        var uids = PublicSurfaceProbe.ProbePublicTypeUids([GetSamplePdbPath()]);

        await Assert.That(uids).Contains("T:SamplePdb.Outer.Nested");
    }

    /// <summary>Generic types (whose metadata name carries a backtick, not angle brackets) survive the filter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeIncludesGenericTypesViaBacktickName()
    {
        var uids = PublicSurfaceProbe.ProbePublicTypeUids([GetSamplePdbPath()]);

        await Assert.That(uids).Contains("T:SamplePdb.GenericContainer`1");
    }

    /// <summary>The <c>&lt;Module&gt;</c> pseudo-type and any compiler-generated <c>&lt;...&gt;</c> closures are filtered.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeFiltersCompilerGeneratedAndModulePseudoType()
    {
        foreach (var uid in PublicSurfaceProbe.ProbePublicTypeUids([GetSamplePdbPath()]))
        {
            await Assert.That(uid.Contains('<', StringComparison.Ordinal)).IsFalse();
            await Assert.That(uid.Contains('>', StringComparison.Ordinal)).IsFalse();
        }
    }

    /// <summary>Every emitted UID begins with the Roslyn-style <c>T:</c> prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeEmitsCommentIdPrefixOnEveryUid()
    {
        var uids = PublicSurfaceProbe.ProbePublicTypeUids([GetSamplePdbPath()]);

        await Assert.That(uids.Count).IsGreaterThan(0);
        foreach (var uid in uids)
        {
            await Assert.That(uid.StartsWith("T:", StringComparison.Ordinal)).IsTrue();
        }
    }

    /// <summary>Non-managed files (a plain text blob on disk) are silently skipped via <see cref="BadImageFormatException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeSilentlySkipsNonManagedFiles()
    {
        using var scratch = new TempDirectory();
        var bogus = Path.Combine(scratch.Path, "not-an-assembly.dll");
        await File.WriteAllTextAsync(bogus, "this is plainly not a PE/COFF image");

        var uids = PublicSurfaceProbe.ProbePublicTypeUids([bogus]);

        await Assert.That(uids).IsEmpty();
    }

    /// <summary>An empty input list produces an empty UID set without touching the disk.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeReturnsEmptySetForEmptyInput()
    {
        var uids = PublicSurfaceProbe.ProbePublicTypeUids([]);

        await Assert.That(uids).IsEmpty();
    }

    /// <summary>A null path list throws <see cref="ArgumentNullException"/> (the argument is the entry contract).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeThrowsForNullList() =>
        await Assert.That(() => PublicSurfaceProbe.ProbePublicTypeUids(null!)).Throws<ArgumentNullException>();

    /// <summary>Multiple inputs are aggregated into one set; probing the same DLL twice does not duplicate UIDs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeDeduplicatesAcrossMultipleInputs()
    {
        var path = GetSamplePdbPath();

        var single = PublicSurfaceProbe.ProbePublicTypeUids([path]);
        var doubled = PublicSurfaceProbe.ProbePublicTypeUids([path, path]);

        await Assert.That(doubled.Count).IsEqualTo(single.Count);
    }

    /// <summary>A missing file is treated as unprobed (the IO failure is swallowed, not surfaced).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeSilentlySkipsMissingFiles()
    {
        using var scratch = new TempDirectory();
        var ghost = Path.Combine(scratch.Path, "does-not-exist.dll");

        // FileNotFoundException derives from IOException, which the
        // probe catches alongside contention failures.
        var uids = PublicSurfaceProbe.ProbePublicTypeUids([ghost]);

        await Assert.That(uids).IsEmpty();
    }

    /// <summary>Returns the on-disk path of the SamplePdb fixture assembly that ships next to the tests.</summary>
    /// <returns>The absolute path to <c>SamplePdb.dll</c>.</returns>
    private static string GetSamplePdbPath() => typeof(SamplePdbAnchor).Assembly.Location;
}
