// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SourceDocParser.SourceLink;

/// <summary>
/// Reads SourceLink data and per-method debug information from a PDB.
/// </summary>
/// <remarks>
/// Supports both embedded portable PDBs and standalone .pdb files.
/// </remarks>
internal sealed class SourceLinkReader : IDisposable
{
    /// <summary>
    /// The opened PE stream for the assembly.
    /// </summary>
    private readonly Stream? _peStream;

    /// <summary>
    /// The opened PE reader for the assembly.
    /// </summary>
    private readonly PEReader? _peReader;

    /// <summary>
    /// The PDB metadata reader provider.
    /// </summary>
    private readonly MetadataReaderProvider? _pdbProvider;

    /// <summary>
    /// The PDB metadata reader.
    /// </summary>
    private readonly MetadataReader? _pdbReader;

    /// <summary>
    /// The parsed SourceLink map.
    /// </summary>
    private readonly SourceLinkMap? _map;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceLinkReader"/> class.
    /// </summary>
    /// <param name="assemblyPath">Absolute path to the .dll on disk.</param>
    public SourceLinkReader(string assemblyPath)
    {
        try
        {
            _peStream = File.OpenRead(assemblyPath);
            _peReader = new(_peStream);

            if (!TryOpenPdb(_peReader, assemblyPath, out var provider, out var reader))
            {
                return;
            }

            _pdbProvider = provider;
            _pdbReader = reader;
            _map = SourceLinkBlobParser.FindAndParse(reader);
        }
        catch
        {
            Dispose();
            _peStream = null;
            _peReader = null;
            _pdbProvider = null;
            _pdbReader = null;
            _map = null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the assembly carried readable SourceLink data.
    /// </summary>
    public bool HasSourceLink => _pdbReader is not null && _map is not null;

    /// <summary>
    /// Resolves a method's source location via the PDB's debug information.
    /// </summary>
    /// <param name="metadataToken">The metadata token for the method symbol.</param>
    /// <returns>The source location, or null if resolution fails.</returns>
    public SourceLocation? GetMethodLocation(int metadataToken)
    {
        if (_pdbReader is null)
        {
            return null;
        }

        try
        {
            var rowNumber = metadataToken & 0x00FFFFFF;
            if (rowNumber <= 0)
            {
                return null;
            }

            var debugHandle = MetadataTokens.MethodDebugInformationHandle(rowNumber);
            var debugInfo = _pdbReader.GetMethodDebugInformation(debugHandle);

            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden)
                {
                    continue;
                }

                var document = _pdbReader.GetDocument(sp.Document);
                var path = _pdbReader.GetString(document.Name);
                return new SourceLocation(path, sp.StartLine);
            }
        }
        catch
        {
            // Bad token or malformed PDB row.
        }

        return null;
    }

    /// <summary>
    /// Substitutes a local source path through the SourceLink map.
    /// </summary>
    /// <param name="localPath">Path as recorded by the PDB.</param>
    /// <returns>The raw remote URL, or null.</returns>
    public string? ResolveRawUrl(string localPath) =>
        _map?.TryResolve(localPath);

    /// <inheritdoc/>
    public void Dispose()
    {
        _pdbProvider?.Dispose();
        _peReader?.Dispose();
        _peStream?.Dispose();
    }

    /// <summary>
    /// Tries to open the PDB for the assembly.
    /// </summary>
    /// <param name="peReader">The opened PE reader.</param>
    /// <param name="assemblyPath">Path to the .dll.</param>
    /// <param name="provider">The PDB metadata reader provider.</param>
    /// <param name="reader">The PDB metadata reader.</param>
    /// <returns>True if a PDB was opened; false otherwise.</returns>
    private static bool TryOpenPdb(
        PEReader peReader,
        string assemblyPath,
        [NotNullWhen(true)] out MetadataReaderProvider? provider,
        [NotNullWhen(true)] out MetadataReader? reader)
    {
        provider = null;
        reader = null;

        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type != DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                continue;
            }

            try
            {
                provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                reader = provider.GetMetadataReader();
                return true;
            }
            catch
            {
                provider?.Dispose();
                provider = null;
                reader = null;
            }
        }

        return StandalonePdbOpener.TryOpen(Path.ChangeExtension(assemblyPath, ".pdb"), out provider, out reader);
    }
}
