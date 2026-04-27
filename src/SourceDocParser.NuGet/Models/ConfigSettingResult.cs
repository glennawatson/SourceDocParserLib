// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Models;

/// <summary>
/// Tri-state result the discovery walk branches on:
/// <see cref="SettingState.NotMentioned"/> = keep walking,
/// <see cref="SettingState.Cleared"/> = stop and use the platform
/// default, <see cref="SettingState.Found"/> = use <see cref="Value"/>.
/// </summary>
/// <param name="State">Which branch the config took.</param>
/// <param name="Value">The configured value when <see cref="State"/> is <see cref="SettingState.Found"/>; otherwise <see langword="null"/>.</param>
public readonly record struct ConfigSettingResult(SettingState State, string? Value);
