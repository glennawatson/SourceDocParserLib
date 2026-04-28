// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SourceDocParser;

/// <summary>
/// Reads a managed DLL set with <see cref="System.Reflection.Metadata.MetadataReader"/>
/// and returns the set of public type UIDs in the same
/// <c>T:Namespace.Type</c> form Roslyn's
/// <see cref="Microsoft.CodeAnalysis.ISymbol.GetDocumentationCommentId"/>
/// produces. The probe avoids constructing a Roslyn compilation
/// graph, so it only pays for IL metadata token enumeration.
/// </summary>
internal static class PublicSurfaceProbe
{
    /// <summary>CommentId prefix for type UIDs.</summary>
    private const string TypeCommentIdPrefix = "T:";

    /// <summary>
    /// Returns the UIDs of every public (or nested-public) type
    /// declared in <paramref name="dllPaths"/>. Compiler-generated
    /// types (display classes, anonymous-type closures) are filtered
    /// out via the same heuristic the walker uses.
    /// </summary>
    /// <param name="dllPaths">Absolute paths to the DLLs to probe.</param>
    /// <returns>The probed UID set keyed on Roslyn-style <c>T:</c> commentIds.</returns>
    public static HashSet<string> ProbePublicTypeUids(IReadOnlyList<string> dllPaths)
    {
        ArgumentNullException.ThrowIfNull(dllPaths);
        var uids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < dllPaths.Count; i++)
        {
            ProbeOne(dllPaths[i], uids);
        }

        return uids;
    }

    /// <summary>Adds the public type UIDs found in one DLL to <paramref name="uids"/>; silently skips files that aren't managed assemblies.</summary>
    /// <param name="dllPath">Absolute path to the DLL.</param>
    /// <param name="uids">Destination set, accumulated across DLLs.</param>
    private static void ProbeOne(string dllPath, HashSet<string> uids)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return;
            }

            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                AppendIfPublic(reader, reader.GetTypeDefinition(handle), uids);
            }
        }
        catch (BadImageFormatException)
        {
            // Native or otherwise unreadable -- skip silently; the
            // caller's fallback to full Roslyn walk picks up anything
            // we miss here.
        }
        catch (IOException)
        {
            // File contention -- treat as unprobed; caller falls back.
        }
    }

    /// <summary>Adds the type's UID to <paramref name="uids"/> when it is public (or nested-public) and not compiler-generated.</summary>
    /// <param name="reader">Metadata reader scoped to the DLL.</param>
    /// <param name="type">Type definition row.</param>
    /// <param name="uids">Destination set.</param>
    private static void AppendIfPublic(MetadataReader reader, TypeDefinition type, HashSet<string> uids)
    {
        if (!IsPubliclyVisible(reader, type))
        {
            return;
        }

        var name = reader.GetString(type.Name);
        if (name.AsSpan().IndexOfAny('<', '>') >= 0)
        {
            return;
        }

        uids.Add(BuildTypeUid(reader, type));
    }

    /// <summary>Returns true when <paramref name="type"/> resolves to public visibility through every enclosing scope.</summary>
    /// <param name="reader">Metadata reader scoped to the DLL.</param>
    /// <param name="type">Type definition row.</param>
    /// <returns>True when the type is part of the public API surface.</returns>
    private static bool IsPubliclyVisible(MetadataReader reader, TypeDefinition type)
    {
        switch (type.Attributes & TypeAttributes.VisibilityMask)
        {
            case TypeAttributes.Public:
                return true;
            case TypeAttributes.NestedPublic:
                {
                    var declHandle = type.GetDeclaringType();
                    return !declHandle.IsNil
                        && IsPubliclyVisible(reader, reader.GetTypeDefinition(declHandle));
                }

            default:
                return false;
        }
    }

    /// <summary>Composes the Roslyn-style <c>T:</c> UID for <paramref name="type"/> in one exact-sized string allocation.</summary>
    /// <param name="reader">Metadata reader scoped to the DLL.</param>
    /// <param name="type">Type definition row.</param>
    /// <returns>The complete type UID.</returns>
    private static string BuildTypeUid(MetadataReader reader, TypeDefinition type) =>
        string.Create(
            TypeCommentIdPrefix.Length + GetFullNameLength(reader, type),
            (Reader: reader, Type: type),
            static (dest, state) =>
            {
                TypeCommentIdPrefix.AsSpan().CopyTo(dest);
                WriteFullName(dest[TypeCommentIdPrefix.Length..], state.Reader, state.Type);
            });

    /// <summary>Returns the character count of the dotted full name (Roslyn commentId form) for <paramref name="type"/>.</summary>
    /// <param name="reader">Metadata reader scoped to the DLL.</param>
    /// <param name="type">Type definition row.</param>
    /// <returns>The full-name length without the <c>T:</c> prefix.</returns>
    private static int GetFullNameLength(MetadataReader reader, TypeDefinition type)
    {
        var nameLength = reader.GetString(type.Name).Length;
        var declaringTypeHandle = type.GetDeclaringType();
        if (!declaringTypeHandle.IsNil)
        {
            return GetFullNameLength(reader, reader.GetTypeDefinition(declaringTypeHandle)) + 1 + nameLength;
        }

        return reader.GetString(type.Namespace) is [_, ..] ns
            ? ns.Length + 1 + nameLength
            : nameLength;
    }

    /// <summary>
    /// Composes the dotted full name (Roslyn commentId form) for
    /// <paramref name="type"/> by walking its declaring chain.
    /// </summary>
    /// <param name="destination">Destination span receiving the dotted full name.</param>
    /// <param name="reader">Metadata reader scoped to the DLL.</param>
    /// <param name="type">Type definition row.</param>
    /// <returns>The number of characters written.</returns>
    private static int WriteFullName(Span<char> destination, MetadataReader reader, TypeDefinition type)
    {
        var declaringTypeHandle = type.GetDeclaringType();
        var position = 0;
        if (!declaringTypeHandle.IsNil)
        {
            position = WriteFullName(destination, reader, reader.GetTypeDefinition(declaringTypeHandle));
            destination[position++] = '.';
        }
        else if (reader.GetString(type.Namespace) is [_, ..] ns)
        {
            ns.AsSpan().CopyTo(destination);
            position = ns.Length;
            destination[position++] = '.';
        }

        var name = reader.GetString(type.Name);
        name.AsSpan().CopyTo(destination[position..]);
        return position + name.Length;
    }
}
