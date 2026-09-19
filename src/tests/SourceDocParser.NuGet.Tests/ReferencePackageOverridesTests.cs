// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.ProjectModel;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Checks reference-pack precedence without executing package assemblies.</summary>
public sealed class ReferencePackageOverridesTests
{
    /// <summary>The package and assembly involved in reference conflicts.</summary>
    private const string Contract = "Contract";

    /// <summary>The separate assembly supplying framework core types.</summary>
    private const string Standard = "Standard";

    /// <summary>The package whose API is documented.</summary>
    private const string Root = "Root";

    /// <summary>The stable package version in synthetic graphs.</summary>
    private const string PackageVersion = "1.0.0";

    /// <summary>The selected package assembly fixture.</summary>
    private const string PackageFile = "package.dll";

    /// <summary>The framework assembly fixture.</summary>
    private const string FrameworkFile = "framework.dll";

    /// <summary>The lower assembly version.</summary>
    private const string FirstAssemblyVersion = "1.0.0.0";

    /// <summary>The higher assembly version.</summary>
    private const string SecondAssemblyVersion = "2.0.0.0";

    /// <summary>The exported-type flag identifying a type forwarder.</summary>
    private const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    /// <summary>Absent published overrides, a newer matching framework assembly wins.</summary>
    /// <param name="packageAssemblyVersion">The selected package's assembly version.</param>
    /// <param name="frameworkAssemblyVersion">The framework's assembly version.</param>
    /// <param name="frameworkWins">Whether the framework reference is preferred.</param>
    /// <returns>A task representing the assertions.</returns>
    [Test]
    [Arguments(FirstAssemblyVersion, SecondAssemblyVersion, true)]
    [Arguments(SecondAssemblyVersion, FirstAssemblyVersion, false)]
    [Arguments(SecondAssemblyVersion, SecondAssemblyVersion, false)]
    public async Task AssemblyVersionsResolveUnpublishedConflicts(string packageAssemblyVersion, string frameworkAssemblyVersion, bool frameworkWins)
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, packageAssemblyVersion, string.Empty, 0, []), false, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(Contract, frameworkAssemblyVersion, string.Empty, 0, []), false, null);

        var selected = Apply(package, framework, Root, PackageVersion, null);

        await Assert.That(selected).IsEqualTo(frameworkWins ? framework : package);
    }

    /// <summary>A matching filename cannot replace a different assembly identity.</summary>
    /// <param name="name">The framework assembly name.</param>
    /// <param name="culture">The framework assembly culture.</param>
    /// <param name="flags">The framework assembly identity flags.</param>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    [Arguments("DifferentContract", "", (AssemblyFlags)0)]
    [Arguments(Contract, "fr-FR", (AssemblyFlags)0)]
    [Arguments(Contract, "", AssemblyFlags.WindowsRuntime)]
    public async Task DifferentIdentitiesPreservePackageReference(string name, string culture, AssemblyFlags flags)
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, FirstAssemblyVersion, string.Empty, 0, []), false, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(name, SecondAssemblyVersion, culture, flags, []), false, null);

        await Assert.That(Apply(package, framework, Root, PackageVersion, null)).IsEqualTo(package);
    }

    /// <summary>Assembly public keys must match independently of filenames and versions.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task DifferentPublicKeysPreservePackageReference()
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, FirstAssemblyVersion, string.Empty, AssemblyFlags.PublicKey, [0]), false, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(Contract, SecondAssemblyVersion, string.Empty, AssemblyFlags.PublicKey, [1]), false, null);

        await Assert.That(Apply(package, framework, Root, PackageVersion, null)).IsEqualTo(package);
    }

    /// <summary>Nonidentity assembly flags do not prevent compatible framework upgrades.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task JitFlagsDoNotChangeAssemblyIdentity()
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, FirstAssemblyVersion, string.Empty, 0, []), false, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(Contract, SecondAssemblyVersion, string.Empty, AssemblyFlags.DisableJitCompileOptimizer, []), false, null);

        await Assert.That(Apply(package, framework, Root, PackageVersion, null)).IsEqualTo(framework);
    }

    /// <summary>Published package-version policy takes precedence over assembly-version comparisons.</summary>
    /// <param name="packageVersion">The resolved NuGet package version.</param>
    /// <param name="packageAssemblyVersion">The selected package's assembly version.</param>
    /// <param name="frameworkAssemblyVersion">The framework's assembly version.</param>
    /// <param name="frameworkWins">Whether the published override includes the package.</param>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    [Arguments("1.0.0", SecondAssemblyVersion, FirstAssemblyVersion, true)]
    [Arguments("2.0.0", FirstAssemblyVersion, SecondAssemblyVersion, false)]
    public async Task PublishedPolicyRemainsAuthoritative(string packageVersion, string packageAssemblyVersion, string frameworkAssemblyVersion, bool frameworkWins)
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, packageAssemblyVersion, string.Empty, 0, []), false, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(Contract, frameworkAssemblyVersion, string.Empty, 0, []), false, null);

        await Assert.That(Apply(package, framework, Root, packageVersion, PackageVersion)).IsEqualTo(frameworkWins ? framework : package);
    }

    /// <summary>Both override paths preserve the assembly selected as the documentation root.</summary>
    /// <param name="publishedOverride">Whether the pack publishes an applicable package override.</param>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DocumentationRootRemainsSelected(bool publishedOverride)
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, FirstAssemblyVersion, string.Empty, 0, []), false, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(Contract, SecondAssemblyVersion, string.Empty, 0, []), false, null);

        await Assert.That(Apply(package, framework, Contract, PackageVersion, publishedOverride ? PackageVersion : null)).IsEqualTo(package);
    }

    /// <summary>Replacing a legacy core contract with its framework facade restores Roslyn core binding.</summary>
    /// <returns>A task representing the assertions.</returns>
    [Test]
    public async Task FrameworkFacadePreventsCompetingCoreAssemblies()
    {
        using var directory = new ScratchDirectory();
        var package = WriteAssembly(directory.Path, PackageFile, new(Contract, FirstAssemblyVersion, string.Empty, 0, []), true, null);
        var standard = WriteAssembly(directory.Path, "standard.dll", new(Standard, FirstAssemblyVersion, string.Empty, 0, []), true, null);
        var framework = WriteAssembly(directory.Path, FrameworkFile, new(Contract, SecondAssemblyVersion, string.Empty, 0, []), false, Standard);
        var conflicting = CreateCompilation(package, standard);
        await Assert.That(conflicting.GetSpecialType(SpecialType.System_Object).TypeKind).IsEqualTo(TypeKind.Error);

        var selected = Apply(package, framework, Root, PackageVersion, null);
        var coherent = CreateCompilation(selected, standard);

        await Assert.That(coherent.GetSpecialType(SpecialType.System_Object).TypeKind).IsEqualTo(TypeKind.Class);
        await Assert.That(coherent.GetSpecialType(SpecialType.System_Object).ContainingAssembly.Name).IsEqualTo(Standard);
    }

    /// <summary>Applies one restored package's framework conflict policy.</summary>
    /// <param name="package">The selected package assembly.</param>
    /// <param name="framework">The framework reference assembly.</param>
    /// <param name="root">The documentation root package.</param>
    /// <param name="packageVersion">The selected NuGet version.</param>
    /// <param name="overrideVersion">The published replacement version, if any.</param>
    /// <returns>The selected reference path.</returns>
    private static string Apply(string package, string framework, string root, string packageVersion, string? overrideVersion)
    {
        var target = new LockFileTarget();
        target.Libraries.Add(new() { Name = Contract, Version = NuGetVersion.Parse(packageVersion), CompileTimeAssemblies = [new($"ref/netstandard2.0/{Contract}.dll")] });
        Dictionary<string, string> references = [with(StringComparer.OrdinalIgnoreCase)];
        references.Add(Contract, package);
        Dictionary<string, string> frameworkReferences = [with(StringComparer.OrdinalIgnoreCase)];
        frameworkReferences.Add(Contract, framework);
        Dictionary<string, NuGetVersion> overrides = [with(StringComparer.OrdinalIgnoreCase)];
        if (overrideVersion is not null)
        {
            overrides.Add(Contract, NuGetVersion.Parse(overrideVersion));
        }

        ReferencePackageOverrides.Apply(target, root, frameworkReferences, overrides, references);
        return references[Contract];
    }

    /// <summary>Creates a compiler reference set without executing assembly code.</summary>
    /// <param name="contract">The chosen contract or facade.</param>
    /// <param name="standard">The framework core assembly.</param>
    /// <returns>The compilation used to check core-type binding.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CSharpCompilation CreateCompilation(string contract, string standard) => CSharpCompilation.Create(
        "BindingProbe",
        references: [MetadataReference.CreateFromFile(contract), MetadataReference.CreateFromFile(standard)],
        options: new(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Writes a managed metadata fixture with an explicit assembly identity.</summary>
    /// <param name="directory">The fixture directory.</param>
    /// <param name="file">The fixture filename.</param>
    /// <param name="identity">The assembly identity to encode.</param>
    /// <param name="declaresObject">Whether to define a core object type.</param>
    /// <param name="forwardTarget">The assembly receiving an object type forwarder, if any.</param>
    /// <returns>The fixture path.</returns>
    private static string WriteAssembly(
        string directory,
        string file,
        in FixtureIdentity identity,
        bool declaresObject,
        string? forwardTarget)
    {
        var metadata = new MetadataBuilder();
        _ = metadata.AddModule(0, metadata.GetOrAddString(file), default, default, default);
        _ = metadata.AddAssembly(
            metadata.GetOrAddString(identity.Name),
            Version.Parse(identity.Version),
            metadata.GetOrAddString(identity.Culture),
            metadata.GetOrAddBlob(identity.PublicKey),
            identity.Flags,
            AssemblyHashAlgorithm.None);
        _ = metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (declaresObject)
        {
            _ = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        if (forwardTarget is not null)
        {
            var target = metadata.AddAssemblyReference(metadata.GetOrAddString(forwardTarget), new(1, 0, 0, 0), default, default, 0, default);
            _ = metadata.AddExportedType(TypeAttributes.Public | Forwarder, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"), target, 0);
        }

        var image = new ManagedPEBuilder(new(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), new(metadata), new());
        var blob = new BlobBuilder();
        _ = image.Serialize(blob);
        var path = Path.Combine(directory, file);
        using var stream = File.Create(path);
        blob.WriteContentTo(stream);
        return path;
    }

    /// <summary>Defines the identity encoded in a synthetic assembly.</summary>
    /// <param name="Name">The assembly name.</param>
    /// <param name="Version">The assembly version.</param>
    /// <param name="Culture">The assembly culture.</param>
    /// <param name="Flags">The assembly identity and execution flags.</param>
    /// <param name="PublicKey">The public-key blob.</param>
    [DebuggerDisplay("{Name,nq}: {Version,nq}")]
    private readonly record struct FixtureIdentity(string Name, string Version, string Culture, AssemblyFlags Flags, byte[] PublicKey);
}
