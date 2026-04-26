// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;

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
            new PackageRoutingRule(FolderName: "ReactiveUI", AssemblyPrefix: "ReactiveUI"),
        ]);
        var emitter = new NavigationEmitter(options);
        var typeA = TestData.ObjectType("Foo", assemblyName: "ReactiveUI") with { Namespace = "ReactiveUI" };

        var yaml = emitter.EmitYaml([typeA]);

        await Assert.That(yaml).Contains("- API:");
        await Assert.That(yaml).Contains("  - ReactiveUI:");
        await Assert.That(yaml).Contains("    - ReactiveUI:");
        await Assert.That(yaml).Contains("      - Foo: ReactiveUI/ReactiveUI/Foo.md");
    }

    /// <summary>YAML output buckets unrouted types under the API fallback folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitYamlFallsBackToApiFolderWhenUnrouted()
    {
        var emitter = new NavigationEmitter(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo") with { Namespace = "Bar" };

        var yaml = emitter.EmitYaml([type]);

        await Assert.That(yaml).Contains("  - API:");
        await Assert.That(yaml).Contains("    - Bar:");
        await Assert.That(yaml).Contains("      - Foo: Bar/Foo.md");
    }

    /// <summary>TOML output uses the project.nav array-of-tables shape Zensical expects.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitTomlProducesProjectNavArray()
    {
        var emitter = new NavigationEmitter(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo") with { Namespace = "Bar" };

        var toml = emitter.EmitToml([type]);

        await Assert.That(toml).Contains("[[project.nav]]");
        await Assert.That(toml).Contains("title = \"API\"");
        await Assert.That(toml).Contains("{ title = \"Bar\", nav = [");
        await Assert.That(toml).Contains("{ title = \"Foo\", path = \"Bar/Foo.md\" }");
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
}
