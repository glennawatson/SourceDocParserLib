// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using System.Xml;

namespace SourceDocParser.NuGet;

/// <summary>
/// Reads <c>&lt;packageSourceCredentials&gt;</c> entries — one
/// nested element per source whose name is the source key
/// (with spaces escaped as <c>_x0020_</c>). Inside each, the
/// <c>Username</c> + <c>ClearTextPassword</c> + optional
/// <c>ValidAuthenticationTypes</c> children carry the auth info.
/// We honour the cleartext path with <c>%ENV%</c> expansion;
/// encrypted-password blobs (Windows-DPAPI) are skipped.
/// </summary>
internal static partial class PackageSourceCredentialsReader
{
    /// <summary>The XML section name for package source credentials.</summary>
    private const string SectionName = "packageSourceCredentials";

    /// <summary>The XML element name for adding a credential entry.</summary>
    private const string AddElementName = "add";

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

    /// <summary>The escape sequence for spaces in source names.</summary>
    private const string SpaceEscape = "_x0020_";

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
    /// <remarks>
    /// This method opens the <c>nuget.config</c> file for reading and parses the <c>&lt;packageSourceCredentials&gt;</c> section.
    /// It handles environment variable expansion in the password and username fields using the <c>%VAR%</c> syntax.
    /// Encrypted passwords are not supported and will be skipped.
    /// </remarks>
    /// <param name="configPath">The absolute path to a <c>nuget.config</c> file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a dictionary of credentials keyed by the source name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configPath"/> is null or whitespace.</exception>
    public static async Task<Dictionary<string, PackageSourceCredential>> ReadAsync(string configPath, CancellationToken cancellationToken = default)
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
    /// <remarks>
    /// This overload is primarily intended for testing purposes. It parses the XML content from the stream
    /// and extracts package source credentials. It follows the same logic as <see cref="ReadAsync(string, CancellationToken)"/>
    /// regarding environment variable expansion and skipping encrypted passwords.
    /// </remarks>
    /// <param name="configStream">The open stream containing the <c>nuget.config</c> XML content.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains a dictionary of credentials keyed by the source name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configStream"/> is null.</exception>
    public static async Task<Dictionary<string, PackageSourceCredential>> ReadAsync(Stream configStream, CancellationToken cancellationToken = default)
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

            if (reader.NodeType == XmlNodeType.Element && IsCredentialChildElement(reader, currentSourceKey))
            {
                if (currentSourceKey is null)
                {
                    currentSourceKey = UnescapeSourceName(reader.LocalName);
                    continue;
                }

                ApplyAddEntry(reader, ref username, ref clearTextPassword, ref validAuthTypes);
            }

            if (reader.NodeType == XmlNodeType.EndElement && currentSourceKey is not null && IsSourceContainerEnd(reader, currentSourceKey))
            {
                FlushCurrent(result, ref currentSourceKey, ref username, ref clearTextPassword, ref validAuthTypes);
            }
        }

        return result;
    }

    /// <summary>Detects whether the reader sits on the per-source container or one of its inner add entries.</summary>
    /// <param name="reader">Reader positioned on an element.</param>
    /// <param name="currentSourceKey">Key of the source container we're already inside, or null.</param>
    /// <returns>True when the element should be processed by the credential walk.</returns>
    private static bool IsCredentialChildElement(XmlReader reader, string? currentSourceKey) =>
        currentSourceKey is null
            ? !reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase)
            : reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when the reader is on the closing tag of the current source container.</summary>
    /// <param name="reader">Reader positioned on an end element.</param>
    /// <param name="currentSourceKey">The source key we're currently accumulating credentials for.</param>
    /// <returns>True when the close tag matches the open container.</returns>
    private static bool IsSourceContainerEnd(XmlReader reader, string currentSourceKey) =>
        UnescapeSourceName(reader.LocalName).Equals(currentSourceKey, StringComparison.Ordinal);

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
            username = ExpandEnvVars(value);
            return;
        }

        if (ClearTextPasswordKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            clearTextPassword = ExpandEnvVars(value);
            return;
        }

        if (!ValidAuthenticationTypesKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        validAuthTypes = ExpandEnvVars(value);
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

    /// <summary>Decodes the <c>_x0020_</c> space escape NuGet uses for source names with spaces.</summary>
    /// <param name="elementName">Raw element local-name.</param>
    /// <returns>The friendly source name.</returns>
    private static string UnescapeSourceName(string elementName) =>
        elementName.Replace(SpaceEscape, " ", StringComparison.Ordinal);

    /// <summary>Expands <c>%VAR%</c> sequences against the process environment.</summary>
    /// <param name="value">Raw value from the config.</param>
    /// <returns>Value with env-var references substituted; unresolved sequences stay literal.</returns>
    private static string ExpandEnvVars(string value) =>
        EnvVarPattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            return Environment.GetEnvironmentVariable(name) ?? match.Value;
        });

    /// <summary>Cached regex for the <c>%VAR%</c> sequence used by env-var expansion.</summary>
    /// <returns>The cached regex instance.</returns>
    [GeneratedRegex(@"%([^%]+)%")]
    private static partial Regex EnvVarPattern();
}
