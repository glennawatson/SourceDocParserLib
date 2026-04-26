// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Container for one TFM's worth of documented types.
/// </summary>
/// <param name="Tfm">The TFM these types were extracted from.</param>
/// <param name="Types">Documented public types in deterministic order.</param>
public sealed record ApiCatalog(string Tfm, ApiType[] Types);
