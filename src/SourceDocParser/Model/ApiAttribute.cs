// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// One attribute applied to a type, member, parameter, or return
/// value. Walker-extracted attributes are filtered to drop
/// compiler-emitted markers from <c>System.Runtime.CompilerServices</c>
/// (CompilerGenerated, NullableContext, ScopedRef, etc.) — what
/// remains matches what Microsoft Learn renders above a type's
/// signature line.
/// </summary>
/// <param name="DisplayName">The attribute's display name (without the <c>Attribute</c> suffix).</param>
/// <param name="Uid">The attribute type's documentation comment ID.</param>
/// <param name="Arguments">The constructor and named arguments, in source order.</param>
public sealed record ApiAttribute(string DisplayName, string Uid, ApiAttributeArgument[] Arguments);
