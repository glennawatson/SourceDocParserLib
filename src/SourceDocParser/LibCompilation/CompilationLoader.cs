// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using static Microsoft.CodeAnalysis.MetadataImportOptions;
using static Microsoft.CodeAnalysis.OutputKind;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Loads a compiled .NET assembly into a Roslyn <see cref="CSharpCompilation"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses ICSharpCode.Decompiler's <c>UniversalAssemblyResolver</c> for transitive
/// assembly reference resolution and attaches XML documentation via a custom
/// provider. Each instance owns a <see cref="MetadataReferenceCache"/> so the
/// BCL ref pack and shared transitive references are only loaded once across a
/// batch of assemblies in the same TFM group.
/// </para>
/// <para>
/// <b>Thread Safety:</b> <see cref="Load"/> is safe to call concurrently — the
/// underlying cache uses a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
/// <see cref="Dispose"/> must not race with concurrent <see cref="Load"/> calls.
/// </para>
/// </remarks>
public sealed partial class CompilationLoader : ICompilationLoader
{
    /// <summary>
    /// Gets a bootstrap syntax tree included in every compilation.
    /// </summary>
    /// <remarks>
    /// Ensures Roslyn has a primary source to anchor the assembly identity,
    /// allowing it to bind core types like <c>System.Object</c>.
    /// </remarks>
    private static readonly SyntaxTree[] _bootstrap =
    [
        CSharpSyntaxTree.ParseText(
            """
            class Bootstrap
            {
                public static void Main(string[] args) { }
            }
            """),
    ];

    /// <summary>Logger for resolver progress and reference-resolution warnings.</summary>
    private readonly ILogger _logger;

    /// <summary>Cache of <see cref="MetadataReference"/> instances by absolute path. Owned by this loader.</summary>
    private readonly MetadataReferenceCache _referenceCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompilationLoader"/> class.
    /// </summary>
    /// <param name="logger">Logger for resolver progress and reference-resolution warnings; <see cref="NullLogger.Instance"/> when null.</param>
    public CompilationLoader(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _referenceCache = new(_logger);
    }

    /// <inheritdoc />
    public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
        string assemblyPath,
        Dictionary<string, string> fallbackReferences,
        bool includePrivateMembers = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(fallbackReferences);

        var resolved = ResolveTransitiveReferences(assemblyPath, fallbackReferences, _logger);

        var references = new List<MetadataReference>(resolved.Count + 1);
        for (var i = 0; i < resolved.Count; i++)
        {
            references.Add(_referenceCache.Get(resolved[i]));
        }

        var primary = _referenceCache.Get(assemblyPath);
        references.Add(primary);

        var compilation = CSharpCompilation.Create(
            assemblyName: null,
            syntaxTrees: _bootstrap,
            references: references,
            options: new(
                outputKind: DynamicallyLinkedLibrary,
                metadataImportOptions: includePrivateMembers ? All : Public));

        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(primary)!;
        return (compilation, assembly);
    }

    /// <inheritdoc />
    public void Dispose() => _referenceCache.Dispose();

    /// <summary>
    /// Resolves the closure of assembly references for a DLL.
    /// </summary>
    /// <param name="assemblyPath">The absolute path to the primary DLL.</param>
    /// <param name="fallbackIndex">The fallback map for resolver misses.</param>
    /// <param name="logger">Logger for resolver progress and unresolved-reference warnings.</param>
    /// <returns>A list of absolute paths to resolved transitive references.</returns>
    /// <remarks>
    /// Uses an iterative stack-based walk to avoid recursion overhead. PEFile
    /// instances are disposed immediately after processing. Skips 0.0.0.0
    /// version references as they typically represent compiler stubs.
    /// </remarks>
    private static List<string> ResolveTransitiveReferences(
        string assemblyPath,
        Dictionary<string, string> fallbackIndex,
        ILogger logger)
    {
        using var primary = new PEFile(assemblyPath);
        var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, primary.DetectTargetFrameworkId());
        var resolvedNames = new HashSet<string>(StringComparer.Ordinal);
        List<string> resolvedPaths = [];
        var assemblyName = Path.GetFileName(assemblyPath);

        var pending = new Stack<PEFile>();
        pending.Push(new(assemblyPath));

        try
        {
            while (pending.TryPop(out var current))
            {
                try
                {
                    foreach (var reference in current.AssemblyReferences)
                    {
                        if (reference.Version is { Major: 0, Minor: 0, Build: 0, Revision: 0 })
                        {
                            continue;
                        }

                        var file = resolver.FindAssemblyFile(reference);
                        if (file is null && !fallbackIndex.TryGetValue(reference.Name, out file))
                        {
                            LogUnresolvedReference(logger, reference.ToString(), assemblyName);
                            continue;
                        }

                        if (!resolvedNames.Add(reference.Name))
                        {
                            continue;
                        }

                        LogResolvedReference(logger, reference.Name, file);
                        resolvedPaths.Add(file);
                        pending.Push(new(file));
                    }
                }
                finally
                {
                    current.Dispose();
                }
            }
        }
        catch
        {
            while (pending.TryPop(out var p))
            {
                p.Dispose();
            }

            throw;
        }

        return resolvedPaths;
    }

    /// <summary>Logs an assembly reference the resolver and fallback index could not locate.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Reference identity (name + version).</param>
    /// <param name="assembly">Filename of the assembly that requested it.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to resolve assembly reference '{Reference}' for {Assembly}")]
    private static partial void LogUnresolvedReference(ILogger logger, string reference, string assembly);

    /// <summary>Logs a successful assembly reference resolution.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Resolved assembly simple name.</param>
    /// <param name="file">Absolute path the reference resolved to.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "  resolved {Reference} -> {File}")]
    private static partial void LogResolvedReference(ILogger logger, string reference, string file);
}
