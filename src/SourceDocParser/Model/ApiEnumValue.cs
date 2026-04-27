// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// One value declared inside an <see cref="ApiEnumType"/>. Carries
/// just enough context for the emitter to render the value table on
/// the enum's type page; per-value markdown pages are intentionally
/// not produced.
/// </summary>
/// <param name="Name">Value identifier (e.g. <c>Friday</c>).</param>
/// <param name="Uid">XML doc member ID (e.g. <c>F:My.Day.Friday</c>).</param>
/// <param name="Value">String form of the underlying constant value (decimal for integers).</param>
/// <param name="Documentation">Parsed XML documentation for the value.</param>
/// <param name="SourceUrl">SourceLink URL pointing at the value declaration.</param>
public sealed record ApiEnumValue(
    string Name,
    string Uid,
    string Value,
    ApiDocumentation Documentation,
    string? SourceUrl);
