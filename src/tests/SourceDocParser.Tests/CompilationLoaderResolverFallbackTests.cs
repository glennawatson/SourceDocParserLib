// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins the fallback-by-name behaviour inside
/// <see cref="CompilationLoader.ResolveTransitiveReferences(string, Dictionary{string, string}, ILogger)"/>.
///
/// The downstream complaint we're chasing is the long tail of
/// <c>Unable to resolve assembly reference 'Splat, Version=15.3.0.0'</c>
/// warnings on real-world ReactiveUI / Akavache / CrissCross walks.
/// The resolver's contract is:
///
/// <list type="number">
///   <item><description>Try ICSharpCode's <c>UniversalAssemblyResolver</c> first.</description></item>
///   <item><description>If that returns null, fall through to the user-supplied
///     fallback dictionary keyed on the simple assembly name (so a
///     newer-version DLL still satisfies the older-version reference).</description></item>
///   <item><description>If neither path finds the file, log an unresolved warning --
///     unless the reference matches the platform/SDK/stub filter,
///     in which case the skip stays silent.</description></item>
/// </list>
///
/// Each scenario is built from synthetic Roslyn-emitted DLLs so the
/// test stays self-contained and deterministic.
/// </summary>
public class CompilationLoaderResolverFallbackTests
{
    /// <summary>
    /// Standalone-DLL scenario: the primary lives in a directory
    /// that does NOT carry its dependency. The standard resolver's
    /// directory probe finds nothing. The fallback dictionary
    /// contains the dep by simple name -- the resolver must pick it
    /// up from there.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveTransitiveReferencesFindsDepViaFallbackWhenPrimaryDirIsIsolated()
    {
        using var temp = new TempDirectory();
        var depDir = MakeSubDir(temp.Path, "deps");
        var depPath = EmitSyntheticDependency("FakeSplat", depDir);

        var primaryDir = MakeSubDir(temp.Path, "primary");
        var primaryPath = EmitPrimaryReferencingDependency("Primary", depPath, primaryDir);

        var fallback = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FakeSplat"] = depPath,
        };

        var resolved = CompilationLoader.ResolveTransitiveReferences(primaryPath, fallback, NullLogger.Instance);

        await Assert.That(resolved).Contains(depPath);
    }

    /// <summary>
    /// Version-mismatch scenario (the actual bug surface): the
    /// reference embedded in the primary's metadata says
    /// <c>FakeSplat, Version=15.3.0.0</c>, but only
    /// <c>FakeSplat, Version=19.0.0.0</c> exists on disk. The fallback
    /// is keyed by NAME only, so the newer DLL must still satisfy the
    /// older-versioned reference.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveTransitiveReferencesIgnoresVersionMismatchOnFallbackHit()
    {
        using var temp = new TempDirectory();

        // The dep we emit at compile time carries the original version
        // so the primary's metadata records the same major.minor in
        // its AssemblyReference. We drop it after compiling so the
        // standard resolver can't find it.
        var compileTimeDir = MakeSubDir(temp.Path, "ct");
        var compileTimeDep = EmitSyntheticDependency("FakeSplat", compileTimeDir, version: "15.3.0.0");
        var primaryDir = MakeSubDir(temp.Path, "primary");
        var primaryPath = EmitPrimaryReferencingDependency("Primary", compileTimeDep, primaryDir);

        // Now emit a NEWER version of FakeSplat in a separate dir and
        // discard the compile-time version. The resolver only sees
        // the newer one, by name, via fallback.
        var fallbackDir = MakeSubDir(temp.Path, "fallback");
        var newerDep = EmitSyntheticDependency("FakeSplat", fallbackDir, version: "19.0.0.0");
        File.Delete(compileTimeDep);

        var fallback = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FakeSplat"] = newerDep,
        };

        var resolved = CompilationLoader.ResolveTransitiveReferences(primaryPath, fallback, NullLogger.Instance);

        await Assert.That(resolved).Contains(newerDep);
    }

    /// <summary>
    /// Genuine-miss scenario: the primary references something that
    /// exists in neither resolver path. The unresolved-reference
    /// warning fires so callers can see the gap.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveTransitiveReferencesLogsWarningForGenuinelyUnresolvedRef()
    {
        using var temp = new TempDirectory();
        var depDir = MakeSubDir(temp.Path, "deps");
        var depPath = EmitSyntheticDependency("UnresolvableTestDep", depDir, version: "9.9.9.9");

        var primaryDir = MakeSubDir(temp.Path, "primary");
        var primaryPath = EmitPrimaryReferencingDependency("Primary", depPath, primaryDir);

        // Drop the dep from disk and provide nothing in the fallback.
        File.Delete(depPath);

        var spy = new RecordingLogger();
        var resolved = CompilationLoader.ResolveTransitiveReferences(primaryPath, [], spy);

        await Assert.That(resolved).DoesNotContain(depPath);
        await Assert.That(spy.HasWarningContaining("UnresolvableTestDep"))
            .IsTrue()
            .Because($"expected an unresolved-reference warning for the missing dep, got [{string.Join(" | ", spy.Warnings)}]");
    }

    /// <summary>
    /// Platform-filter scenario: a reference whose simple name lives
    /// in <see cref="UnresolvableReferenceFilter"/>'s exact-match set
    /// (e.g. <c>Java.Interop</c>) is silently skipped instead of
    /// logged. Otherwise every Android-workload walk drowns in
    /// thousands of identical warnings.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveTransitiveReferencesSuppressesWarningForFilteredPlatformRef()
    {
        using var temp = new TempDirectory();

        // We emit Java.Interop ourselves so the primary has a real
        // metadata reference to it, then drop it from disk so the
        // resolver path returns null. The filter must keep the warning
        // off the logger regardless.
        var compileDir = MakeSubDir(temp.Path, "ct");
        var compileDep = EmitSyntheticDependency("Java.Interop", compileDir);
        var primaryDir = MakeSubDir(temp.Path, "primary");
        var primaryPath = EmitPrimaryReferencingDependency("Primary", compileDep, primaryDir);
        File.Delete(compileDep);

        var spy = new RecordingLogger();
        CompilationLoader.ResolveTransitiveReferences(primaryPath, [], spy);

        await Assert.That(spy.HasWarningContaining("Java.Interop"))
            .IsFalse()
            .Because($"filter should have suppressed the warning, got [{string.Join(" | ", spy.Warnings)}]");
    }

    /// <summary>
    /// Repeated-reference scenario: when a transitive ref appears
    /// multiple times across the dependency closure (the same dep
    /// referenced by two siblings, or a cyclic ref), the resolver
    /// records it exactly once.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveTransitiveReferencesDedupesByName()
    {
        using var temp = new TempDirectory();
        var depDir = MakeSubDir(temp.Path, "deps");
        var depPath = EmitSyntheticDependency("SharedLib", depDir);

        // Each intermediary carries its own Marker type AND a reference
        // to SharedLib (through Marker's type). The primary references
        // both intermediaries. SharedLib therefore appears twice in
        // the dependency BFS -- once via MidA, once via MidB -- and
        // the resolver must record it exactly once.
        var midDir = MakeSubDir(temp.Path, "mid");
        var midA = EmitDependencyReferencing("MidA", depPath, midDir);
        var midB = EmitDependencyReferencing("MidB", depPath, midDir);
        var primaryDir = MakeSubDir(temp.Path, "primary");
        var primaryPath = EmitPrimaryReferencingMultiple("Primary", [midA, midB], primaryDir);

        var fallback = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SharedLib"] = depPath,
            ["MidA"] = midA,
            ["MidB"] = midB,
        };

        var resolved = CompilationLoader.ResolveTransitiveReferences(primaryPath, fallback, NullLogger.Instance);

        var sharedHits = 0;
        for (var i = 0; i < resolved.Count; i++)
        {
            if (string.Equals(resolved[i], depPath, StringComparison.Ordinal))
            {
                sharedHits++;
            }
        }

        await Assert.That(sharedHits).IsEqualTo(1);
    }

    /// <summary>
    /// Builds <paramref name="parent"/>/<paramref name="name"/> as a
    /// fresh subdirectory.
    /// </summary>
    /// <param name="parent">Parent directory.</param>
    /// <param name="name">Subdirectory name.</param>
    /// <returns>The created path.</returns>
    private static string MakeSubDir(string parent, string name)
    {
        var dir = Path.Combine(parent, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Compiles a tiny library carrying a single <c>Marker</c> type
    /// to <paramref name="outputDir"/>, with the supplied
    /// <paramref name="version"/> stamped into its assembly metadata.
    /// </summary>
    /// <param name="assemblyName">Simple assembly name -- becomes the on-disk filename and the metadata identity.</param>
    /// <param name="outputDir">Where to emit the resulting <c>.dll</c>.</param>
    /// <param name="version">Assembly version stamped into the manifest. Defaults to <c>1.0.0.0</c>.</param>
    /// <returns>The absolute path of the emitted DLL.</returns>
    private static string EmitSyntheticDependency(string assemblyName, string outputDir, string version = "1.0.0.0")
    {
        var ns = SafeNamespace(assemblyName);
        var source = $$"""
            using System.Reflection;
            [assembly: AssemblyVersion("{{version}}")]
            namespace {{ns}};
            public class Marker { }
            """;

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            BclReferences(),
            new(OutputKind.DynamicallyLinkedLibrary));

        return EmitToFile(compilation, outputDir, assemblyName);
    }

    /// <summary>
    /// Emits a dependency that has its own <c>Marker</c> type AND
    /// references another dependency's <c>Marker</c> type, so the
    /// resulting PE file's <c>AssemblyReferences</c> list carries
    /// the upstream dep by name. Used by the dedup scenario where
    /// two siblings reference the same downstream library.
    /// </summary>
    /// <param name="assemblyName">Simple assembly name -- becomes the on-disk filename.</param>
    /// <param name="upstreamDependencyPath">Absolute path of the dep this assembly references.</param>
    /// <param name="outputDir">Output dir.</param>
    /// <returns>The absolute path of the emitted DLL.</returns>
    private static string EmitDependencyReferencing(string assemblyName, string upstreamDependencyPath, string outputDir)
    {
        var ns = SafeNamespace(assemblyName);
        var upstreamName = Path.GetFileNameWithoutExtension(upstreamDependencyPath);
        var upstreamNamespace = SafeNamespace(upstreamName);
        var source = $$"""
            namespace {{ns}};
            public class Marker
            {
                public {{upstreamNamespace}}.Marker Upstream;
            }
            """;

        var references = new List<MetadataReference>(BclReferences())
        {
            MetadataReference.CreateFromFile(upstreamDependencyPath),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));

        return EmitToFile(compilation, outputDir, assemblyName);
    }

    /// <summary>
    /// Compiles a primary that consumes the <c>Marker</c> type from
    /// <paramref name="dependencyPath"/>, so the emitted PE file's
    /// <c>AssemblyReferences</c> list carries the dependency by name
    /// and version.
    /// </summary>
    /// <param name="primaryName">Simple primary name.</param>
    /// <param name="dependencyPath">Absolute path to the dependency DLL.</param>
    /// <param name="outputDir">Output dir.</param>
    /// <returns>The absolute path of the emitted primary DLL.</returns>
    private static string EmitPrimaryReferencingDependency(string primaryName, string dependencyPath, string outputDir) =>
        EmitPrimaryReferencingMultiple(primaryName, [dependencyPath], outputDir);

    /// <summary>
    /// Multi-dep variant of <see cref="EmitPrimaryReferencingDependency"/>.
    /// Each input DLL is added as a metadata reference and the source
    /// references one type from each so the bindings can't be culled.
    /// </summary>
    /// <param name="primaryName">Simple primary name.</param>
    /// <param name="dependencyPaths">Absolute paths to each dep DLL.</param>
    /// <param name="outputDir">Output dir.</param>
    /// <returns>The absolute path of the emitted primary DLL.</returns>
    private static string EmitPrimaryReferencingMultiple(string primaryName, string[] dependencyPaths, string outputDir)
    {
        var references = new List<MetadataReference>(BclReferences());
        var consumeLines = new List<string>(dependencyPaths.Length);
        for (var i = 0; i < dependencyPaths.Length; i++)
        {
            references.Add(MetadataReference.CreateFromFile(dependencyPaths[i]));
            var depAssemblyName = Path.GetFileNameWithoutExtension(dependencyPaths[i]);
            var depNamespace = SafeNamespace(depAssemblyName);
            var index = i.ToString(CultureInfo.InvariantCulture);
            consumeLines.Add($"        public {depNamespace}.Marker M{index};");
        }

        var consumeBody = string.Join("\n", consumeLines);
        var source = $$"""
            namespace Test.Primary;
            public class P
            {
            {{consumeBody}}
            }
            """;

        var compilation = CSharpCompilation.Create(
            primaryName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));

        return EmitToFile(compilation, outputDir, primaryName);
    }

    /// <summary>Materialises <paramref name="compilation"/> as <paramref name="outputDir"/>/<paramref name="name"/>.dll.</summary>
    /// <param name="compilation">The compilation to emit.</param>
    /// <param name="outputDir">Destination directory.</param>
    /// <param name="name">File-name stem (without extension).</param>
    /// <returns>The absolute path of the emitted DLL.</returns>
    private static string EmitToFile(CSharpCompilation compilation, string outputDir, string name)
    {
        var path = Path.Combine(outputDir, name + ".dll");
        var emit = compilation.Emit(path);
        if (!emit.Success)
        {
            throw new InvalidOperationException($"Compile failed for {name}: " + string.Join('\n', emit.Diagnostics));
        }

        return path;
    }

    /// <summary>Replaces dots with underscores so the assembly name doubles as a valid namespace identifier.</summary>
    /// <param name="assemblyName">Source assembly name.</param>
    /// <returns>Identifier-safe namespace.</returns>
    private static string SafeNamespace(string assemblyName) => assemblyName.Replace('.', '_');

    /// <summary>BCL references picked up from the live test runner's loaded assemblies.</summary>
    /// <returns>The reference set every synthetic compilation needs to compile.</returns>
    private static IEnumerable<MetadataReference> BclReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));

    /// <summary>
    /// Test-only logger that records every warning message so the
    /// test can assert on whether a specific reference name appeared.
    /// Used by the filter-suppression and genuine-miss scenarios.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        /// <summary>Gets the captured warning messages, in arrival order.</summary>
        public List<string> Warnings { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (logLevel is not LogLevel.Warning)
            {
                return;
            }

            Warnings.Add(formatter(state, exception));
        }

        /// <summary>Returns true when any captured warning contains <paramref name="needle"/>.</summary>
        /// <param name="needle">Substring to search for.</param>
        /// <returns>True on first match.</returns>
        public bool HasWarningContaining(string needle)
        {
            for (var i = 0; i < Warnings.Count; i++)
            {
                if (Warnings[i].Contains(needle, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
