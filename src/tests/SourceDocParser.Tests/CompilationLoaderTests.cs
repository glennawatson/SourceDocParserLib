// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="CompilationLoader"/> and <see cref="ICompilationLoader"/>
/// driven against the test runner's own assemblies (always present next to
/// the test binary).
/// </summary>
public class CompilationLoaderTests
{
    /// <summary>
    /// Gets the path to SourceDocParser.dll next to the test binary so we
    /// have a real assembly with transitive references to load.
    /// </summary>
    private static string SourceDocParserDllPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "SourceDocParser.dll");

    /// <summary>
    /// Loading a real DLL produces a non-null compilation and assembly symbol.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadProducesCompilationAndAssemblySymbol()
    {
        using var loader = new CompilationLoader();
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);

        var (compilation, assembly) = loader.Load(SourceDocParserDllPath, fallback);

        await Assert.That(compilation).IsNotNull();
        await Assert.That(assembly).IsNotNull();
        await Assert.That(assembly.Name).IsEqualTo("SourceDocParser");
    }

    /// <summary>
    /// The loader's compilation can resolve a known public type from the loaded assembly.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadedCompilationResolvesKnownType()
    {
        using var loader = new CompilationLoader();
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);

        var (compilation, _) = loader.Load(SourceDocParserDllPath, fallback);
        var type = compilation.GetTypeByMetadataName("SourceDocParser.MetadataExtractor");

        await Assert.That(type).IsNotNull();
        await Assert.That(type!.TypeKind).IsEqualTo(TypeKind.Class);
    }

    /// <summary>
    /// The cached metadata reference is reused across calls (the loader's
    /// internal cache is the public-perf invariant we rely on).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadReusesReferencesAcrossCalls()
    {
        using var loader = new CompilationLoader();
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);

        var (firstCompilation, _) = loader.Load(SourceDocParserDllPath, fallback);
        var (secondCompilation, _) = loader.Load(SourceDocParserDllPath, fallback);

        // References for the same path should be the same object reference because the cache returned them.
        var firstPrimary = firstCompilation.References.First(static r =>
            r is PortableExecutableReference per
            && string.Equals(per.FilePath, SourceDocParserDllPath, StringComparison.OrdinalIgnoreCase));
        var secondPrimary = secondCompilation.References.First(static r =>
            r is PortableExecutableReference per
            && string.Equals(per.FilePath, SourceDocParserDllPath, StringComparison.OrdinalIgnoreCase));

        await Assert.That(ReferenceEquals(firstPrimary, secondPrimary)).IsTrue();
    }

    /// <summary>
    /// Null/whitespace assembly path and null fallback both throw.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadValidatesArguments()
    {
        using var loader = new CompilationLoader();
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);

        await Assert.That(() => loader.Load(null!, fallback)).Throws<ArgumentException>();
        await Assert.That(() => loader.Load("   ", fallback)).Throws<ArgumentException>();
        await Assert.That(() => loader.Load(SourceDocParserDllPath, null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Calling <see cref="CompilationLoader.Dispose"/> twice does not throw.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeIsIdempotent()
    {
        var loader = new CompilationLoader();
        loader.Dispose();
        await Assert.That(loader.Dispose).ThrowsNothing();
    }
}
