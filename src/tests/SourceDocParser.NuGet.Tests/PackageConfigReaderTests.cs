// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="PackageConfigReader.Read"/> against malformed and
/// minimal JSON shapes — the regular happy path is exercised by the
/// integration tests, this file owns the empty-array / wrong-shape /
/// missing-property branches plus the every-display-name fall-through.
/// </summary>
public class PackageConfigReaderTests
{
    /// <summary>Read rejects a blank path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadRejectsBlankPath()
    {
        await Assert.That(() => PackageConfigReader.Read(string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => PackageConfigReader.Read("   ")).Throws<ArgumentException>();
    }

    /// <summary>Read throws when the JSON root is not an object.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadThrowsWhenRootIsNotObject()
    {
        var path = WriteTemp("[]");
        try
        {
            await Assert.That(() => PackageConfigReader.Read(path)).Throws<JsonException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An empty JSON object yields a config with all empty collections.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadEmptyObjectReturnsEmptyConfig()
    {
        var path = WriteTemp("{}");
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.NugetPackageOwners).IsEmpty();
            await Assert.That(config.TfmPreference).IsEmpty();
            await Assert.That(config.AdditionalPackages).IsEmpty();
            await Assert.That(config.ExcludePackages).IsEmpty();
            await Assert.That(config.ExcludePackagePrefixes).IsEmpty();
            await Assert.That(config.ReferencePackages).IsEmpty();
            await Assert.That(config.TfmOverrides).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>String-array properties of the wrong shape are treated as absent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadStringArrayPropertyOfWrongShapeReturnsEmpty()
    {
        var path = WriteTemp("""{"nugetPackageOwners":"not-an-array"}""");
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.NugetPackageOwners).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An additionalPackages property of the wrong shape is treated as absent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadAdditionalPackagesOfWrongShapeReturnsEmpty()
    {
        var path = WriteTemp("""{"additionalPackages":{"id":"Foo"}}""");
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.AdditionalPackages).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A referencePackages property of the wrong shape is treated as absent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadReferencePackagesOfWrongShapeReturnsEmpty()
    {
        var path = WriteTemp("""{"referencePackages":42}""");
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.ReferencePackages).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A tfmOverrides property of the wrong shape yields an empty dictionary.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadTfmOverridesOfWrongShapeReturnsEmpty()
    {
        var path = WriteTemp("""{"tfmOverrides":[]}""");
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.TfmOverrides).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A populated tfmOverrides dictionary maps each entry into the result.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadTfmOverridesPopulatesDictionary()
    {
        var path = WriteTemp("""{"tfmOverrides":{"net6.0":"net8.0","netstandard2.0":"net8.0"}}""");
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.TfmOverrides.Count).IsEqualTo(2);
            await Assert.That(config.TfmOverrides["net6.0"]).IsEqualTo("net8.0");
            await Assert.That(config.TfmOverrides["netstandard2.0"]).IsEqualTo("net8.0");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An additionalPackages entry without a required <c>id</c> throws with a context message.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadAdditionalPackagesMissingIdThrows()
    {
        var path = WriteTemp("""{"additionalPackages":[{"version":"1.0.0"}]}""");
        try
        {
            await Assert.That(() => PackageConfigReader.Read(path)).Throws<JsonException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A referencePackages entry without a required <c>id</c> throws with a context message.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadReferencePackagesMissingIdThrows()
    {
        var path = WriteTemp("""{"referencePackages":[{"version":"1.0.0","targetTfm":"net8.0"}]}""");
        try
        {
            await Assert.That(() => PackageConfigReader.Read(path)).Throws<JsonException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A populated referencePackages array honours the optional fields and pathPrefix default.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadReferencePackagesPopulatesEntries()
    {
        var path = WriteTemp("""
            {
              "referencePackages": [
                { "id": "Foo", "version": "1.2.3", "targetTfm": "net8.0", "pathPrefix": "lib" },
                { "id": "Bar" }
              ]
            }
            """);
        try
        {
            var config = PackageConfigReader.Read(path);

            await Assert.That(config.ReferencePackages.Length).IsEqualTo(2);
            await Assert.That(config.ReferencePackages[0].Id).IsEqualTo("Foo");
            await Assert.That(config.ReferencePackages[0].Version).IsEqualTo("1.2.3");
            await Assert.That(config.ReferencePackages[0].TargetTfm).IsEqualTo("net8.0");
            await Assert.That(config.ReferencePackages[0].PathPrefix).IsEqualTo("lib");
            await Assert.That(config.ReferencePackages[1].Id).IsEqualTo("Bar");
            await Assert.That(config.ReferencePackages[1].Version).IsNull();
            await Assert.That(config.ReferencePackages[1].TargetTfm).IsEqualTo(string.Empty);
            await Assert.That(config.ReferencePackages[1].PathPrefix).IsEqualTo("ref");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Each known display name in <c>excludePackages</c> /<c>excludePackagePrefixes</c> /etc populates.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadAllStringArrayPropertiesPopulate()
    {
        var path = WriteTemp("""
            {
              "nugetPackageOwners": ["alice"],
              "tfmPreference": ["net8.0"],
              "excludePackages": ["Bad"],
              "excludePackagePrefixes": ["Internal."]
            }
            """);
        try
        {
            var config = PackageConfigReader.Read(path);

            string[] expectedOwners = ["alice"];
            string[] expectedTfms = ["net8.0"];
            string[] expectedExcludes = ["Bad"];
            string[] expectedPrefixes = ["Internal."];
            await Assert.That(config.NugetPackageOwners).IsEquivalentTo(expectedOwners);
            await Assert.That(config.TfmPreference).IsEquivalentTo(expectedTfms);
            await Assert.That(config.ExcludePackages).IsEquivalentTo(expectedExcludes);
            await Assert.That(config.ExcludePackagePrefixes).IsEquivalentTo(expectedPrefixes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Writes <paramref name="json"/> to a temp file and returns its path.</summary>
    /// <param name="json">JSON text to write.</param>
    /// <returns>The path of the temp file.</returns>
    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdp-pkg-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
