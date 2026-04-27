// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Synthesises the C# declaration line that docfx renders inside
/// the type-level <c>syntax: content:</c> field — the
/// <c>public class Foo : Bar, IBaz</c> shape that the walker
/// doesn't pre-format. The walker exposes structured bits
/// (<see cref="ApiObjectType.Kind"/>, modifier flags,
/// <see cref="ApiType.BaseType"/>, <see cref="ApiType.Interfaces"/>);
/// this helper composes them into a single string the YAML emitter
/// can pass straight through <see cref="DocfxYamlBuilderExtensions.AppendSyntaxContent"/>.
/// </summary>
internal static class DocfxObjectSignature
{
    /// <summary>
    /// Builds the declaration line for an object-shaped type. Result
    /// shape: <c>{modifiers} {kind} {Name}{ : Base, IFace1, IFace2 }</c>.
    /// One <see cref="StringBuilder"/> allocation; final
    /// <see cref="StringBuilder.ToString()"/> is the only string alloc.
    /// </summary>
    /// <param name="type">Object-shaped type whose signature to render.</param>
    /// <returns>The synthesised C# declaration line.</returns>
    internal static string Synthesise(ApiObjectType type)
    {
        var modifiers = ModifierKeywords(type);
        var kindKeyword = KindKeyword(type.Kind);
        var name = type.Name;

        var hasBase = type.BaseType is not null && !IsImplicitObject(type);
        var hasInterfaces = type.Interfaces is [_, ..];

        var sb = new StringBuilder(modifiers.Length + 1 + kindKeyword.Length + 1 + name.Length + 64);
        if (modifiers is [_, ..])
        {
            sb.Append(modifiers).Append(' ');
        }

        sb.Append(kindKeyword).Append(' ').Append(name);

        if (!hasBase && !hasInterfaces)
        {
            return sb.ToString();
        }

        sb.Append(" : ");
        var first = true;
        if (hasBase && type.BaseType is { } baseRef)
        {
            sb.Append(baseRef.DisplayName);
            first = false;
        }

        if (!hasInterfaces)
        {
            return sb.ToString();
        }

        for (var i = 0; i < type.Interfaces.Length; i++)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            sb.Append(type.Interfaces[i].DisplayName);
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the C# keyword for an <see cref="ApiObjectKind"/> —
    /// <c>class</c>, <c>struct</c>, <c>interface</c>, <c>record</c>,
    /// or <c>record struct</c>.
    /// </summary>
    /// <param name="kind">Kind to render.</param>
    /// <returns>The keyword sequence.</returns>
    internal static string KindKeyword(ApiObjectKind kind) => kind switch
    {
        ApiObjectKind.Class => "class",
        ApiObjectKind.Struct => "struct",
        ApiObjectKind.Interface => "interface",
        ApiObjectKind.Record => "record",
        ApiObjectKind.RecordStruct => "record struct",
        _ => "class",
    };

    /// <summary>
    /// Joins the type-level access + lifecycle modifiers in the order
    /// docfx emits them: <c>public</c>, then <c>static</c> /
    /// <c>abstract sealed</c> / <c>abstract</c> / <c>sealed</c> /
    /// <c>readonly</c> / <c>ref</c>. Every walked type is at minimum
    /// <c>public</c>; visibility filtering happens upstream.
    /// </summary>
    /// <param name="type">Type whose modifiers to render.</param>
    /// <returns>Space-separated modifier sequence; <c>"public"</c> at minimum.</returns>
    internal static string ModifierKeywords(ApiObjectType type) => (type.IsStatic, type.IsAbstract, type.IsSealed, type.IsByRefLike, type.IsReadOnly, type.Kind) switch
    {
        (true, _, _, _, _, _) => "public static",
        (_, true, true, _, _, _) => "public abstract sealed",
        (_, true, _, _, _, _) => "public abstract",
        (_, _, _, true, true, _) => "public readonly ref",
        (_, _, _, true, _, _) => "public ref",
        (_, _, _, _, true, _) => "public readonly",
        (_, _, true, _, _, ApiObjectKind.Class or ApiObjectKind.Record) => "public sealed",
        _ => "public",
    };

    /// <summary>
    /// Returns true when the type's <see cref="ApiType.BaseType"/>
    /// would just restate the implicit BCL base — <c>System.Object</c>
    /// for classes, <c>System.ValueType</c> for structs. The walker
    /// already filters those out (returning null), but the predicate
    /// is here so a future model change that surfaces them doesn't
    /// pollute the rendered declaration line.
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <returns>True when the base reference is the implicit BCL one.</returns>
    internal static bool IsImplicitObject(ApiObjectType type) =>
        type.BaseType is { Uid: var uid } && uid is "T:System.Object" or "T:System.ValueType";
}
