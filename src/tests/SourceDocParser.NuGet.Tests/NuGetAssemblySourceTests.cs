// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Constructor-level coverage for <see cref="NuGetAssemblySource"/>.
/// Heavier scenarios that touch nuget.org live in
/// <c>SourceDocParser.IntegrationTests</c>.
/// </summary>
public class NuGetAssemblySourceTests
{
    /// <summary>Constructing with a null root directory throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullRootDirectory()
    {
        var act = () => new NuGetAssemblySource(rootDirectory: null!, apiPath: "/tmp/api");
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    /// <summary>Constructing with a null api path throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullApiPath()
    {
        var act = () => new NuGetAssemblySource(rootDirectory: "/tmp/repo", apiPath: null!);
        await Assert.That(act).Throws<ArgumentNullException>();
    }
}
