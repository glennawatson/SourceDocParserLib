// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Models;

/// <summary>
/// One per-source credential block from a nuget.config
/// <c>packageSourceCredentials</c> section. We only consume
/// the cleartext / env-expanded path (encrypted blobs require
/// Windows DPAPI which we don't take a dep on); GitHub Packages and
/// Azure Artifacts both work via PAT-in-env-var.
/// </summary>
/// <param name="SourceKey">Friendly source name (matches <see cref="PackageSource.Key"/>).</param>
/// <param name="Username">User name to send.</param>
/// <param name="ClearTextPassword">Already-resolved password (env-var expanded if the config used <c>%VAR%</c>).</param>
/// <param name="ValidAuthenticationTypes">Comma-separated auth types (e.g. <c>basic</c>) -- null when unset.</param>
public readonly record struct PackageSourceCredential(
    string SourceKey,
    string Username,
    string ClearTextPassword,
    string? ValidAuthenticationTypes);
