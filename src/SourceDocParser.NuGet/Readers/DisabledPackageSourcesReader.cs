// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Reads <c>disabledPackageSources</c> from a nuget.config --
/// returns the source keys whose <c>value</c> attribute is the
/// string <c>"true"</c>. The fetcher filters these out of the
/// resolved source list so users behind corp proxies who disable
/// nuget.org don't get fetches against it.
/// </summary>
internal static class DisabledPackageSourcesReader
{
    /// <summary>The XML section name for disabled package sources.</summary>
    private const string SectionName = "disabledPackageSources";

    /// <summary>The XML element name for adding a disabled source.</summary>
    private const string AddElementName = "add";

    /// <summary>The XML attribute name for the source key.</summary>
    private const string KeyAttributeName = "key";

    /// <summary>The XML attribute name for the value.</summary>
    private const string ValueAttributeName = "value";

    /// <summary>The literal string value indicating a source is disabled.</summary>
    private const string TrueLiteral = "true";

    /// <summary>Settings for the XML reader.</summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true, IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads the disabled-source keys from the specified <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a set of disabled source keys (case-insensitive).</returns>
    public static Task<HashSet<string>> ReadAsync(string configPath) =>
        ReadAsync(configPath, CancellationToken.None);

    /// <summary>Reads the disabled-source keys from the specified <paramref name="configPath"/>.</summary>
    /// <remarks>
    /// This method opens the <c>nuget.config</c> file for reading and parses the <c>disabledPackageSources</c> section.
    /// It returns a set of keys for sources that have been explicitly disabled by setting their value to <c>"true"</c>.
    /// </remarks>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a set of disabled source keys (case-insensitive).</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configPath"/> is null or whitespace.</exception>
    public static async Task<HashSet<string>> ReadAsync(
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
    /// Reads the disabled-source keys from the provided <paramref name="configStream"/>.
    /// </summary>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a set of disabled source keys (case-insensitive).</returns>
    public static Task<HashSet<string>> ReadAsync(Stream configStream) =>
        ReadAsync(configStream, CancellationToken.None);

    /// <summary>Reads the disabled-source keys from the provided <paramref name="configStream"/>.</summary>
    /// <remarks>
    /// This overload is primarily intended for testing purposes. It parses the XML content from the stream
    /// and extracts disabled package sources. It identifies sources where the <c>value</c> attribute
    /// is set to <c>"true"</c>.
    /// </remarks>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a set of disabled source keys (case-insensitive).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configStream"/> is null.</exception>
    public static async Task<HashSet<string>> ReadAsync(
        Stream configStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configStream);
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = XmlReader.Create(configStream, _readerSettings);
        var insideSection = false;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader)
            {
                case { NodeType: XmlNodeType.Element } when
                    reader.LocalName.Equals(SectionName, StringComparison.OrdinalIgnoreCase):
                    {
                        insideSection = true;
                        continue;
                    }

                case { NodeType: XmlNodeType.EndElement } when
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

            if (!reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = reader.GetAttribute(KeyAttributeName);
            var value = reader.GetAttribute(ValueAttributeName);
            if (!TextHelpers.HasNonWhitespace(key) || !TrueLiteral.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            disabled.Add(key);
        }

        return disabled;
    }
}
