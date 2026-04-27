// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Reads <c>&lt;packageSourceCredentials&gt;</c> entries — one
/// nested element per source whose name is the source key
/// (with spaces escaped as <c>_x0020_</c>). Inside each, the
/// <c>Username</c> + <c>ClearTextPassword</c> + optional
/// <c>ValidAuthenticationTypes</c> children carry the auth info.
/// We honour the cleartext path with <c>%ENV%</c> expansion;
/// encrypted-password blobs (Windows-DPAPI) are skipped. The
/// per-element classification, source-name unescape and env-var
/// expansion live in <see cref="PackageSourceCredentialParser"/>.
/// </summary>
internal static class PackageSourceCredentialsReader
{
    /// <summary>The XML section name for package source credentials.</summary>
    private const string SectionName = "packageSourceCredentials";

    /// <summary>The XML attribute name for the key.</summary>
    private const string KeyAttributeName = "key";

    /// <summary>The XML attribute name for the value.</summary>
    private const string ValueAttributeName = "value";

    /// <summary>The key name for the username.</summary>
    private const string UsernameKey = "Username";

    /// <summary>The key name for the clear text password.</summary>
    private const string ClearTextPasswordKey = "ClearTextPassword";

    /// <summary>The key name for valid authentication types.</summary>
    private const string ValidAuthenticationTypesKey = "ValidAuthenticationTypes";

    /// <summary>Settings for the XML reader.</summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads all credential entries from the specified <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a dictionary of credentials keyed by the source name.</returns>
    public static Task<Dictionary<string, PackageSourceCredential>> ReadAsync(string configPath) =>
        ReadAsync(configPath, CancellationToken.None);

    /// <summary>
    /// Reads all credential entries from the specified <paramref name="configPath"/>.
    /// </summary>
    /// <remarks>
    /// This method opens the <c>nuget.config</c> file for reading and parses the <c>&lt;packageSourceCredentials&gt;</c> section.
    /// It handles environment variable expansion in the password and username fields using the <c>%VAR%</c> syntax.
    /// Encrypted passwords are not supported and will be skipped.
    /// </remarks>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a dictionary of credentials keyed by the source name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configPath"/> is null or whitespace.</exception>
    public static async Task<Dictionary<string, PackageSourceCredential>> ReadAsync(string configPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads all credential entries from the provided <paramref name="configStream"/>.
    /// </summary>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a dictionary of credentials keyed by the source name.</returns>
    public static Task<Dictionary<string, PackageSourceCredential>> ReadAsync(Stream configStream) =>
        ReadAsync(configStream, CancellationToken.None);

    /// <summary>
    /// Reads all credential entries from the provided <paramref name="configStream"/>.
    /// </summary>
    /// <remarks>
    /// This overload is primarily intended for testing purposes. It parses the XML content from the stream
    /// and extracts package source credentials. It follows the same logic as <see cref="ReadAsync(string, CancellationToken)"/>
    /// regarding environment variable expansion and skipping encrypted passwords.
    /// </remarks>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a dictionary of credentials keyed by the source name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configStream"/> is null.</exception>
    public static async Task<Dictionary<string, PackageSourceCredential>> ReadAsync(Stream configStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configStream);
        var result = new Dictionary<string, PackageSourceCredential>(StringComparer.OrdinalIgnoreCase);

        using var reader = XmlReader.Create(configStream, _readerSettings);
        var insideSection = false;
        string? currentSourceKey = null;
        string? username = null;
        string? clearTextPassword = null;
        string? validAuthTypes = null;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName.Equals(SectionName, StringComparison.OrdinalIgnoreCase))
            {
                insideSection = true;
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName.Equals(SectionName, StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent(result, ref currentSourceKey, ref username, ref clearTextPassword, ref validAuthTypes);
                insideSection = false;
                continue;
            }

            if (!insideSection)
            {
                continue;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element when PackageSourceCredentialParser.IsCredentialChildElement(reader, currentSourceKey):
                    {
                        if (currentSourceKey is null)
                        {
                            currentSourceKey = PackageSourceCredentialParser.UnescapeSourceName(reader.LocalName);
                            continue;
                        }

                        ApplyAddEntry(reader, ref username, ref clearTextPassword, ref validAuthTypes);
                        break;
                    }

                case XmlNodeType.EndElement when currentSourceKey is not null && PackageSourceCredentialParser.IsSourceContainerEnd(reader, currentSourceKey):
                    {
                        FlushCurrent(result, ref currentSourceKey, ref username, ref clearTextPassword, ref validAuthTypes);
                        break;
                    }
            }
        }

        return result;
    }

    /// <summary>Reads one inner add element and updates the matching credential field.</summary>
    /// <param name="reader">The XML reader positioned on the add element.</param>
    /// <param name="username">A reference to the username accumulator.</param>
    /// <param name="clearTextPassword">A reference to the password accumulator.</param>
    /// <param name="validAuthTypes">A reference to the auth types accumulator.</param>
    private static void ApplyAddEntry(XmlReader reader, ref string? username, ref string? clearTextPassword, ref string? validAuthTypes)
    {
        var key = reader.GetAttribute(KeyAttributeName);
        var value = reader.GetAttribute(ValueAttributeName);
        if (key is null || value is null)
        {
            return;
        }

        if (UsernameKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            username = PackageSourceCredentialParser.ExpandEnvironmentVariables(value);
            return;
        }

        if (ClearTextPasswordKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            clearTextPassword = PackageSourceCredentialParser.ExpandEnvironmentVariables(value);
            return;
        }

        if (!ValidAuthenticationTypesKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        validAuthTypes = PackageSourceCredentialParser.ExpandEnvironmentVariables(value);
    }

    /// <summary>Persists the accumulated per-source credential into the result dictionary and resets the accumulators.</summary>
    /// <param name="sink">The destination dictionary for credentials.</param>
    /// <param name="currentSourceKey">The source key being closed. Reset to null.</param>
    /// <param name="username">The username accumulator. Reset to null.</param>
    /// <param name="clearTextPassword">The password accumulator. Reset to null.</param>
    /// <param name="validAuthTypes">The auth-types accumulator. Reset to null.</param>
    private static void FlushCurrent(
        Dictionary<string, PackageSourceCredential> sink,
        ref string? currentSourceKey,
        ref string? username,
        ref string? clearTextPassword,
        ref string? validAuthTypes)
    {
        if (currentSourceKey is not null && username is not null && clearTextPassword is not null)
        {
            sink[currentSourceKey] = new(currentSourceKey, username, clearTextPassword, validAuthTypes);
        }

        currentSourceKey = null;
        username = null;
        clearTextPassword = null;
        validAuthTypes = null;
    }
}
