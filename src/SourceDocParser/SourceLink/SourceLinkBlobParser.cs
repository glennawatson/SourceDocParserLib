// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.Metadata;

namespace SourceDocParser.SourceLink;

/// <summary>
/// Parses the SourceLink JSON blob a portable PDB stores under a
/// well-known custom-debug-information GUID. Lifted out of
/// <see cref="SourceLinkReader"/> so the parse + scan can be unit
/// tested without crafting a PDB — and so the malformed-blob catch
/// is reachable from a direct test feeding garbage bytes.
/// </summary>
internal static class SourceLinkBlobParser
{
    /// <summary>
    /// GUID of the SourceLink custom debug information record
    /// portable PDB writers stamp the blob with.
    /// </summary>
    internal static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    /// <summary>
    /// Decodes a raw SourceLink JSON blob into a map. Returns null
    /// for empty / malformed input rather than throwing — matches
    /// the rest of the reader pipeline's degrade-gracefully contract.
    /// </summary>
    /// <param name="bytes">Raw bytes of the SourceLink JSON blob.</param>
    /// <returns>The populated map, or null when the blob doesn't parse.</returns>
    public static SourceLinkMap? TryParse(in ReadOnlyMemory<byte> bytes)
    {
        try
        {
            List<SourceLinkMapEntry> entries = [.. SourceLinkJsonParser.Parse(bytes)];
            return entries is [_, ..] ? new SourceLinkMap(entries) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Walks the CustomDebugInformation table looking for the
    /// SourceLink record and decodes the first one found.
    /// </summary>
    /// <param name="pdb">Open PDB metadata reader.</param>
    /// <returns>The map, or null when no record was present (or it didn't parse).</returns>
    public static SourceLinkMap? FindAndParse(MetadataReader pdb) => FindAndParse(pdb, SourceLinkGuid);

    /// <summary>
    /// Walks the CustomDebugInformation table for the supplied
    /// <paramref name="recordGuid"/> and decodes the first match.
    /// Tests pass a non-matching GUID to exercise the no-record
    /// fall-through; production callers always pass the SourceLink
    /// GUID via the overload above.
    /// </summary>
    /// <param name="pdb">Open PDB metadata reader.</param>
    /// <param name="recordGuid">Custom-debug-info GUID to look for.</param>
    /// <returns>The map, or null when no record was present (or it didn't parse).</returns>
    internal static SourceLinkMap? FindAndParse(MetadataReader pdb, in Guid recordGuid)
    {
        ArgumentNullException.ThrowIfNull(pdb);

        foreach (var handle in pdb.CustomDebugInformation)
        {
            var info = pdb.GetCustomDebugInformation(handle);
            if (pdb.GetGuid(info.Kind) != recordGuid)
            {
                continue;
            }

            var blob = pdb.GetBlobReader(info.Value);
            var bytes = blob.ReadBytes(blob.Length);
            return TryParse(bytes);
        }

        return null;
    }
}
