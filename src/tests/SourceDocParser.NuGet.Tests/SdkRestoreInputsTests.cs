// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies SDK-derived dependency defaults and their cache boundaries.</summary>
public sealed class SdkRestoreInputsTests
{
    /// <summary>The modern framework used to exercise SDK package pruning.</summary>
    private const string TargetFramework = "net10.0";

    /// <summary>The explicit dependency retained during SDK evaluation.</summary>
    private const string RootPackage = "Documentation.Root";

    /// <summary>The dependency version supplied by the caller.</summary>
    private const string RootVersion = "[1.0.0]";

    /// <summary>SDK pruning and runtime compatibility metadata accompany unchanged root constraints.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ApplyAsyncPreservesRootAndUsesSdkPruning()
    {
        using var session = new PackageRestoreSession(Path.GetTempPath(), NullLogger.Instance);
        var target = new TargetFrameworkInformation
        {
            FrameworkName = NuGetFramework.ParseFolder(TargetFramework),
            Dependencies = [PackageGraphRestore.Dependency(RootPackage, VersionRange.Parse(RootVersion))],
        };

        var result = await SdkRestoreInputs.ApplyAsync(session, target, null, CancellationToken.None);

        await Assert.That(result.Dependencies.Length).IsEqualTo(1);
        await Assert.That(result.Dependencies[0].Name).IsEqualTo(RootPackage);
        await Assert.That(result.Dependencies[0].LibraryRange.VersionRange).IsEqualTo(VersionRange.Parse(RootVersion));
        await Assert.That(result.PackagesToPrune["System.Security.AccessControl"].VersionRange.Satisfies(NuGetVersion.Parse("4.5.0"))).IsTrue();
        await Assert.That(File.Exists(result.RuntimeIdentifierGraphPath)).IsTrue();
        var hasWindowsPack = false;
        for (var i = 0; i < result.DownloadDependencies.Length; i++)
        {
            hasWindowsPack |= result.DownloadDependencies[i].Name.StartsWith("Microsoft.Windows", StringComparison.OrdinalIgnoreCase);
        }

        await Assert.That(hasWindowsPack).IsFalse();
    }

    /// <summary>Cached SDK metadata produces equivalent target inputs without sharing caller dependency changes.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ApplyAsyncCacheRetainsEachCallersDependencyConstraints()
    {
        using var session = new PackageRestoreSession(Path.GetTempPath(), NullLogger.Instance);
        var target = new TargetFrameworkInformation { FrameworkName = NuGetFramework.ParseFolder(TargetFramework) };
        var first = await SdkRestoreInputs.ApplyAsync(session, target, null, CancellationToken.None);
        var pinned = new TargetFrameworkInformation(target) { Dependencies = [PackageGraphRestore.Dependency(RootPackage, VersionRange.Parse(RootVersion))] };

        var second = await SdkRestoreInputs.ApplyAsync(session, pinned, null, CancellationToken.None);

        await Assert.That(second.PackagesToPrune.Count).IsEqualTo(first.PackagesToPrune.Count);
        await Assert.That(second.RuntimeIdentifierGraphPath).IsEqualTo(first.RuntimeIdentifierGraphPath);
        await Assert.That(first.Dependencies).IsEmpty();
        await Assert.That(second.Dependencies.Length).IsEqualTo(1);
        await Assert.That(second.Dependencies[0].Name).IsEqualTo(RootPackage);
    }

    /// <summary>Cancellation is honored before SDK evaluation or cached inputs are used.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ApplyAsyncRejectsCanceledRequests()
    {
        using var session = new PackageRestoreSession(Path.GetTempPath(), NullLogger.Instance);
        var target = new TargetFrameworkInformation { FrameworkName = NuGetFramework.ParseFolder(TargetFramework) };

        await Assert.That(async () =>
        {
            _ = await SdkRestoreInputs.ApplyAsync(session, target, null, new(true));
        }).Throws<OperationCanceledException>();
    }
}
