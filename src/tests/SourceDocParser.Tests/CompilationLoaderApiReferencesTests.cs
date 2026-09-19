// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using SourceDocParser.LibCompilation;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Tests;

/// <summary>Requires only metadata used by a documentation root's public API.</summary>
public sealed class CompilationLoaderApiReferencesTests
{
    /// <summary>The assembly intentionally absent from the supplied reference set.</summary>
    private const string MissingAssembly = "MissingApiDependency";

    /// <summary>The missing type used by metadata fixtures.</summary>
    private const string MissingSource = "namespace Missing { public class Marker { } }";

    /// <summary>The package assembly supplied as a reference.</summary>
    private const string DependencyAssembly = "Dependency";

    /// <summary>Metadata-only inspection does not read or parse unrelated XML documentation.</summary>
    /// <param name="includeDocumentation">Whether XML documentation is requested.</param>
    /// <returns>A task representing the assertions.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task XmlDocumentationIsReadOnlyWhenRequested(bool includeDocumentation)
    {
        using var directory = new ScratchDirectory();
        var root = Emit(directory.Path, "Api", "public class Api { }");
        await File.WriteAllTextAsync(
            Path.ChangeExtension(root, ".xml"),
            "<doc><members><member name=\"T:Api\"><summary>Requested API documentation.</summary></member></members></doc>");
        var logger = new CaptureLogger();
        using var loader = new CompilationLoader(logger, includeDocumentation) { UseOnlySuppliedReferences = true };

        var (_, assembly) = loader.Load(root, RuntimeReferences());
        var documentation = assembly.GetTypeByMetadataName("Api")!.GetDocumentationCommentXml();

        await Assert.That(documentation?.Contains("Requested API documentation.", StringComparison.Ordinal) is true).IsEqualTo(includeDocumentation);
    }

    /// <summary>Private implementation metadata does not become a documentation dependency.</summary>
    /// <param name="source">The root source.</param>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    [Arguments("public class Api { private Missing.Marker _value; public int Value => 0; }")]
    [Arguments("public class Api { private Missing.Marker Method() => null; public int Value => 0; }")]
    public async Task PrivateImplementationReferencesDoNotWarn(string source)
    {
        using var directory = new ScratchDirectory();
        var missing = Emit(directory.Path, MissingAssembly, MissingSource);
        var root = Emit(directory.Path, "Api", source, missing);
        var logger = new CaptureLogger();
        using var loader = new CompilationLoader(logger) { UseOnlySuppliedReferences = true };

        _ = loader.Load(root, RuntimeReferences());

        await Assert.That(logger.Warnings).IsEmpty();
    }

    /// <summary>Unrelated dependency types and unused forwarded types do not extend the public API walk.</summary>
    /// <param name="dependencySource">The dependency source.</param>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    [Arguments("public class Needed { } public class Unrelated { public Missing.Marker Value; }")]
    [Arguments("[assembly:System.Runtime.CompilerServices.TypeForwardedTo(typeof(Missing.Marker))] public class Needed { }")]
    public async Task OnlyReferencedDependencyTypesAreRequired(string dependencySource)
    {
        using var directory = new ScratchDirectory();
        var missing = Emit(directory.Path, MissingAssembly, MissingSource);
        var dependency = Emit(directory.Path, DependencyAssembly, dependencySource, missing);
        var root = Emit(directory.Path, "Api", "public class Api { public Needed Value; }", dependency);
        var references = RuntimeReferences();
        references.Add(DependencyAssembly, dependency);
        var logger = new CaptureLogger();
        using var loader = new CompilationLoader(logger) { UseOnlySuppliedReferences = true };

        _ = loader.Load(root, references);

        await Assert.That(logger.Warnings).IsEmpty();
    }

    /// <summary>Missing types exposed by documented signatures remain actionable warnings.</summary>
    /// <param name="source">The public API source.</param>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    [Arguments("public class Api { public Missing.Marker Value; }")]
    [Arguments("public class Api { public Missing.Marker Method(Missing.Marker value) => value; }")]
    public async Task PublicApiReferencesStillWarn(string source)
    {
        using var directory = new ScratchDirectory();
        var missing = Emit(directory.Path, MissingAssembly, MissingSource);
        var root = Emit(directory.Path, "Api", source, missing);
        var logger = new CaptureLogger();
        using var loader = new CompilationLoader(logger) { UseOnlySuppliedReferences = true };

        _ = loader.Load(root, RuntimeReferences());

        var warning = await Assert.That(logger.Warnings).HasSingleItem(static message => message.Contains(MissingAssembly, StringComparison.Ordinal));
        await Assert.That(warning).Contains(root);
    }

    /// <summary>Inherited public members retain their required assembly references.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Test]
    public async Task InheritedPublicApiReferencesStillWarn()
    {
        using var directory = new ScratchDirectory();
        var missing = Emit(directory.Path, MissingAssembly, MissingSource);
        var dependency = Emit(directory.Path, DependencyAssembly, "public class Base { public Missing.Marker Value; }", missing);
        var root = Emit(directory.Path, "Api", "public class Api : Base { }", dependency);
        var references = RuntimeReferences();
        references.Add(DependencyAssembly, dependency);
        var logger = new CaptureLogger();
        using var loader = new CompilationLoader(logger) { UseOnlySuppliedReferences = true };

        _ = loader.Load(root, references);

        _ = await Assert.That(logger.Warnings).HasSingleItem(static message => message.Contains(MissingAssembly, StringComparison.Ordinal));
    }

    /// <summary>Supplies the explicitly selected core library for synthetic fixtures.</summary>
    /// <returns>The core-library reference map.</returns>
    private static Dictionary<string, string> RuntimeReferences()
    {
        Dictionary<string, string> references = [with(StringComparer.OrdinalIgnoreCase)];
        references.Add(typeof(object).Assembly.GetName().Name!, typeof(object).Assembly.Location);
        return references;
    }

    /// <summary>Emits a metadata fixture without executing the resulting assembly.</summary>
    /// <param name="directory">Fixture directory.</param>
    /// <param name="name">Assembly name.</param>
    /// <param name="source">Fixture source.</param>
    /// <param name="dependencies">Additional compile references.</param>
    /// <returns>The assembly file.</returns>
    /// <exception cref="InvalidOperationException">The fixture source cannot be compiled.</exception>
    private static string Emit(string directory, string name, string source, params string[] dependencies)
    {
        var references = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        foreach (var dependency in dependencies)
        {
            references.Add(MetadataReference.CreateFromFile(dependency));
        }

        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references, new(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(directory, $"{name}.dll");
        var result = compilation.Emit(path);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return path;
    }

    /// <summary>Collects reference warnings emitted by the loader.</summary>
    private sealed class CaptureLogger : ILogger
    {
        /// <summary>Gets captured warnings.</summary>
        public List<string> Warnings { get; } = [];

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
