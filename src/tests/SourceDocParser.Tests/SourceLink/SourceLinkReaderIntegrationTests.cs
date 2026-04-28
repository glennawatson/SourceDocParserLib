// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection;
using SamplePdb;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests.SourceLink;

/// <summary>
/// End-to-end coverage of <see cref="SourceLinkReader"/> against a
/// real on-disk DLL -- the SamplePdb fixture builds with an embedded
/// portable PDB and the Microsoft.SourceLink.GitHub package wired in
/// (inherited via Directory.Build.props), so each build produces an
/// assembly carrying the full SourceLink JSON map for this repo's
/// git origin. Pins the three public surfaces synthetic tests can't
/// cover: HasSourceLink, GetMethodLocation row arithmetic, and
/// ResolveRawUrl path-to-URL substitution.
/// </summary>
public class SourceLinkReaderIntegrationTests
{
    /// <summary>The value returned by the SamplePdb anchor method.</summary>
    private const int ExpectedAnchorReturnValue = 42;

    /// <summary>The exact source line the anchor method should occupy in the fixture source file.</summary>
    private const string ExpectedAnchorMethodSourceLine = "    public static int Anchor() => AnchorReturnValue;";

    /// <summary>Gets the on-disk path to the SamplePdb fixture assembly, sitting next to the test bin.</summary>
    private static string SamplePdbAssemblyPath => typeof(SamplePdbAnchor).Assembly.Location;

    /// <summary>Opening a real DLL with an embedded PDB + SourceLink JSON yields HasSourceLink = true.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasSourceLinkIsTrueForFixtureAssembly()
    {
        using var reader = new SourceLinkReader(SamplePdbAssemblyPath);

        await Assert.That(reader.HasSourceLink).IsTrue();
    }

    /// <summary>
    /// GetMethodLocation against the SamplePdb anchor returns the
    /// SamplePdbAnchor.cs source path and the body line pinned by
    /// <see cref="SamplePdb.SamplePdbAnchor.KnownMethodBodyLine"/> --
    /// keeps the test in lockstep with the fixture: any time the
    /// body moves, the const moves with it.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetMethodLocationResolvesAnchorMethod()
    {
        using var reader = new SourceLinkReader(SamplePdbAssemblyPath);
        var anchorMethod = typeof(SamplePdbAnchor).GetMethod(nameof(SamplePdbAnchor.Anchor), BindingFlags.Public | BindingFlags.Static)!;

        var location = reader.GetMethodLocation(anchorMethod.MetadataToken);

        await Assert.That(location).IsNotNull();
        await Assert.That(location!.Value.LocalPath).EndsWith("SamplePdbAnchor.cs");
        await Assert.That(location.Value.StartLine).IsEqualTo(SamplePdbAnchor.KnownMethodBodyLine);
    }

    /// <summary>
    /// The SamplePdb anchor fixture stays self-consistent: its pinned
    /// line constant still points at the anchor method declaration in
    /// the checked-in source file.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AnchorFixtureLineConstantMatchesSourceFile()
    {
        using var reader = new SourceLinkReader(SamplePdbAssemblyPath);
        var anchorMethod = typeof(SamplePdbAnchor).GetMethod(nameof(SamplePdbAnchor.Anchor), BindingFlags.Public | BindingFlags.Static)!;
        var location = reader.GetMethodLocation(anchorMethod.MetadataToken);

        await Assert.That(location).IsNotNull();

        var sourceLines = await File.ReadAllLinesAsync(location!.Value.LocalPath);

        await Assert.That(sourceLines.Length).IsGreaterThanOrEqualTo(SamplePdbAnchor.KnownMethodBodyLine);
        await Assert.That(sourceLines[SamplePdbAnchor.KnownMethodBodyLine - 1]).IsEqualTo(ExpectedAnchorMethodSourceLine);
    }

    /// <summary>
    /// The anchor method returns the constant value the fixture is meant
    /// to pin, so refactors of its implementation still preserve the
    /// expected behavior.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AnchorFixtureReturnsExpectedValue() =>
        await Assert.That(SamplePdbAnchor.Anchor()).IsEqualTo(ExpectedAnchorReturnValue);

    /// <summary>
    /// GetMethodLocation against a metadata token whose row part is
    /// zero (a TypeRef row, an out-of-range RID, etc.) returns null
    /// rather than throwing -- the helper's defensive contract.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetMethodLocationReturnsNullForInvalidToken()
    {
        using var reader = new SourceLinkReader(SamplePdbAssemblyPath);

        var location = reader.GetMethodLocation(metadataToken: 0);

        await Assert.That(location).IsNull();
    }

    /// <summary>
    /// ResolveRawUrl maps a known local source path through the
    /// SourceLink JSON. Microsoft.SourceLink.GitHub generates a
    /// wildcard map rooted at this repo's local checkout, so any
    /// path inside the repo resolves to a github.com raw URL.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveRawUrlMapsSourcePathToGitHubRawUrl()
    {
        using var reader = new SourceLinkReader(SamplePdbAssemblyPath);
        var anchorMethod = typeof(SamplePdbAnchor).GetMethod(nameof(SamplePdbAnchor.Anchor), BindingFlags.Public | BindingFlags.Static)!;
        var location = reader.GetMethodLocation(anchorMethod.MetadataToken)!.Value;

        var rawUrl = reader.ResolveRawUrl(location.LocalPath);

        await Assert.That(rawUrl).IsNotNull();
        await Assert.That(rawUrl!).Contains("raw.githubusercontent.com");
        await Assert.That(rawUrl).EndsWith("SamplePdbAnchor.cs");
    }

    /// <summary>
    /// ResolveRawUrl returns null for a path that doesn't sit under
    /// any of the SourceLink map's roots.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveRawUrlReturnsNullForUnmappedPath()
    {
        using var reader = new SourceLinkReader(SamplePdbAssemblyPath);

        var rawUrl = reader.ResolveRawUrl("/no/such/path/Foo.cs");

        await Assert.That(rawUrl).IsNull();
    }

    /// <summary>
    /// Pointing the reader at a path that isn't a managed PE flips
    /// HasSourceLink to false instead of throwing -- the constructor
    /// catches every failure and degrades gracefully.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasSourceLinkIsFalseForNonExistentPath()
    {
        using var reader = new SourceLinkReader("/does/not/exist.dll");

        await Assert.That(reader.HasSourceLink).IsFalse();
    }

    /// <summary>
    /// GetMethodLocation against a reader whose constructor failed
    /// (so <c>_pdbReader</c> is null) returns null without throwing.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetMethodLocationReturnsNullWhenPdbReaderIsNull()
    {
        using var reader = new SourceLinkReader("/does/not/exist.dll");

        var location = reader.GetMethodLocation(0x06000001);

        await Assert.That(location).IsNull();
    }
}
