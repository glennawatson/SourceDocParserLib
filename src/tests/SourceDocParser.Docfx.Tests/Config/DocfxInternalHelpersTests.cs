// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Common;
using SourceDocParser.Docfx.Config;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Config;

/// <summary>
/// Direct tests for the internal docfx helper methods that the public
/// writer/emitter build on.
/// </summary>
public class DocfxInternalHelpersTests
{
    /// <summary>
    /// Only immediate child directories that contain at least one DLL are treated as TFMs.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverTfmsReturnsSortedImmediateDllDirectories()
    {
        using var scratch = new ScratchDirectory("sdp-docfx-helpers");
        Directory.CreateDirectory(Path.Combine(scratch.Path, "net8.0"));
        Directory.CreateDirectory(Path.Combine(scratch.Path, "net10.0"));
        Directory.CreateDirectory(Path.Combine(scratch.Path, "empty"));
        File.WriteAllBytes(Path.Combine(scratch.Path, "net10.0", "A.dll"), []);
        File.WriteAllBytes(Path.Combine(scratch.Path, "net8.0", "B.dll"), []);

        var tfms = DocfxInternalHelpers.DiscoverTfms(scratch.Path);

        await Assert.That(tfms).IsEquivalentTo(["net10.0", "net8.0"]);
        await Assert.That(tfms[0]).IsEqualTo("net10.0");
        await Assert.That(tfms[1]).IsEqualTo("net8.0");
    }

    /// <summary>
    /// Package DLL discovery excludes names already present in the matching refs/ directory.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectPackageDllNamesExcludesReferenceMatches()
    {
        using var scratch = new ScratchDirectory("sdp-docfx-helpers");
        File.WriteAllBytes(Path.Combine(scratch.Path, "Package.dll"), []);
        File.WriteAllBytes(Path.Combine(scratch.Path, "Shared.dll"), []);

        var packageDlls = DocfxInternalHelpers.CollectPackageDllNames(
            scratch.Path,
            new(StringComparer.OrdinalIgnoreCase) { "Shared.dll" });

        await Assert.That(packageDlls.Count).IsEqualTo(1);
        await Assert.That(packageDlls[0]).IsEqualTo("Package.dll");
    }

    /// <summary>
    /// refs/ DLL-name scans are cached per TFM and reused on subsequent lookups.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetOrAddDllNamesReusesCachedSet()
    {
        using var scratch = new ScratchDirectory("sdp-docfx-helpers");
        File.WriteAllBytes(Path.Combine(scratch.Path, "A.dll"), []);
        var cache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var first = DocfxInternalHelpers.GetOrAddDllNames(cache, "net10.0", scratch.Path);
        File.WriteAllBytes(Path.Combine(scratch.Path, "B.dll"), []);
        var second = DocfxInternalHelpers.GetOrAddDllNames(cache, "net10.0", scratch.Path);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(second.Contains("A.dll")).IsTrue();
        await Assert.That(second.Contains("B.dll")).IsFalse();
    }

    /// <summary>
    /// Platform content patching removes old injected entries and appends a fresh ordered set.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PatchBuildSectionReplacesInjectedPlatformEntries()
    {
        var template = new DocfxBuildSection(
        [
            new(["**.md"]),
            new(["api-ios/**.yml", "api-ios/index.md"]),
            new(["api/**.yml", "api/index.md"]),
        ]);

        var patched = DocfxInternalHelpers.PatchBuildSection(template, ["android", "ios"]);

        await Assert.That(patched.Content.Length).IsEqualTo(4);
        await Assert.That(patched.Content[0].Files![0]).IsEqualTo("**.md");
        await Assert.That(patched.Content[1].Files![0]).IsEqualTo("api/**.yml");
        await Assert.That(patched.Content[2].Files![0]).IsEqualTo("api-android/**.yml");
        await Assert.That(patched.Content[3].Files![0]).IsEqualTo("api-ios/**.yml");
    }

    /// <summary>
    /// File-stem sanitization replaces filesystem-hostile characters and leaves safe strings untouched.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SanitiseFileStemHandlesSafeAndUnsafeValues()
    {
        await Assert.That(DocfxInternalHelpers.SanitiseFileStem("Foo.Bar")).IsEqualTo("Foo.Bar");
        await Assert.That(DocfxInternalHelpers.SanitiseFileStem("T:Foo/Bar<Baz>\"Qux\"")).IsEqualTo("T_Foo_Bar_Baz__Qux_");
    }
}
