// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SourceDocParser.LibCompilation;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.IntegrationTests;

/// <summary>Checks published Refit packages against documentation dependency resolution.</summary>
public sealed class RefitPackageRestoreTests
{
    /// <summary>The published package root and selected documentation target.</summary>
    private const string Manifest = """
        {
          "additionalPackages": [{ "id": "Refit", "version": "15.2.0" }],
          "excludePackagePrefixes": ["ReactiveUI."],
          "tfmPreference": ["net10.0"],
          "tfmOverrides": { "Refit": "net10.0" }
        }
        """;

    /// <summary>The public feed containing the published regression packages.</summary>
    private const string FeedConfiguration = """
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """;

    /// <summary>Excluded ReactiveUI dependencies remain available while only the pinned Refit assembly receives documentation.</summary>
    /// <param name="cancellationToken">Cancellation for package acquisition.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PinnedRefitRestoresExcludedDependenciesAndParsesWithoutMissingReferences(CancellationToken cancellationToken)
    {
        using var scratch = new ScratchDirectory("sdp-refit-restore");
        await File.WriteAllTextAsync(Path.Combine(scratch.Path, "nuget-packages.json"), Manifest, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(scratch.Path, "nuget.config"), FeedConfiguration, cancellationToken);
        using var source = new NuGetAssemblySource(scratch.Path, Path.Combine(scratch.Path, "api"));
        List<AssemblyGroup> groups = [];
        await foreach (var group in source.DiscoverAsync(cancellationToken))
        {
            groups.Add(group);
        }

        await Assert.That(groups.Count).IsEqualTo(1);
        var selected = groups[0];
        await Assert.That(selected.Tfm).IsEqualTo("net10.0");
        await Assert.That(selected.AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(selected.AssemblyPaths[0].Replace('\\', '/')).Contains("/refit/15.2.0/");
        var logger = new ReferenceWarningLogger();
        using var loader = new CompilationLoader(logger);
        var loaded = loader.Load(selected.AssemblyPaths[0], selected.FallbackIndex);
        HashSet<string> imported = [with(StringComparer.Ordinal)];
        foreach (var identity in loaded.Compilation.ReferencedAssemblyNames)
        {
            _ = imported.Add(identity.Name);
        }

        string[] dependencies = ["ReactiveUI.Primitives", "ReactiveUI.Primitives.Core", "ReactiveUI.Disposables"];
        foreach (var dependency in dependencies)
        {
            await Assert.That(selected.FallbackIndex.ContainsKey(dependency)).IsTrue();
            await Assert.That(selected.FallbackIndex[dependency].Replace('\\', '/')).Contains("/7.1.1/lib/net10.0/");
            await Assert.That(imported).Contains(dependency);
        }

        await Assert.That(loaded.Assembly.Name).IsEqualTo("Refit");
        await Assert.That(loaded.Assembly.GetTypeByMetadataName("Refit.ApiException")).IsNotNull();
        await Assert.That(logger.Warnings).IsEmpty();
    }

    /// <summary>Records missing-reference diagnostics while parsing the published assembly.</summary>
    private sealed class ReferenceWarningLogger : ILogger
    {
        /// <summary>Gets warnings emitted during API parsing.</summary>
        public List<string> Warnings { get; } = [];

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Warning;

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
    }
}
