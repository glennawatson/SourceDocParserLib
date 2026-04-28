// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="LoaderRegistry"/>: tracks registered
/// <see cref="ICompilationLoader"/> instances and disposes each one
/// in the order they were registered when the registry itself is
/// disposed. <see cref="LoaderRegistry.Track"/> returns the same
/// loader for fluent assignment.
/// </summary>
public class LoaderRegistryTests
{
    /// <summary>Track returns the same instance it was handed (fluent assignment).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TrackReturnsSameInstance()
    {
        using var registry = new LoaderRegistry();
        var loader = new RecordingLoader();

        var result = registry.Track(loader);

        await Assert.That(result).IsSameReferenceAs(loader);
    }

    /// <summary>Disposing the registry disposes every tracked loader exactly once.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeDisposesAllTrackedLoaders()
    {
        var first = new RecordingLoader();
        var second = new RecordingLoader();
        var registry = new LoaderRegistry();

        registry.Track(first);
        registry.Track(second);
        registry.Dispose();

        await Assert.That(first.DisposeCount).IsEqualTo(1);
        await Assert.That(second.DisposeCount).IsEqualTo(1);
    }

    /// <summary>Disposing twice only flushes loaders once -- second call is a no-op because the registry clears its list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeIsIdempotentForRegistry()
    {
        var loader = new RecordingLoader();
        var registry = new LoaderRegistry();
        registry.Track(loader);

        registry.Dispose();
        registry.Dispose();

        await Assert.That(loader.DisposeCount).IsEqualTo(1);
    }

    /// <summary>Disposing an empty registry is a no-op (does not throw).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeNoOpForEmptyRegistry()
    {
        var registry = new LoaderRegistry();
        await Assert.That(registry.Dispose).ThrowsNothing();
    }

    /// <summary>Counts dispose invocations and returns a synthetic compilation on Load.</summary>
    private sealed class RecordingLoader : ICompilationLoader
    {
        /// <summary>Gets the number of times <see cref="Dispose"/> has been called.</summary>
        public int DisposeCount { get; private set; }

        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(string assemblyPath, Dictionary<string, string> fallbackReferences) =>
            Load(assemblyPath, fallbackReferences, includePrivateMembers: false);

        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(string assemblyPath, Dictionary<string, string> fallbackReferences, bool includePrivateMembers)
        {
            var compilation = CSharpCompilation.Create("Probe");
            return (compilation, compilation.Assembly);
        }

        /// <inheritdoc />
        public void Dispose() => DisposeCount++;
    }
}
