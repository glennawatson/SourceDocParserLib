// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Navigation;
using SourceDocParser.Zensical.Options;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the YAML and TOML output shapes of <see cref="NavigationEmitter"/>
/// — package grouping, namespace ordering, page paths, fallback when no
/// routing rule matches, scalar quoting.
/// </summary>
public class NavigationEmitterTests
{
    /// <summary>YAML output groups types under their routed package folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitYamlGroupsByRoutedPackage()
    {
        var options = new ZensicalEmitterOptions([
            new(FolderName: "ReactiveUI", AssemblyPrefix: "ReactiveUI"),
        ]);
        var emitter = new NavigationEmitter(options);
        var typeA = TestData.ObjectType("Foo", assemblyName: "ReactiveUI") with { Namespace = "ReactiveUI" };

        var yaml = emitter.EmitYaml([typeA]);

        await Assert.That(yaml).Contains("- API:");
        await Assert.That(yaml).Contains("  - ReactiveUI:");
        await Assert.That(yaml).Contains("    - ReactiveUI:");
        await Assert.That(yaml).Contains("      - Foo: ReactiveUI/ReactiveUI/Foo.md");
    }

    /// <summary>Without explicit rules the assembly name is the package folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitYamlDefaultsPackageToAssemblyName()
    {
        var emitter = new NavigationEmitter(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", assemblyName: "Splat") with { Namespace = "Bar" };

        var yaml = emitter.EmitYaml([type]);

        await Assert.That(yaml).Contains("  - Splat:");
        await Assert.That(yaml).Contains("    - Bar:");
        await Assert.That(yaml).Contains("      - Foo: Splat/Bar/Foo.md");
    }

    /// <summary>TOML output uses the project.nav array-of-tables shape Zensical expects.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitTomlProducesProjectNavArray()
    {
        var emitter = new NavigationEmitter(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", assemblyName: "Splat") with { Namespace = "Bar" };

        var toml = emitter.EmitToml([type]);

        await Assert.That(toml).Contains("[[project.nav]]");
        await Assert.That(toml).Contains("title = \"API\"");
        await Assert.That(toml).Contains("{ title = \"Splat\", nav = [");
        await Assert.That(toml).Contains("{ title = \"Bar\", nav = [");
        await Assert.That(toml).Contains("{ title = \"Foo\", path = \"Splat/Bar/Foo.md\" }");
    }

    /// <summary>Types within a namespace are sorted ordinally so output is deterministic.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EntriesAreOrderedAlphabetically()
    {
        var emitter = new NavigationEmitter(ZensicalEmitterOptions.Default);
        var zType = TestData.ObjectType("Zeta") with { Namespace = "Bar" };
        var aType = TestData.ObjectType("Alpha") with { Namespace = "Bar" };

        var yaml = emitter.EmitYaml([zType, aType]);
        var alphaIndex = yaml.IndexOf("Alpha", StringComparison.Ordinal);
        var zetaIndex = yaml.IndexOf("Zeta", StringComparison.Ordinal);

        await Assert.That(alphaIndex).IsGreaterThan(0);
        await Assert.That(alphaIndex).IsLessThan(zetaIndex);
    }

    /// <summary>Types without a namespace are bucketed under <c>(global)</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypesWithoutNamespaceFallToGlobalBucket()
    {
        var emitter = new NavigationEmitter(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", assemblyName: "Splat") with { Namespace = string.Empty };

        var yaml = emitter.EmitYaml([type]);

        await Assert.That(yaml).Contains("    - (global):");
    }
}
