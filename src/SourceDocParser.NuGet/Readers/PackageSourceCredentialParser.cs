// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using System.Xml;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Pure helpers for the <c>nuget.config</c>
/// <c>&lt;packageSourceCredentials&gt;</c> walker — element-type
/// classification, source-name unescaping (the <c>_x0020_</c> space
/// escape), and <c>%VAR%</c> environment-variable expansion. Lifted
/// out of <see cref="PackageSourceCredentialsReader"/> so the XML
/// state-machine in the reader stays focused on flow control while
/// the per-element predicates and string transforms live in one
/// testable place.
/// </summary>
internal static partial class PackageSourceCredentialParser
{
    /// <summary>The XML element name for adding a credential entry.</summary>
    private const string AddElementName = "add";

    /// <summary>The escape sequence for spaces in source names.</summary>
    private const string SpaceEscape = "_x0020_";

    /// <summary>
    /// Detects whether the reader sits on the per-source container or
    /// one of its inner add entries — drives the credential walk's
    /// "open container" vs "process inner add" branch.
    /// </summary>
    /// <param name="reader">Reader positioned on an element.</param>
    /// <param name="currentSourceKey">Key of the source container we're already inside, or null.</param>
    /// <returns>True when the element should be processed by the credential walk.</returns>
    public static bool IsCredentialChildElement(XmlReader reader, string? currentSourceKey)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return currentSourceKey is null
            ? !reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase)
            : reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when <paramref name="reader"/> is on the closing
    /// tag of the per-source container we're currently accumulating
    /// credentials for. The reader's local name is unescaped before
    /// the comparison so a source named <c>"My Source"</c> matches
    /// even though the XML element is <c>My_x0020_Source</c>.
    /// </summary>
    /// <param name="reader">Reader positioned on an end element.</param>
    /// <param name="currentSourceKey">The source key currently being accumulated.</param>
    /// <returns>True when the close tag matches the open container.</returns>
    public static bool IsSourceContainerEnd(XmlReader reader, string currentSourceKey)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return UnescapeSourceName(reader.LocalName).Equals(currentSourceKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Decodes the <c>_x0020_</c> space escape NuGet uses for source
    /// names with spaces in their key.
    /// </summary>
    /// <param name="elementName">Raw element local-name.</param>
    /// <returns>The friendly source name.</returns>
    public static string UnescapeSourceName(string elementName)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        return elementName.Replace(SpaceEscape, " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Expands <c>%VAR%</c> sequences against the process environment.
    /// Unresolved sequences pass through verbatim so the original
    /// literal is preserved when the variable isn't set.
    /// </summary>
    /// <param name="value">Raw value from the config.</param>
    /// <returns>Value with env-var references substituted; unresolved sequences stay literal.</returns>
    public static string ExpandEnvironmentVariables(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return EnvVarPattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            return Environment.GetEnvironmentVariable(name) ?? match.Value;
        });
    }

    /// <summary>Cached regex for the <c>%VAR%</c> sequence used by env-var expansion.</summary>
    /// <returns>The cached regex instance.</returns>
    [GeneratedRegex(@"%([^%]+)%")]
    private static partial Regex EnvVarPattern();
}
