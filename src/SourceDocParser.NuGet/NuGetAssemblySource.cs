// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace SourceDocParser.NuGet;

/// <summary>
/// <see cref="IAssemblySource"/> that fetches NuGet packages described
/// by <c>nuget-packages.json</c> into <c>apiPath/lib</c> + <c>apiPath/refs</c>
/// and exposes the extracted assemblies, grouped by TFM, to the parser.
/// </summary>
public sealed class NuGetAssemblySource : IAssemblySource
{
    /// <summary>File pattern used to discover assemblies.</summary>
    private const string DllPattern = "*.dll";

    /// <summary>Sub-directory under <c>apiPath</c> holding extracted package lib/ trees.</summary>
    private const string LibDirName = "lib";

    /// <summary>Sub-directory under <c>apiPath</c> holding extracted reference assemblies.</summary>
    private const string RefsDirName = "refs";

    /// <summary>Repository root containing <c>nuget-packages.json</c>.</summary>
    private readonly string _rootDirectory;

    /// <summary>Destination root for fetched and extracted package assemblies.</summary>
    private readonly string _apiPath;

    /// <summary>Logger forwarded to the fetcher and used for discovery progress.</summary>
    private readonly ILogger _logger;

    /// <summary>Fetcher that materialises the lib/ + refs/ trees this source then walks.</summary>
    private readonly INuGetFetcher _fetcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetAssemblySource"/> class.
    /// </summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination root for fetched and extracted package assemblies (typically <c>reactiveui/api</c>).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="fetcher">Optional fetcher; defaults to a fresh <see cref="NuGetFetcher"/>. Inject for tests that want to skip the network.</param>
    /// <exception cref="ArgumentException">When <paramref name="rootDirectory"/> or <paramref name="apiPath"/> is null, empty, or whitespace.</exception>
    public NuGetAssemblySource(string rootDirectory, string apiPath, ILogger? logger = null, INuGetFetcher? fetcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        _rootDirectory = rootDirectory;
        _apiPath = apiPath;
        _logger = logger ?? NullLogger.Instance;
        _fetcher = fetcher ?? new NuGetFetcher();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _fetcher.FetchPackagesAsync(_rootDirectory, _apiPath, _logger, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var libDir = Path.Combine(_apiPath, LibDirName);
        var refsDir = Path.Combine(_apiPath, RefsDirName);

        if (!Directory.Exists(libDir))
        {
            throw new DirectoryNotFoundException($"No {LibDirName}/ directory found at {libDir}");
        }

        var libTfms = DiscoverTfms(libDir);
        var refsTfms = Directory.Exists(refsDir) ? DiscoverTfms(refsDir) : [];

        foreach (var tfm in libTfms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var libTfmDir = Path.Combine(libDir, tfm);
            var bestRef = TfmResolver.FindBestRefsTfm(tfm, refsTfms);

            var refDllNames = bestRef is not null
                ? CollectFileNamesWithoutExtension(Path.Combine(refsDir, bestRef), DllPattern)
                : new(StringComparer.OrdinalIgnoreCase);

            var packageDlls = CollectPackageDlls(libTfmDir, refDllNames);
            if (packageDlls.Count == 0)
            {
                continue;
            }

            var fallbackIndex = bestRef is not null
                ? AssemblyResolution.BuildFallbackIndex([Path.Combine(refsDir, bestRef), libTfmDir], _logger)
                : AssemblyResolution.BuildFallbackIndex([libTfmDir], _logger);

            yield return new(tfm, packageDlls, fallbackIndex);
        }
    }

    /// <summary>
    /// Returns the names of every immediate sub-directory of <paramref name="root"/> that contains at least one DLL.
    /// </summary>
    /// <param name="root">Directory to enumerate.</param>
    /// <returns>Sorted list of TFM directory names.</returns>
    private static List<string> DiscoverTfms(string root)
    {
        var tfms = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (HasAtLeastOneFile(dir, DllPattern))
            {
                tfms.Add(Path.GetFileName(dir.AsSpan()).ToString());
            }
        }

        tfms.Sort(StringComparer.Ordinal);
        return tfms;
    }

    /// <summary>
    /// Returns the package DLLs in <paramref name="libTfmDir"/>, excluding any whose name matches a co-located reference assembly.
    /// </summary>
    /// <param name="libTfmDir">Per-TFM lib directory.</param>
    /// <param name="refDllNames">Filename-without-extension names of reference assemblies to exclude.</param>
    /// <returns>Sorted list of absolute DLL paths.</returns>
    private static List<string> CollectPackageDlls(string libTfmDir, HashSet<string> refDllNames)
    {
        var packageDlls = new List<string>();

        foreach (var dll in Directory.EnumerateFiles(libTfmDir, DllPattern))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(dll.AsSpan()).ToString();
            if (!refDllNames.Contains(nameWithoutExt))
            {
                packageDlls.Add(dll);
            }
        }

        packageDlls.Sort(StringComparer.Ordinal);
        return packageDlls;
    }

    /// <summary>
    /// Returns the filenames-without-extension of every file matching <paramref name="pattern"/> in <paramref name="dir"/>.
    /// </summary>
    /// <param name="dir">Directory to scan (must exist).</param>
    /// <param name="pattern">File pattern.</param>
    /// <returns>OrdinalIgnoreCase set of filenames without extensions.</returns>
    private static HashSet<string> CollectFileNamesWithoutExtension(string dir, string pattern)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(dir, pattern))
        {
            names.Add(Path.GetFileNameWithoutExtension(file.AsSpan()).ToString());
        }

        return names;
    }

    /// <summary>
    /// Cheap "does this directory contain at least one matching file?" check via lazy enumeration.
    /// </summary>
    /// <param name="dir">Directory to check.</param>
    /// <param name="pattern">File pattern.</param>
    /// <returns>True if at least one matching file exists.</returns>
    private static bool HasAtLeastOneFile(string dir, string pattern)
    {
        using var enumerator = Directory.EnumerateFiles(dir, pattern).GetEnumerator();
        return enumerator.MoveNext();
    }
}
