// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Reads <c>&lt;fallbackPackageFolders&gt;</c> entries — read-only
/// extra global caches the SDK probes before downloading. The
/// .NET SDK ships one at <c>~/.dotnet/NuGetFallbackFolder</c> that
/// we should consult so CI runs don't re-download common packages.
/// </summary>
internal static class FallbackPackageFoldersReader
{
    /// <summary>The XML section name for fallback package folders.</summary>
    private const string SectionName = "fallbackPackageFolders";

    /// <summary>The XML element name for adding a folder entry.</summary>
    private const string AddElementName = "add";

    /// <summary>The XML element name for clearing existing folder entries.</summary>
    private const string ClearElementName = "clear";

    /// <summary>The XML attribute name for the key.</summary>
    private const string KeyAttributeName = "key";

    /// <summary>The XML attribute name for the value (folder path).</summary>
    private const string ValueAttributeName = "value";

    /// <summary>Settings for the XML reader.</summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true, IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads fallback folder paths from the specified <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the per-file result with clear flag and ordered folder paths.</returns>
    public static Task<FallbackFoldersFileResult> ReadAsync(string configPath) =>
        ReadAsync(configPath, CancellationToken.None);

    /// <summary>Reads fallback folder paths from the specified <paramref name="configPath"/>.</summary>
    /// <remarks>
    /// This method opens the <c>nuget.config</c> file for reading and parses the <c>&lt;fallbackPackageFolders&gt;</c> section.
    /// It returns an ordered list of folder paths found in the configuration.
    /// </remarks>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the per-file result with clear flag and ordered folder paths.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configPath"/> is null or whitespace.</exception>
    public static async Task<FallbackFoldersFileResult> ReadAsync(
        string configPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var stream = new FileStream(
            configPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads fallback folder paths from the provided <paramref name="configStream"/>.
    /// </summary>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the per-file result with clear flag and ordered folder paths.</returns>
    public static Task<FallbackFoldersFileResult> ReadAsync(Stream configStream) =>
        ReadAsync(configStream, CancellationToken.None);

    /// <summary>Reads fallback folder paths from the provided <paramref name="configStream"/>.</summary>
    /// <remarks>
    /// This overload is primarily intended for testing purposes. It parses the XML content from the stream
    /// and extracts fallback package folders. It handles <c>&lt;clear /&gt;</c> and <c>&lt;add /&gt;</c> elements
    /// within the <c>&lt;fallbackPackageFolders&gt;</c> section.
    /// </remarks>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the per-file result with clear flag and ordered folder paths.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configStream"/> is null.</exception>
    public static async Task<FallbackFoldersFileResult> ReadAsync(
        Stream configStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configStream);
        var folders = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clearedSeen = false;

        using var reader = XmlReader.Create(configStream, _readerSettings);
        var insideSection = false;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element when
                    reader.LocalName.Equals(SectionName, StringComparison.OrdinalIgnoreCase):
                    {
                        insideSection = true;
                        continue;
                    }

                case XmlNodeType.EndElement when
                    reader.LocalName.Equals(SectionName, StringComparison.OrdinalIgnoreCase):
                    {
                        insideSection = false;
                        continue;
                    }
            }

            if (!insideSection || reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName.Equals(ClearElementName, StringComparison.OrdinalIgnoreCase))
            {
                folders.Clear();
                seenKeys.Clear();
                clearedSeen = true;
                continue;
            }

            if (!reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = reader.GetAttribute(KeyAttributeName);
            var value = reader.GetAttribute(ValueAttributeName);
            if (!TextHelpers.HasNonWhitespace(key) || !TextHelpers.HasNonWhitespace(value))
            {
                continue;
            }

            if (seenKeys.Add(key))
            {
                folders.Add(value);
            }
        }

        return new(clearedSeen, [.. folders]);
    }
}
