using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.Primitives;
using AssetRipper.SerializationLogic.Extensions;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace AssetRipper.SerializationLogic;

public readonly partial struct FieldSerializer
{
	private bool IsStructSerializable => version.GreaterThanOrEquals(4, 5);
	private bool IsInt8Serializable => IsInt16Serializable;
	private bool IsInt16Serializable => version.GreaterThanOrEquals(5);
	private bool IsUInt32Serializable => IsInt16Serializable;
	private bool IsCharSerializable => IsInt64Serializable;
	private bool IsInt64Serializable => version.GreaterThanOrEquals(2017);
	private bool IsGenericInstanceSerializable => version.GreaterThanOrEquals(2020);

	private bool WillUnitySerialize(FieldDefinition fieldDefinition, TypeSignature fieldType)
	{
		if (fieldDefinition == null)
		{
			return false;
		}

		if (fieldDefinition.IsStatic || fieldDefinition.IsConst() || fieldDefinition.IsNotSerialized || fieldDefinition.IsInitOnly)
		{
			return false;
		}

		if (!fieldDefinition.IsPublic &&
			!ShouldHaveHadAllFieldsPublic(fieldDefinition) &&
			!fieldDefinition.HasSerializeFieldAttribute() &&
			!fieldDefinition.HasSerializeReferenceAttribute())
		{
			return false;
		}

		if (ShouldNotTryToResolve(fieldDefinition.Signature!.FieldType))
		{
			return false;
		}

		if (fieldDefinition.HasFixedBufferAttribute())
		{
			return true;
		}

		if (fieldType is CustomModifierTypeSignature customModifierType)
		{
			fieldType = customModifierType.BaseType;
		}

		if (fieldType is CorLibTypeSignature corLibTypeSignature && corLibTypeSignature.ElementType == ElementType.String)
		{
			return true;
		}

		if (fieldType.IsValueType)
		{
			return IsValueTypeSerializable(fieldType);
		}

		if (fieldType is SzArrayTypeSignature || AsmUtils.IsGenericList(fieldType))
		{
			if (!fieldDefinition.HasSerializeReferenceAttribute())
			{
				return IsSupportedCollection(fieldType);
			}
		}

		if (!IsReferenceTypeSerializable(fieldType) && !fieldDefinition.HasSerializeReferenceAttribute())
		{
			return false;
		}

		if (IsDelegate(fieldType))
		{
			return false;
		}

		return true;
	}

	private static bool IsDelegate(ITypeDescriptor typeReference)
	{
		return typeReference.IsAssignableTo("System", "Delegate");
	}

	private static IEnumerable<FieldDefinition> AllFieldsFor(TypeDefinition definition)
	{
		TypeDefinition? baseType = definition.BaseType?.Resolve();

		if (baseType != null)
		{
			foreach (FieldDefinition baseField in AllFieldsFor(baseType))
			{
				yield return baseField;
			}
		}

		foreach (FieldDefinition field in definition.Fields)
		{
			yield return field;
		}
	}

	private static bool ShouldNotTryToResolve(ITypeDescriptor typeReference)
	{
		if (typeReference is TypeDefinition)
		{
			return false;
		}

		string? typeReferenceScopeName = typeReference.Scope?.Name;
		if (typeReferenceScopeName == "Windows")
		{
			return true;
		}

		if (typeReferenceScopeName == "mscorlib")
		{
			TypeDefinition? resolved = typeReference.Resolve();
			return resolved == null;
		}

		try
		{
			typeReference.Resolve();
		}
		catch
		{
			return true;
		}

		return false;
	}

	private bool IsValueTypeSerializable(TypeSignature typeReference)
	{
		if (typeReference.IsPrimitive())
		{
			return IsSerializablePrimitive((CorLibTypeSignature)typeReference);
		}

		if (typeReference.IsEnum())
		{
			TypeDefinition typeDefinition = typeReference.CheckedResolve();
			CorLibTypeSignature underlyingType = (CorLibTypeSignature)typeDefinition.GetEnumUnderlyingType()!;
			return IsSerializablePrimitive(underlyingType);
		}
		else
		{
			return EngineTypePredicates.IsSerializableUnityStruct(typeReference) || ShouldImplementIDeserializable(typeReference);
		}
	}

	private bool IsReferenceTypeSerializable(TypeSignature fieldType)
	{
		if (fieldType is CorLibTypeSignature { ElementType: ElementType.String } corLibTypeSignature)
		{
			return IsSerializablePrimitive(corLibTypeSignature);
		}

		if (AsmUtils.IsGenericDictionary(fieldType))
		{
			return false;
		}

		if (EngineTypePredicates.IsUnityEngineObject(fieldType) || EngineTypePredicates.IsSerializableUnityClass(fieldType) || ShouldImplementIDeserializable(fieldType))
		{
			return true;
		}

		return false;
	}

	private bool IsTypeSerializable(TypeSignature fieldType)
	{
		if (fieldType is CorLibTypeSignature { ElementType: ElementType.String })
		{
			return true;
		}

		if (fieldType.IsValueType)
		{
			return IsValueTypeSerializable(fieldType);
		}

		return IsReferenceTypeSerializable(fieldType);
	}

	private bool IsSerializablePrimitive(CorLibTypeSignature typeReference)
	{
		return typeReference.ElementType switch
		{
			ElementType.I1 => IsInt8Serializable,
			ElementType.I2 or ElementType.U2 => IsInt16Serializable,
			ElementType.U4 => IsUInt32Serializable,
			ElementType.I8 or ElementType.U8 => IsInt64Serializable,
			ElementType.Char => IsCharSerializable,
			ElementType.Boolean or ElementType.U1 or ElementType.I4 or ElementType.R4 or ElementType.R8 or ElementType.String => true,
			_ => false,
		};
	}

	private bool IsSupportedCollection(TypeSignature fieldType)
	{
		if (fieldType is SzArrayTypeSignature || AsmUtils.IsGenericList(fieldType))
		{
			return IsTypeSerializable(AsmUtils.ElementTypeOfCollection(fieldType));
		}

		return false;
	}

	private static bool ShouldHaveHadAllFieldsPublic(FieldDefinition field)
	{
		return field.DeclaringType is not null && EngineTypePredicates.IsUnityEngineValueType(field.DeclaringType);
	}

	private static bool IsNonSerialized([NotNullWhen(false)] ITypeDescriptor? typeDeclaration)
	{
		if (typeDeclaration == null)
		{
			return true;
		}

		if (typeDeclaration.ToTypeSignature() is GenericInstanceTypeSignature genericInstanceTypeSignature
			&& genericInstanceTypeSignature.TypeArguments.Any(t => t is GenericParameterSignature))
		{
			return true;
		}

		if (typeDeclaration.ToTypeSignature() is CorLibTypeSignature { ElementType: ElementType.Object })
		{
			return true;
		}

		if (typeDeclaration.IsArray())
		{
			return true;
		}

		if (typeDeclaration.IsEnum())
		{
			return true;
		}

		if (typeDeclaration is { Namespace: EngineTypePredicates.UnityEngineNamespace, Name: EngineTypePredicates.MonoBehaviour or EngineTypePredicates.ScriptableObject })
		{
			return true;
		}

		return typeDeclaration.Namespace == "System" || (typeDeclaration.Namespace?.StartsWith("System.", StringComparison.Ordinal) ?? false);
	}

	private bool ShouldImplementIDeserializable([NotNullWhen(true)] ITypeDescriptor? typeDeclaration)
	{
		if (typeDeclaration is { Namespace: EngineTypePredicates.UnityEngineNamespace, Name: "ExposedReference`1" })
		{
			return true;
		}

		if (IsNonSerialized(typeDeclaration))
		{
			return false;
		}

		if (EngineTypePredicates.ShouldHaveHadSerializableAttribute(typeDeclaration))
		{
			return true;
		}

		TypeDefinition resolvedTypeDeclaration = typeDeclaration.CheckedResolve();

		bool isSerializable = resolvedTypeDeclaration.IsSerializable;

		isSerializable &= !resolvedTypeDeclaration.IsAbstract;
		isSerializable &= !resolvedTypeDeclaration.IsInterface;
		isSerializable &= !resolvedTypeDeclaration.IsCompilerGenerated();
		isSerializable &= IsGenericInstanceSerializable || typeDeclaration.ToTypeSignature() is not GenericInstanceTypeSignature;

		if (typeDeclaration.IsValueType)
		{
			return isSerializable && IsStructSerializable;
		}

		return isSerializable || resolvedTypeDeclaration.InheritsFromMonoBehaviour() || resolvedTypeDeclaration.InheritsFromScriptableObject();
	}
}