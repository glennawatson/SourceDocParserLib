// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests;

/// <summary>Checks the metadata required to describe a root assembly's API.</summary>
public sealed class ApiReferenceInspectorTests
{
    /// <summary>The unavailable assembly used by the fixtures.</summary>
    private const string MissingAssembly = "MissingApiDependency";

    /// <summary>The referenced package assembly.</summary>
    private const string DependencyAssembly = "Dependency";

    /// <summary>Types used to exercise reference positions in API signatures.</summary>
    private const string MissingSource = """
        namespace Missing
        {
            public class Marker { }
            public interface IMarker { }
            public struct Value { }
            public sealed class MarkerAttribute : System.Attribute { }
        }
        """;

    /// <summary>Every documented type position can identify its missing assembly.</summary>
    /// <param name="source">The root assembly source.</param>
    /// <returns>A task representing the assertions.</returns>
    [Test]
    [Arguments("public class Api<T> where T : Missing.Marker { }")]
    [Arguments("public class Api { public void Method<T>() where T : Missing.IMarker { } }")]
    [Arguments("public class Api { public Missing.Marker[] Value { get; set; } }")]
    [Arguments("public class Api { public System.Action<Missing.Marker> Value; }")]
    [Arguments("public unsafe class Api { public Missing.Value* Value; }")]
    [Arguments("public unsafe class Api { public delegate*<Missing.Marker, void> Value; }")]
    [Arguments("[Missing.Marker] public class Api { }")]
    [Arguments("public class Api { [return: Missing.Marker] public int Method() => 0; }")]
    [Arguments("public class Api { public void Method([Missing.Marker] int value) { } }")]
    [Arguments("public class Api<[Missing.Marker] T> { }")]
    [Arguments("[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Missing.Marker))]")]
    [Arguments("""
        public sealed class LabelAttribute : System.Attribute
        {
            public LabelAttribute(System.Type type) { }
        }
        [Label(typeof(Missing.Marker))] public class Api { }
        """)]
    public async Task SignatureReferencesReportTheirMissingAssembly(string source)
    {
        var missing = Emit(MissingAssembly, MissingSource);
        var root = Emit("Api", source, missing);
        var assembly = Load(root);

        var result = ApiReferenceInspector.GetMissingReferences(assembly, false);

        var reference = await Assert.That(result).HasSingleItem();
        await Assert.That(reference.AssemblyIdentity.Name).IsEqualTo(MissingAssembly);
        await Assert.That(reference.Member).IsNotEmpty();
    }

    /// <summary>Members on a referenced value type are outside the root API surface.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task ExternalSignatureTypesDoNotExposeTheirMembers()
    {
        var missing = Emit(MissingAssembly, MissingSource);
        var dependency = Emit(DependencyAssembly, "public class External { public Missing.Marker Value; }", missing);
        var root = Emit("Api", "public class Api { public External Value; }", dependency);
        var assembly = Load(root, dependency);

        var result = ApiReferenceInspector.GetMissingReferences(assembly, false);

        await Assert.That(result).IsEmpty();
    }

    /// <summary>Constructors are excluded when only a base type's inherited members are needed.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task BaseConstructorsDoNotBecomeInheritedRequirements()
    {
        var missing = Emit(MissingAssembly, MissingSource);
        var dependency = Emit(DependencyAssembly, "public class Base { public Base() { } public Base(Missing.Marker value) { } }", missing);
        var root = Emit("Api", "public class Api : Base { }", dependency);
        var assembly = Load(root, dependency);

        var result = ApiReferenceInspector.GetMissingReferences(assembly, false);

        await Assert.That(result).IsEmpty();
    }

    /// <summary>Private declarations require their references only when explicitly requested.</summary>
    /// <returns>A task representing the assertions.</returns>
    [Test]
    public async Task PrivateReferencesHonorTheRequestedVisibility()
    {
        var missing = Emit(MissingAssembly, MissingSource);
        var root = Emit("Api", "public class Api { private Missing.Marker Value; }", missing);
        var assembly = Load(root);

        await Assert.That(ApiReferenceInspector.GetMissingReferences(assembly, false)).IsEmpty();
        var reference = await Assert.That(ApiReferenceInspector.GetMissingReferences(assembly, true)).HasSingleItem();
        await Assert.That(reference.AssemblyIdentity.Name).IsEqualTo(MissingAssembly);
    }

    /// <summary>Nonpublic enclosing types keep their visible members outside the root API.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task NonpublicContainingTypesAreExcluded()
    {
        var missing = Emit(MissingAssembly, MissingSource);
        var root = Emit("Api", "public class Api { private class Nested { public Missing.Marker Value; } }", missing);
        var assembly = Load(root);

        await Assert.That(ApiReferenceInspector.GetMissingReferences(assembly, false)).IsEmpty();
    }

    /// <summary>Nonpublic forwarded implementation members remain outside the root assembly.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task PrivateOptInDoesNotInspectForwardedImplementationMembers()
    {
        var missing = Emit(MissingAssembly, MissingSource);
        var dependency = Emit(DependencyAssembly, "public class External { private Missing.Marker Value; }", missing);
        var root = Emit("Api", "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(External))]", dependency);
        var assembly = Load(root, dependency);

        await Assert.That(ApiReferenceInspector.GetMissingReferences(assembly, true)).IsEmpty();
    }

    /// <summary>Null attribute arrays contain no referenced element types.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task NullAttributeArraysDoNotDereferenceMissingValues()
    {
        var root = Emit("Api", """
            public sealed class LabelAttribute : System.Attribute
            {
                public LabelAttribute(System.Type[] types) { }
            }
            [Label(null)] public class Api { }
            """);
        var assembly = Load(root);

        await Assert.That(ApiReferenceInspector.GetMissingReferences(assembly, false)).IsEmpty();
    }

    /// <summary>Recursive constraints and attribute constructors terminate without creating dependencies.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task RecursiveSignaturesAndAttributesTerminate()
    {
        var root = Emit("Api", """
            [Loop] public class Api<T> where T : Api<T> { }
            public sealed class LoopAttribute : System.Attribute
            {
                [Loop] public LoopAttribute() { }
            }
            """);
        var assembly = Load(root);

        await Assert.That(ApiReferenceInspector.GetMissingReferences(assembly, false)).IsEmpty();
    }

    /// <summary>Loads a root without the missing assembly used to compile it.</summary>
    /// <param name="root">The root assembly reference.</param>
    /// <param name="dependencies">The available dependency references.</param>
    /// <returns>The root assembly symbol.</returns>
    private static IAssemblySymbol Load(PortableExecutableReference root, params MetadataReference[] dependencies)
    {
        var references = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location), root };
        references.AddRange(dependencies);
        var compilation = CSharpCompilation.Create(
            "Inspection",
            references: references,
            options: new(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));
        return (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(root)!;
    }

    /// <summary>Creates metadata fixtures without executing their code.</summary>
    /// <param name="name">The assembly name.</param>
    /// <param name="source">The fixture source.</param>
    /// <param name="dependencies">Additional compilation references.</param>
    /// <returns>A metadata reference to the compiled assembly.</returns>
    /// <exception cref="InvalidOperationException">The fixture cannot be compiled.</exception>
    private static PortableExecutableReference Emit(string name, string source, params MetadataReference[] dependencies)
    {
        var references = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        references.AddRange(dependencies);
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
