// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Common;
using SourceDocParser.Docfx.Config;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Config;

/// <summary>Direct tests for the internal docfx helper methods that the public writer/emitter build on.</summary>
public class DocfxInternalHelpersTests
{
    /// <summary>Prefix for isolated helper fixture directories.</summary>
    private const string ScratchDirectoryPrefix = "sdp-docfx-helpers";

    /// <summary>Target framework for the .NET 8 fixture.</summary>
    private const string Net80 = "net8.0";

    /// <summary>Target framework for the .NET 10 fixture.</summary>
    private const string Net100 = "net10.0";

    /// <summary>File name of the first assembly fixture.</summary>
    private const string AssemblyAFileName = "A.dll";

    /// <summary>File name of the second assembly fixture.</summary>
    private const string AssemblyBFileName = "B.dll";

    /// <summary>Only immediate child directories that contain at least one DLL are treated as TFMs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverTfmsReturnsSortedImmediateDllDirectories()
    {
        using var scratch = new ScratchDirectory(ScratchDirectoryPrefix);
        _ = Directory.CreateDirectory(Path.Combine(scratch.Path, Net80));
        _ = Directory.CreateDirectory(Path.Combine(scratch.Path, Net100));
        _ = Directory.CreateDirectory(Path.Combine(scratch.Path, "empty"));
        await File.WriteAllBytesAsync(Path.Combine(scratch.Path, Net100, AssemblyAFileName), []);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Path, Net80, AssemblyBFileName), []);

        var tfms = DocfxInternalHelpers.DiscoverTfms(scratch.Path);

        await Assert.That(tfms).IsEquivalentTo([Net100, Net80]);
        await Assert.That(tfms[0]).IsEqualTo(Net100);
        await Assert.That(tfms[1]).IsEqualTo(Net80);
    }

    /// <summary>Package DLL discovery excludes names already present in the matching refs/ directory.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectPackageDllNamesExcludesReferenceMatches()
    {
        using var scratch = new ScratchDirectory(ScratchDirectoryPrefix);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Path, "Package.dll"), []);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Path, "Shared.dll"), []);

        var packageDlls = DocfxInternalHelpers.CollectPackageDllNames(
            scratch.Path,
            [with(StringComparer.OrdinalIgnoreCase), "Shared.dll"]);

        await Assert.That(packageDlls.Count).IsEqualTo(1);
        await Assert.That(packageDlls[0]).IsEqualTo("Package.dll");
    }

    /// <summary>Refs/ DLL-name scans are cached per TFM and reused on subsequent lookups.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetOrAddDllNamesReusesCachedSet()
    {
        using var scratch = new ScratchDirectory(ScratchDirectoryPrefix);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Path, AssemblyAFileName), []);
        Dictionary<string, HashSet<string>> cache = [with(StringComparer.OrdinalIgnoreCase)];

        var first = DocfxInternalHelpers.GetOrAddDllNames(cache, Net100, scratch.Path);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Path, AssemblyBFileName), []);
        var second = DocfxInternalHelpers.GetOrAddDllNames(cache, Net100, scratch.Path);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(second.Contains(AssemblyAFileName)).IsTrue();
        await Assert.That(second.Contains(AssemblyBFileName)).IsFalse();
    }

    /// <summary>Platform content patching removes old injected entries and appends a fresh ordered set.</summary>
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

        await Assert.That(patched.Content.Length).IsEqualTo(template.Content.Length + 1);
        await Assert.That(patched.Content[0].Files![0]).IsEqualTo("**.md");
        await Assert.That(patched.Content[1].Files![0]).IsEqualTo("api/**.yml");
        await Assert.That(patched.Content[2].Files![0]).IsEqualTo("api-android/**.yml");
        await Assert.That(patched.Content[3].Files![0]).IsEqualTo("api-ios/**.yml");
    }

    /// <summary>File-stem sanitization replaces filesystem-hostile characters and leaves safe strings untouched.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SanitiseFileStemHandlesSafeAndUnsafeValues()
    {
        await Assert.That(DocfxInternalHelpers.SanitiseFileStem("Foo.Bar")).IsEqualTo("Foo.Bar");
        await Assert.That(DocfxInternalHelpers.SanitiseFileStem("T:Foo/Bar<Baz>\"Qux\"")).IsEqualTo("T_Foo_Bar_Baz__Qux_");
    }
}
