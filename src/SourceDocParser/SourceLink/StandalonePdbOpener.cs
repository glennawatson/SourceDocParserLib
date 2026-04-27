// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace SourceDocParser.SourceLink;

/// <summary>
/// Opens a standalone <c>.pdb</c> file (the form produced when an
/// assembly was built with <c>DebugType=portable</c> instead of
/// <c>embedded</c>). Lifted out of <see cref="SourceLinkReader"/> so
/// the missing-file / corrupt-file branches can be exercised with a
/// direct unit test instead of crafting an assembly with a sibling
/// .pdb file.
/// </summary>
internal static class StandalonePdbOpener
{
    /// <summary>
    /// Opens <paramref name="pdbPath"/> as a portable PDB. Returns
    /// false when the file is missing or doesn't parse; the out
    /// parameters are nulled in that case so callers don't have to
    /// reset them themselves.
    /// </summary>
    /// <param name="pdbPath">Absolute path to the .pdb file.</param>
    /// <param name="provider">Receives the metadata reader provider on success.</param>
    /// <param name="reader">Receives the metadata reader on success.</param>
    /// <returns>True on success, false on missing / malformed input.</returns>
    public static bool TryOpen(
        string pdbPath,
        [NotNullWhen(true)] out MetadataReaderProvider? provider,
        [NotNullWhen(true)] out MetadataReader? reader)
    {
        provider = null;
        reader = null;

        if (!File.Exists(pdbPath))
        {
            return false;
        }

        // Hold both the stream and the provider in locals until
        // ownership transfers — to the provider on success of
        // FromPortablePdbStream, then to the out parameter on success
        // of GetMetadataReader. The finally only disposes whatever
        // ownership we still hold, so a disposed provider never leaks
        // out to the caller.
        FileStream? pdbStream = null;
        MetadataReaderProvider? localProvider = null;
        try
        {
            pdbStream = File.OpenRead(pdbPath);
            localProvider = MetadataReaderProvider.FromPortablePdbStream(
                pdbStream,
                MetadataStreamOptions.PrefetchMetadata);
            pdbStream = null;
            reader = localProvider.GetMetadataReader();
            provider = localProvider;
            localProvider = null;
            return true;
        }
        catch
        {
            provider = null;
            reader = null;
            return false;
        }
        finally
        {
            pdbStream?.Dispose();
            localProvider?.Dispose();
        }
    }
}
