// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Loads the XML documentation file sitting next to an assembly,
/// degrading to null on any failure so the surrounding metadata
/// reference cache can still produce a reference. Lifted out of
/// <see cref="MetadataReferenceCache"/> so the parse-failure
/// fallback can be exercised directly.
/// </summary>
internal static partial class XmlDocsLoader
{
    /// <summary>
    /// Attempts to load the <c>.xml</c> file paired with
    /// <paramref name="assemblyPath"/>. Returns null when the file
    /// is missing or fails to parse — callers can still produce a
    /// metadata reference, just without docs attached.
    /// </summary>
    /// <param name="assemblyPath">The absolute path to the assembly DLL.</param>
    /// <param name="logger">Target logger.</param>
    /// <returns>A documentation provider when the XML loaded; null otherwise.</returns>
    public static FileXmlDocumentationProvider? TryLoad(string assemblyPath, ILogger logger)
    {
        try
        {
            var source = XmlDocSource.TryLoad(assemblyPath);
            if (source is null)
            {
                return null;
            }

            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            LogXmlDocLoaded(logger, source.Count, xmlPath);
            return new(source, xmlPath);
        }
        catch (Exception ex)
        {
            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            LogXmlDocParseFailed(logger, ex, xmlPath);
            return null;
        }
    }

    /// <summary>Logs a successful XML doc load alongside an assembly.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="entryCount">Number of XML doc entries parsed.</param>
    /// <param name="xmlPath">Absolute path to the loaded XML doc file.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "  loaded {EntryCount} XML doc entry/ies from {XmlPath}")]
    private static partial void LogXmlDocLoaded(ILogger logger, int entryCount, string xmlPath);

    /// <summary>Logs failure to parse the XML doc file (load continues without docs).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="exception">Parse exception.</param>
    /// <param name="xmlPath">Absolute path to the failed XML doc file.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse XML doc file '{XmlPath}'")]
    private static partial void LogXmlDocParseFailed(ILogger logger, Exception exception, string xmlPath);
}
