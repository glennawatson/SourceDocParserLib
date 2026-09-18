// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.RuntimeModel;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Checks when restored package graphs require MSBuild reference evaluation.</summary>
public sealed class PackageBuildReferencesTests
{
    /// <summary>Graphs without executable build assets leave the SDK and reference set untouched.</summary>
    /// <param name="emptyMarkers">Whether NuGet empty-group markers are present.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GraphWithoutBuildAssetsDoesNotEvaluateSdk(bool emptyMarkers)
    {
        using var directory = new ScratchDirectory();
        using var session = new PackageRestoreSession(directory.Path, NullLogger.Instance);
        var framework = NuGetFramework.ParseFolder("net10.0");
        var path = Path.Combine(directory.Path, "documentation.csproj");
        var project = new PackageSpec([new TargetFrameworkInformation { FrameworkName = framework }])
        {
            FilePath = path,
            RuntimeGraph = RuntimeGraph.Empty,
            RestoreMetadata = new() { OutputPath = directory.Path },
        };
        var target = new LockFileTarget { TargetFramework = framework, RuntimeIdentifier = string.Empty };
        var library = new LockFileTargetLibrary { Name = "Root" };
        if (emptyMarkers)
        {
            library.Build.Add(new("build/net10.0/_._"));
            library.BuildMultiTargeting.Add(new("buildMultiTargeting/_._"));
        }

        target.Libraries.Add(library);
        var assets = new LockFile();
        assets.Targets.Add(target);
        Dictionary<string, string> references = [with(StringComparer.OrdinalIgnoreCase)];
        references.Add("Selected", "selected.dll");

        await PackageBuildReferences.AddAsync(session, project, assets, references, CancellationToken.None);

        await Assert.That(references.Count).IsEqualTo(1);
        await Assert.That(references["Selected"]).IsEqualTo("selected.dll");
        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(directory.Path, PackageBuildReferences.ResultFileName))).IsFalse();
    }
}
