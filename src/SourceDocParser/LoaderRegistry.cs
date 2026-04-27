// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;

namespace SourceDocParser;

/// <summary>
/// Manages and tracks instances of <see cref="ICompilationLoader"/> for the duration of
/// a single operation, ensuring proper disposal of memory-mapped resources tied to
/// the BCL reference pack. Implements <see cref="IDisposable"/> to release resources
/// when the scope exits.
/// </summary>
internal sealed class LoaderRegistry : IDisposable
{
    /// <summary>Tracked loaders in registration order.</summary>
    private readonly List<ICompilationLoader> _loaders = [];

    /// <summary>
    /// Registers <paramref name="loader"/> for disposal and returns it
    /// for fluent assignment at the call site.
    /// </summary>
    /// <param name="loader">Loader to track.</param>
    /// <returns>The same loader instance.</returns>
    public ICompilationLoader Track(ICompilationLoader loader)
    {
        _loaders.Add(loader);
        return loader;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        for (var i = 0; i < _loaders.Count; i++)
        {
            _loaders[i].Dispose();
        }

        _loaders.Clear();
    }
}
