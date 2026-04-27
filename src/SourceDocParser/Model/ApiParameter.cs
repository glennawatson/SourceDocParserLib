// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// A parameter on a member.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The parameter type.</param>
/// <param name="IsOptional">Whether the parameter is optional.</param>
/// <param name="IsParams">Whether the parameter is a params array.</param>
/// <param name="IsIn">Whether the parameter is declared as in.</param>
/// <param name="IsOut">Whether the parameter is declared as out.</param>
/// <param name="IsRef">Whether the parameter is declared as ref.</param>
/// <param name="DefaultValue">The default value as a C# literal.</param>
public sealed record ApiParameter(
    string Name,
    ApiTypeReference Type,
    bool IsOptional,
    bool IsParams,
    bool IsIn,
    bool IsOut,
    bool IsRef,
    string? DefaultValue);
