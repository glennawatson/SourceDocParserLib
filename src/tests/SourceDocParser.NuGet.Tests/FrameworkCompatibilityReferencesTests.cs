// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.Packaging;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies .NET Framework compatibility through NuGet-only reference assets.</summary>
public sealed class FrameworkCompatibilityReferencesTests
{
    /// <summary>The compatibility contract required by .NET Standard dependencies.</summary>
    private const string StandardName = "netstandard";

    /// <summary>A .NET Framework target without an inbox .NET Standard 2.0 facade.</summary>
    private const string Framework = "net462";

    /// <summary>The selected .NET Framework core assembly.</summary>
    private const string CoreName = "mscorlib";

    /// <summary>The documented consumer assembly name.</summary>
    private const string ConsumerName = "Consumer";

    /// <summary>A required facade forwards System.Object to the selected .NET Framework core library.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RequiredFacadePreservesFrameworkTypeIdentityAndDocumentationRoots()
    {
        using var directory = new ScratchDirectory("framework-compatibility");
        using var session = new PackageRestoreSession(directory.Path, NullLogger.Instance);
        using var frameworkPackage = await session.DownloadAsync("Microsoft.NETFramework.ReferenceAssemblies.net462", "1.0.3", CancellationToken.None);
        var identity = frameworkPackage.PackageReader!.GetIdentity();
        var package = new VersionFolderPathResolver(session.PackagesPath).GetInstallPath(identity.Id, identity.Version);
        var core = Path.Combine(package, "build", ".NETFramework", "v4.6.2", "mscorlib.dll");
        var consumer = Path.Combine(directory.Path, "Consumer.dll");
        await File.WriteAllBytesAsync(consumer, CreateConsumer());
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CoreName] = core, [ConsumerName] = consumer };
        var group = new AssemblyGroup(Framework, [consumer], references);

        await FrameworkCompatibilityReferences.AddAsync(session, NuGetFramework.ParseFolder(Framework), references, CancellationToken.None);

        await Assert.That(references.ContainsKey(StandardName)).IsTrue();
        var facade = references[StandardName];
        await using (var stream = File.OpenRead(facade))
        using (var image = new PEReader(stream))
        {
            var metadata = image.GetMetadataReader();
            await Assert.That(metadata.TypeDefinitions.Count).IsEqualTo(1);
            await Assert.That(metadata.ExportedTypes.Count).IsGreaterThan(0);
        }

        var compilation = CSharpCompilation.Create(
            "FacadeBinding",
            references: [MetadataReference.CreateFromFile(core), MetadataReference.CreateFromFile(consumer), MetadataReference.CreateFromFile(facade)]);
        var type = compilation.GetTypeByMetadataName("Example.Consumer");
        await Assert.That(type!.BaseType!.TypeKind).IsEqualTo(TypeKind.Class);
        await Assert.That(type.BaseType.ContainingAssembly.Name).IsEqualTo(CoreName);
        await Assert.That(group.AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(group.AssemblyPaths[0]).IsEqualTo(consumer);
        await Assert.That(references[CoreName]).IsEqualTo(core);
    }

    /// <summary>Framework graphs without .NET Standard references do not acquire compatibility assets.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task UnneededCompatibilityAssetsAreNotAdded()
    {
        using var directory = new ScratchDirectory("framework-without-standard");
        using var session = new PackageRestoreSession(directory.Path, NullLogger.Instance);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await FrameworkCompatibilityReferences.AddAsync(session, NuGetFramework.ParseFolder(Framework), references, CancellationToken.None);

        await Assert.That(references).IsEmpty();
    }

    /// <summary>An explicitly supplied facade remains authoritative.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ExistingFacadeIsPreserved()
    {
        using var directory = new ScratchDirectory("framework-explicit-standard");
        using var session = new PackageRestoreSession(directory.Path, NullLogger.Instance);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [StandardName] = "explicit-facade.dll" };

        await FrameworkCompatibilityReferences.AddAsync(session, NuGetFramework.ParseFolder(Framework), references, CancellationToken.None);

        await Assert.That(references.Count).IsEqualTo(1);
        await Assert.That(references[StandardName]).IsEqualTo("explicit-facade.dll");
    }

    /// <summary>Builds a public type whose base class is scoped to the .NET Standard contract.</summary>
    /// <returns>The consumer assembly metadata.</returns>
    private static byte[] CreateConsumer()
    {
        var metadata = new MetadataBuilder();
        _ = metadata.AddModule(0, metadata.GetOrAddString("Consumer.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        _ = metadata.AddAssembly(metadata.GetOrAddString(ConsumerName), new(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        var token = metadata.GetOrAddBlob(Convert.FromHexString("CC7B13FFCD2DDD51"));
        var standard = metadata.AddAssemblyReference(metadata.GetOrAddString(StandardName), new(2, 0, 0, 0), default, token, 0, default);
        var systemObject = metadata.AddTypeReference(standard, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var firstField = MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        _ = metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default, firstField, firstMethod);
        _ = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Example"), metadata.GetOrAddString(ConsumerName), systemObject, firstField, firstMethod);
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new(metadata), new());
        var output = new BlobBuilder();
        _ = builder.Serialize(output);
        return output.ToArray();
    }
}
