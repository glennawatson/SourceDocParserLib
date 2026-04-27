// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Hand-rolled XmlReader scanner for <c>nuget.config</c> files —
/// extracts only the values we need (today: the
/// <c>globalPackagesFolder</c> setting). Same shape as
/// <see cref="NuspecDependencyReader"/>: small composable helpers,
/// no XDocument materialisation, no LINQ-to-XML.
/// </summary>
internal static class NuGetConfigReader
{
    /// <summary>The XML element NuGet wraps every per-key setting in (e.g. <c>&lt;add key="…" value="…"/&gt;</c>).</summary>
    private const string AddElementName = "add";

    /// <summary>The XML element NuGet uses to drop accumulated parent-config values.</summary>
    private const string ClearElementName = "clear";

    /// <summary>Container element holding global config settings.</summary>
    private const string ConfigElementName = "config";

    /// <summary>Attribute on the <c>add</c> element carrying the setting name.</summary>
    private const string KeyAttributeName = "key";

    /// <summary>Attribute on the <c>add</c> element carrying the setting value.</summary>
    private const string ValueAttributeName = "value";

    /// <summary>Setting key for the global packages folder override.</summary>
    private const string GlobalPackagesFolderKey = "globalPackagesFolder";

    /// <summary>Reader settings shared across every parse — async on so we can pump a FileStream that opened with FileOptions.Asynchronous.</summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads the <c>globalPackagesFolder</c> setting from
    /// <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">Absolute path to a <c>nuget.config</c>.</param>
    /// <returns>Tri-state result — Found / Cleared / NotMentioned.</returns>
    public static Task<ConfigSettingResult> ReadGlobalPackagesFolderAsync(string configPath) =>
        ReadGlobalPackagesFolderAsync(configPath, CancellationToken.None);

    /// <summary>
    /// Reads the <c>globalPackagesFolder</c> setting from
    /// <paramref name="configPath"/>. Returns <see langword="null"/>
    /// when the file doesn't carry that key — caller falls back to
    /// the env var / platform default.
    /// </summary>
    /// <param name="configPath">Absolute path to a <c>nuget.config</c>.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Tri-state result — Found / Cleared / NotMentioned.</returns>
    public static async Task<ConfigSettingResult> ReadGlobalPackagesFolderAsync(string configPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadGlobalPackagesFolderAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the <c>globalPackagesFolder</c> setting from an open
    /// <c>nuget.config</c> stream.
    /// </summary>
    /// <param name="configStream">Open stream positioned at the start of the <c>nuget.config</c> XML.</param>
    /// <returns>Tri-state result — Found / Cleared / NotMentioned.</returns>
    public static Task<ConfigSettingResult> ReadGlobalPackagesFolderAsync(Stream configStream) =>
        ReadGlobalPackagesFolderAsync(configStream, CancellationToken.None);

    /// <summary>
    /// Stream-based overload — useful for tests that want to feed
    /// canned XML without a tempfile dance. Walks until it finds the
    /// first <c>&lt;add key="globalPackagesFolder" value="…"/&gt;</c>
    /// inside a <c>&lt;config&gt;</c> parent and returns the value.
    /// </summary>
    /// <param name="configStream">Open stream positioned at the start of the <c>nuget.config</c> XML.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Tri-state result — Found / Cleared / NotMentioned.</returns>
    public static async Task<ConfigSettingResult> ReadGlobalPackagesFolderAsync(Stream configStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configStream);

        // Per NuGet's SettingFactory.ParseChildren: walk the entire
        // <config> section in document order. Track the first <add>
        // for the key after the most-recent <clear/>. Within a file,
        // <clear/> wipes accumulated state and a subsequent <add>
        // re-introduces a value; without a clear, the first add wins.
        using var reader = XmlReader.Create(configStream, _readerSettings);
        var insideConfig = false;
        string? foundValue = null;
        var clearedSeen = false;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryUpdateConfigScope(reader, ref insideConfig) || !ShouldInspectConfigElement(reader, insideConfig))
            {
                continue;
            }

            if (HandleConfigClear(reader, ref foundValue, ref clearedSeen))
            {
                continue;
            }

            TryCaptureGlobalPackagesFolder(reader, ref foundValue);
        }

        if (foundValue is not null)
        {
            return new(SettingState.Found, foundValue);
        }

        return clearedSeen
            ? new(SettingState.Cleared, null)
            : new(SettingState.NotMentioned, null);
    }

    /// <summary>
    /// Updates whether the reader is currently inside the config section.
    /// </summary>
    /// <param name="reader">Reader positioned on the current node.</param>
    /// <param name="insideConfig">Current in-config flag.</param>
    /// <returns>True when the current node only updated scope.</returns>
    internal static bool TryUpdateConfigScope(XmlReader reader, ref bool insideConfig)
    {
        if (reader is { NodeType: XmlNodeType.Element } && reader.LocalName.Equals(ConfigElementName, StringComparison.OrdinalIgnoreCase))
        {
            insideConfig = true;
            return true;
        }

        if (reader is not { NodeType: XmlNodeType.EndElement } ||
            !reader.LocalName.Equals(ConfigElementName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        insideConfig = false;
        return true;
    }

    /// <summary>
    /// Returns true when the current node should be inspected as a config child element.
    /// </summary>
    /// <param name="reader">Reader positioned on the current node.</param>
    /// <param name="insideConfig">Whether the parser is currently inside the config section.</param>
    /// <returns>True when the node is a candidate config child element.</returns>
    internal static bool ShouldInspectConfigElement(XmlReader reader, bool insideConfig) =>
        insideConfig && reader.NodeType == XmlNodeType.Element;

    /// <summary>
    /// Handles a clear directive inside the config section.
    /// </summary>
    /// <param name="reader">Reader positioned on the current element.</param>
    /// <param name="foundValue">Current in-file value accumulator.</param>
    /// <param name="clearedSeen">Whether a clear directive has been seen.</param>
    /// <returns>True when the element was a clear directive.</returns>
    internal static bool HandleConfigClear(XmlReader reader, ref string? foundValue, ref bool clearedSeen)
    {
        if (!IsClearElement(reader))
        {
            return false;
        }

        foundValue = null;
        clearedSeen = true;
        return true;
    }

    /// <summary>
    /// Captures the first globalPackagesFolder add value after the most recent clear.
    /// </summary>
    /// <param name="reader">Reader positioned on the current element.</param>
    /// <param name="foundValue">Current in-file value accumulator.</param>
    internal static void TryCaptureGlobalPackagesFolder(XmlReader reader, ref string? foundValue)
    {
        if (!IsAddElement(reader))
        {
            return;
        }

        if (foundValue is not null)
        {
            return;
        }

        var key = reader.GetAttribute(KeyAttributeName);
        if (!GlobalPackagesFolderKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var value = reader.GetAttribute(ValueAttributeName);
        if (!TextHelpers.HasNonWhitespace(value))
        {
            return;
        }

        foundValue = value;
    }

    /// <summary>
    /// Returns true when the reader is positioned on a
    /// <c>&lt;clear/&gt;</c> element. Used by the parse loop to
    /// reset the within-file accumulator.
    /// </summary>
    /// <param name="reader">Reader positioned on an element.</param>
    /// <returns>True when the element is a <c>clear</c> directive.</returns>
    private static bool IsClearElement(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.LocalName.Equals(ClearElementName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the reader is positioned on an <c>&lt;add&gt;</c>
    /// element. Local-name match is enough — nuget.config carries no
    /// xmlns and the local name uniquely identifies the setting entry.
    /// </summary>
    /// <param name="reader">Reader positioned on an element.</param>
    /// <returns>True when the element is the per-setting <c>add</c> entry.</returns>
    private static bool IsAddElement(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase);
    }
}
