// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SourceDocParser.Walk;

namespace SourceDocParser.LibCompilation;

/// <summary>Finds unavailable assemblies required by a documentation root's API.</summary>
public static class ApiReferenceInspector
{
    /// <summary>Returns unavailable references used by the assembly's documented signatures.</summary>
    /// <param name="assembly">The assembly being documented.</param>
    /// <param name="includePrivateMembers">Whether to inspect nonpublic declarations in the documentation root.</param>
    /// <returns>One entry per unavailable assembly, identifying a member that requires it.</returns>
    public static MissingApiReference[] GetMissingReferences(IAssemblySymbol assembly, bool includePrivateMembers)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var context = new InspectionContext(assembly, includePrivateMembers);
        InspectNamespace(assembly.GlobalNamespace, context);
        var forwarded = assembly.GetForwardedTypes();
        for (var i = 0; i < forwarded.Length; i++)
        {
            InspectSurface(forwarded[i], false, forwarded[i], context);
        }

        return [.. context.Missing.Values];
    }

    /// <summary>Inspects declarations owned by a documentation root's namespace.</summary>
    /// <param name="symbol">The namespace to inspect.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectNamespace(INamespaceSymbol symbol, InspectionContext context)
    {
        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol nested:
                {
                    InspectNamespace(nested, context);
                    break;
                }

                case INamedTypeSymbol type when IsVisible(type, context.IncludePrivateMembers):
                {
                    InspectSurface(type, false, type, context);
                    break;
                }
            }
        }
    }

    /// <summary>Inspects declared or inherited API members of a type.</summary>
    /// <param name="type">The type whose surface is required.</param>
    /// <param name="inherited">Whether only inherited members are required.</param>
    /// <param name="member">The API member requiring the type.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectSurface(INamedTypeSymbol type, bool inherited, ISymbol member, InspectionContext context)
    {
        InspectType(type, member, context);
        var visited = inherited ? context.InheritedTypes : context.DeclaredTypes;
        if (type is IErrorTypeSymbol || !visited.Add(type.OriginalDefinition))
        {
            return;
        }

        if (!inherited)
        {
            InspectAttributes(type.GetAttributes(), type, context);
        }

        var parameters = type.TypeParameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            InspectType(parameters[i], member, context);
        }

        InspectDeclaredMembers(type, inherited, context);
        if (type.BaseType is { } baseType)
        {
            InspectSurface(baseType, true, member, context);
        }

        var interfaces = type.Interfaces;
        for (var i = 0; i < interfaces.Length; i++)
        {
            InspectSurface(interfaces[i], true, member, context);
        }
    }

    /// <summary>Inspects members declared on a required type.</summary>
    /// <param name="type">The type declaring the members.</param>
    /// <param name="inherited">Whether only inherited members are required.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectDeclaredMembers(INamedTypeSymbol type, bool inherited, InspectionContext context)
    {
        var includePrivateMembers = !inherited && context.IncludePrivateMembers
            && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, context.RootAssembly);
        var members = type.GetMembers();
        for (var i = 0; i < members.Length; i++)
        {
            var candidate = members[i];
            if (!IsVisible(candidate, includePrivateMembers)
                || (inherited && IsConstructionMember(candidate)))
            {
                continue;
            }

            if (candidate is INamedTypeSymbol nested)
            {
                InspectSurface(nested, false, nested, context);
                continue;
            }

            InspectMember(candidate, context);
        }
    }

    /// <summary>Checks whether a declaration describes construction or finalization.</summary>
    /// <param name="symbol">The declaration to check.</param>
    /// <returns>Whether the declaration is excluded from inherited members.</returns>
    private static bool IsConstructionMember(ISymbol symbol) =>
        symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor };

    /// <summary>Checks whether a declaration belongs to the requested API surface.</summary>
    /// <param name="symbol">The declaration to check.</param>
    /// <param name="includePrivateMembers">Whether nonpublic declarations are included.</param>
    /// <returns>Whether the declaration should be inspected.</returns>
    private static bool IsVisible(ISymbol symbol, bool includePrivateMembers) =>
        includePrivateMembers || SymbolWalkerHelpers.IsExternallyVisible(symbol.DeclaredAccessibility);

    /// <summary>Inspects the types and attributes exposed by a member.</summary>
    /// <param name="member">The member to inspect.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectMember(ISymbol member, InspectionContext context)
    {
        InspectAttributes(member.GetAttributes(), member, context);
        switch (member)
        {
            case IFieldSymbol field:
            {
                InspectType(field.Type, member, context);
                InspectModifiers(field.CustomModifiers, member, context);
                break;
            }

            case IPropertySymbol property:
            {
                InspectType(property.Type, member, context);
                InspectModifiers(property.TypeCustomModifiers, member, context);
                InspectModifiers(property.RefCustomModifiers, member, context);
                InspectParameters(property.Parameters, member, context);
                break;
            }

            case IEventSymbol @event:
            {
                InspectType(@event.Type, member, context);
                break;
            }

            case IMethodSymbol method:
            {
                InspectMethod(method, member, context);
                break;
            }
        }
    }

    /// <summary>Inspects a method or function-pointer signature.</summary>
    /// <param name="method">The signature to inspect.</param>
    /// <param name="member">The API member exposing the signature.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectMethod(IMethodSymbol method, ISymbol member, InspectionContext context)
    {
        InspectType(method.ReturnType, member, context);
        InspectModifiers(method.ReturnTypeCustomModifiers, member, context);
        InspectModifiers(method.RefCustomModifiers, member, context);
        InspectAttributes(method.GetReturnTypeAttributes(), member, context);
        InspectParameters(method.Parameters, member, context);
        var parameters = method.TypeParameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            InspectType(parameters[i], member, context);
        }

        var conventions = method.UnmanagedCallingConventionTypes;
        for (var i = 0; i < conventions.Length; i++)
        {
            InspectType(conventions[i], member, context);
        }
    }

    /// <summary>Inspects parameters exposed by a signature.</summary>
    /// <param name="parameters">The parameters to inspect.</param>
    /// <param name="member">The API member exposing the parameters.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectParameters(ImmutableArray<IParameterSymbol> parameters, ISymbol member, InspectionContext context)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            InspectType(parameter.Type, member, context);
            InspectModifiers(parameter.CustomModifiers, member, context);
            InspectModifiers(parameter.RefCustomModifiers, member, context);
            InspectAttributes(parameter.GetAttributes(), member, context);
        }
    }

    /// <summary>Inspects a signature type without enumerating the type's members.</summary>
    /// <param name="type">The referenced type, if available.</param>
    /// <param name="member">The API member requiring the type.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectType(ITypeSymbol? type, ISymbol member, InspectionContext context)
    {
        if (type is null || !context.SignatureTypes.Add(type))
        {
            return;
        }

        if (type is IErrorTypeSymbol { ContainingAssembly.Identity: { } identity })
        {
            _ = context.Missing.TryAdd(identity, new(identity, member.ToDisplayString()));
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
            {
                InspectType(array.ElementType, member, context);
                InspectModifiers(array.CustomModifiers, member, context);
                break;
            }

            case IPointerTypeSymbol pointer:
            {
                InspectType(pointer.PointedAtType, member, context);
                InspectModifiers(pointer.CustomModifiers, member, context);
                break;
            }

            case IFunctionPointerTypeSymbol pointer:
            {
                InspectMethod(pointer.Signature, member, context);
                break;
            }

            case ITypeParameterSymbol parameter:
            {
                InspectAttributes(parameter.GetAttributes(), member, context);
                var constraints = parameter.ConstraintTypes;
                for (var i = 0; i < constraints.Length; i++)
                {
                    InspectType(constraints[i], member, context);
                }

                break;
            }

            case INamedTypeSymbol named:
            {
                InspectType(named.ContainingType, member, context);
                var arguments = named.TypeArguments;
                for (var i = 0; i < arguments.Length; i++)
                {
                    InspectType(arguments[i], member, context);
                }

                break;
            }
        }
    }

    /// <summary>Inspects custom modifiers that contribute to a signature.</summary>
    /// <param name="modifiers">The signature modifiers.</param>
    /// <param name="member">The API member exposing the modifiers.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectModifiers(ImmutableArray<CustomModifier> modifiers, ISymbol member, InspectionContext context)
    {
        for (var i = 0; i < modifiers.Length; i++)
        {
            InspectType(modifiers[i].Modifier, member, context);
        }
    }

    /// <summary>Inspects attribute types and arguments attached to an API declaration.</summary>
    /// <param name="attributes">The declaration's attributes.</param>
    /// <param name="member">The declaration requiring the attributes.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectAttributes(ImmutableArray<AttributeData> attributes, ISymbol member, InspectionContext context)
    {
        for (var i = 0; i < attributes.Length; i++)
        {
            var attribute = attributes[i];
            InspectType(attribute.AttributeClass, member, context);
            if (attribute.AttributeConstructor is { } constructor)
            {
                var parameters = constructor.Parameters;
                for (var j = 0; j < parameters.Length; j++)
                {
                    InspectType(parameters[j].Type, member, context);
                    InspectModifiers(parameters[j].CustomModifiers, member, context);
                }
            }

            var arguments = attribute.ConstructorArguments;
            for (var j = 0; j < arguments.Length; j++)
            {
                InspectConstant(arguments[j], member, context);
            }

            var namedArguments = attribute.NamedArguments;
            for (var j = 0; j < namedArguments.Length; j++)
            {
                InspectConstant(namedArguments[j].Value, member, context);
            }
        }
    }

    /// <summary>Inspects types encoded in an attribute argument.</summary>
    /// <param name="constant">The attribute argument.</param>
    /// <param name="member">The API member requiring the argument.</param>
    /// <param name="context">The current inspection.</param>
    private static void InspectConstant(TypedConstant constant, ISymbol member, InspectionContext context)
    {
        InspectType(constant.Type, member, context);
        if (constant.IsNull)
        {
            return;
        }

        if (constant.Kind is TypedConstantKind.Array)
        {
            var values = constant.Values;
            for (var i = 0; i < values.Length; i++)
            {
                InspectConstant(values[i], member, context);
            }

            return;
        }

        if (constant.Kind is TypedConstantKind.Type && constant.Value is ITypeSymbol type)
        {
            InspectType(type, member, context);
        }
    }

    /// <summary>Tracks symbols inspected for a single documentation root.</summary>
    /// <param name="rootAssembly">The assembly whose declarations are being documented.</param>
    /// <param name="includePrivateMembers">Whether nonpublic declarations are included.</param>
    private sealed class InspectionContext(IAssemblySymbol rootAssembly, bool includePrivateMembers)
    {
        /// <summary>Initial capacity for the references and symbols found during inspection.</summary>
        private const int InitialCapacity = 16;

        /// <summary>Gets the assembly whose nonpublic declarations may be inspected.</summary>
        public IAssemblySymbol RootAssembly { get; } = rootAssembly;

        /// <summary>Gets whether nonpublic root declarations are included.</summary>
        public bool IncludePrivateMembers { get; } = includePrivateMembers;

        /// <summary>Gets the signature types whose references have been inspected.</summary>
        public HashSet<ITypeSymbol> SignatureTypes { get; } = [with(InitialCapacity, SymbolEqualityComparer.Default)];

        /// <summary>Gets the types whose declared members have been inspected.</summary>
        public HashSet<INamedTypeSymbol> DeclaredTypes { get; } = [with(InitialCapacity, SymbolEqualityComparer.Default)];

        /// <summary>Gets the types whose inherited members have been inspected.</summary>
        public HashSet<INamedTypeSymbol> InheritedTypes { get; } = [with(InitialCapacity, SymbolEqualityComparer.Default)];

        /// <summary>Gets the unavailable assemblies and the API members requiring them.</summary>
        public Dictionary<AssemblyIdentity, MissingApiReference> Missing { get; } = [with(InitialCapacity)];
    }
}
