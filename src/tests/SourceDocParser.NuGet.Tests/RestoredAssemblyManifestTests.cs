// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies the fetcher-to-discovery contract for restored assembly groups.</summary>
public sealed class RestoredAssemblyManifestTests
{
    /// <summary>The input file included in the manifest identity.</summary>
    private const string PackageManifest = "nuget-packages.json";

    /// <summary>The output used by serialization tests.</summary>
    private const string GroupManifest = "groups.json";

    /// <summary>A framework shared by independent package roots.</summary>
    private const string Framework = "net10.0";

    /// <summary>The shared assembly name that must remain isolated between groups.</summary>
    private const string Shared = "Shared";

    /// <summary>The manifest identity includes both the root directory and exact package declarations.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task IdentityIncludesRootDirectoryAndPackageDeclarations()
    {
        using var first = new ScratchDirectory();
        using var second = new ScratchDirectory();
        using var output = new ScratchDirectory();
        await File.WriteAllTextAsync(Path.Combine(first.Path, PackageManifest), "{}");
        await File.WriteAllTextAsync(Path.Combine(second.Path, PackageManifest), "{}");
        var firstPath = await RestoredAssemblyManifest.GetPathAsync(first.Path, output.Path, CancellationToken.None);
        var secondPath = await RestoredAssemblyManifest.GetPathAsync(second.Path, output.Path, CancellationToken.None);
        await RestoredAssemblyManifest.WriteAsync(firstPath!, [], CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(first.Path, PackageManifest), """{"additionalPackages":[]}""");
        var changedPath = await RestoredAssemblyManifest.GetPathAsync(first.Path, output.Path, CancellationToken.None);
        var changed = await RestoredAssemblyManifest.ReadAsync(changedPath, CancellationToken.None);

        await Assert.That(firstPath).IsNotEqualTo(secondPath);
        await Assert.That(changedPath).IsNotEqualTo(firstPath);
        await Assert.That(changed).IsNull();
    }

    /// <summary>References with the same name remain separate for different documentation roots.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RoundTripPreservesIndependentGraphsAndBroadcastFrameworks()
    {
        using var directory = new ScratchDirectory();
        var first = Path.Combine(directory.Path, "first.dll");
        var second = Path.Combine(directory.Path, "second.dll");
        await File.WriteAllBytesAsync(first, []);
        await File.WriteAllBytesAsync(second, []);
        Dictionary<string, string> firstReferences = [with(StringComparer.OrdinalIgnoreCase)];
        Dictionary<string, string> secondReferences = [with(StringComparer.OrdinalIgnoreCase)];
        firstReferences.Add(Shared, first);
        secondReferences.Add(Shared, second);
        AssemblyGroup[] groups = [new(Framework, [first], firstReferences, ["net9.0"]), new(Framework, [second], secondReferences)];
        var path = Path.Combine(directory.Path, GroupManifest);

        await RestoredAssemblyManifest.WriteAsync(path, groups, CancellationToken.None);
        var restored = await RestoredAssemblyManifest.ReadAsync(path, CancellationToken.None);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Length).IsEqualTo(groups.Length);
        for (var i = 0; i < groups.Length; i++)
        {
            await Assert.That(restored[i].AssemblyPaths).IsEquivalentTo(groups[i].AssemblyPaths);
            await Assert.That(restored[i].FallbackIndex).IsEquivalentTo(groups[i].FallbackIndex);
            await Assert.That(restored[i].BroadcastTfms).IsEquivalentTo(groups[i].BroadcastTfms);
        }
    }

    /// <summary>A validation failure discards the partial file and preserves the published groups.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task InvalidWritePreservesPublishedManifest()
    {
        using var directory = new ScratchDirectory();
        var path = Path.Combine(directory.Path, GroupManifest);
        await RestoredAssemblyManifest.WriteAsync(path, [], CancellationToken.None);
        var before = await File.ReadAllBytesAsync(path);
        AssemblyGroup[] invalid = [new(string.Empty, [], [])];

        await Assert.That(() => RestoredAssemblyManifest.WriteAsync(path, invalid, CancellationToken.None)).Throws<ArgumentException>();

        var after = await File.ReadAllBytesAsync(path);
        await Assert.That(before.AsSpan().SequenceEqual(after)).IsTrue();
        await Assert.That(Directory.GetFiles(directory.Path, "*.tmp-*")).IsEmpty();
        var restored = await RestoredAssemblyManifest.ReadAsync(path, CancellationToken.None);
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!).IsEmpty();
    }

    /// <summary>Cancellation preserves a published manifest and leaves no partial file.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CanceledWritePreservesPublishedManifest()
    {
        using var directory = new ScratchDirectory();
        var path = Path.Combine(directory.Path, GroupManifest);
        await RestoredAssemblyManifest.WriteAsync(path, [], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.That(() => RestoredAssemblyManifest.WriteAsync(path, [], cancellation.Token)).Throws<OperationCanceledException>();

        var restored = await RestoredAssemblyManifest.ReadAsync(path, CancellationToken.None);
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!).IsEmpty();
        await Assert.That(Directory.GetFiles(directory.Path, "*.tmp-*")).IsEmpty();
    }

    /// <summary>A malformed manifest cannot redirect parsing through a relative assembly path.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RelativeAssemblyPathIsRejected()
    {
        using var directory = new ScratchDirectory();
        var path = Path.Combine(directory.Path, GroupManifest);
        await File.WriteAllTextAsync(path, """{"formatVersion":1,"groups":[{"tfm":"net10.0","assemblies":["relative.dll"],"broadcastTfms":[],"references":{}}]}""");

        await Assert.That(async () => { _ = await RestoredAssemblyManifest.ReadAsync(path, CancellationToken.None); }).Throws<InvalidDataException>();
    }
}
