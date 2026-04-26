// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// One argument supplied to an attribute. Constructor arguments
/// have a null <see cref="Name"/>; named arguments carry the
/// property/field name. <see cref="Value"/> is the formatted source
/// representation (e.g. <c>"hello"</c>, <c>true</c>, <c>typeof(int)</c>,
/// <c>SomeEnum.Foo</c>) — already escaped, ready to drop into the
/// rendered attribute usage.
/// </summary>
/// <param name="Name">Named-argument label, or null for a positional/constructor argument.</param>
/// <param name="Value">Pre-formatted source representation of the argument value.</param>
public sealed record ApiAttributeArgument(string? Name, string Value);
