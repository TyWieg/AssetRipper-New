using AssetRipper.SourceGenerated.Subclasses.SerializedShaderFloatValue;

namespace AssetRipper.SourceGenerated.Extensions;

public static class SerializedShaderFloatValueExtensions
{
	public static bool IsZero(this ISerializedShaderFloatValue floatValue) => floatValue.Value == 0.0f;
	
	public static bool IsMax(this ISerializedShaderFloatValue floatValue) => floatValue.Value == 255.0f;

	public static Utf8String GetName(this ISerializedShaderFloatValue floatValue)
	{
		return floatValue.Has_Name_R_Utf8String() ? floatValue.Name_R_Utf8String : floatValue.Name_R_FastPropertyName.Name;
	}

	public static void SetName(this ISerializedShaderFloatValue floatValue, Utf8String value)
	{
		if (floatValue.Has_Name_R_Utf8String())
		{
			floatValue.Name_R_Utf8String = value;
		}
		else
		{
			floatValue.Name_R_FastPropertyName.Name = value;
		}
	}
}