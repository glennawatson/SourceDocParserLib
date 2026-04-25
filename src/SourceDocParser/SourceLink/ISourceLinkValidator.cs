// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// HEAD-checks the source URLs collected during extraction so the docs
/// site never advertises a link that no longer resolves.
/// </summary>
public interface ISourceLinkValidator
{
    /// <summary>
    /// Validates the supplied source-link entries.
    /// </summary>
    /// <param name="entries">Entries to validate. Empty input is a no-op.</param>
    /// <param name="failOnBroken">When true, throws if any URL fails to resolve.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <returns>Count of broken URLs.</returns>
    Task<int> ValidateAsync(List<SourceLinkEntry> entries, bool failOnBroken = false, ILogger? logger = null);
}
