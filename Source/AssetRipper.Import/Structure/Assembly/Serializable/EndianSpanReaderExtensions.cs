using AssetRipper.IO.Endian;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

internal static class EndianSpanReaderExtensions
{
	public static T[] ReadPrimitiveArray<T>(this ref EndianSpanReader reader, UnityVersion version) where T : unmanaged
	{
		int count = reader.ReadInt32();
		int index = 0;
		ThrowIfNegativeCount(count);
		ThrowIfNotEnoughSpaceForArray(ref reader, count, Unsafe.SizeOf<T>());
		T[] array = count == 0 ? [] : new T[count];
		while (index < count)
		{
			try
			{
				array[index] = reader.ReadPrimitive<T>();
			}
			catch (Exception ex)
			{
				throw new EndOfStreamException($"End of stream. Read {index}, expected {count} elements", ex);
			}
			index++;
		}
		if (IsAlignArrays(version))
		{
			reader.Align();
		}
		return array;
	}

	public static T[][] ReadPrimitiveArrayArray<T>(this ref EndianSpanReader reader, UnityVersion version) where T : unmanaged
	{
		int count = reader.ReadInt32();
		int index = 0;
		ThrowIfNegativeCount(count);
		ThrowIfNotEnoughSpaceForArray(ref reader, count, sizeof(int));
		T[][] array = count == 0 ? [] : new T[count][];
		while (index < count)
		{
			try
			{
				array[index] = reader.ReadPrimitiveArray<T>(version);
			}
			catch (Exception ex)
			{
				throw new EndOfStreamException($"End of stream. Read {index}, expected {count} elements", ex);
			}
			index++;
		}
		if (IsAlignArrays(version))
		{
			reader.Align();
		}
		return array;
	}

	public static Utf8String ReadUtf8StringAligned(this ref EndianSpanReader reader)
	{
		Utf8String result = reader.ReadUtf8String();
		reader.Align();//Alignment after strings has happened since 2.1.0
		return result;
	}

	public static string[] ReadStringArray(this ref EndianSpanReader reader, UnityVersion version)
	{
		int count = reader.ReadInt32();
		int index = 0;
		ThrowIfNegativeCount(count);
		ThrowIfNotEnoughSpaceForArray(ref reader, count, sizeof(int));
		string[] array = count == 0 ? [] : new string[count];
		while (index < count)
		{
			try
			{
				array[index] = reader.ReadUtf8StringAligned().String;
			}
			catch (Exception ex)
			{
				throw new EndOfStreamException($"End of stream. Read {index}, expected {count} elements", ex);
			}
			index++;
		}
		if (IsAlignArrays(version))
		{
			reader.Align();
		}
		return array;
	}

	public static string[][] ReadStringArrayArray(this ref EndianSpanReader reader, UnityVersion version)
	{
		int count = reader.ReadInt32();
		int index = 0;
		ThrowIfNegativeCount(count);
		ThrowIfNotEnoughSpaceForArray(ref reader, count, sizeof(int));
		string[][] array = count == 0 ? [] : new string[count][];
		while (index < count)
		{
			try
			{
				array[index] = reader.ReadStringArray(version);
			}
			catch (Exception ex)
			{
				throw new EndOfStreamException($"End of stream. Read {index}, expected {count} elements", ex);
			}
			index++;
		}
		if (IsAlignArrays(version))
		{
			reader.Align();
		}
		return array;
	}

	public static bool[] ReadBooleanArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<bool>(ref reader, version);
	public static char[] ReadCharArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<char>(ref reader, version);
	public static sbyte[] ReadSByteArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<sbyte>(ref reader, version);
	public static byte[] ReadByteArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<byte>(ref reader, version);
	public static short[] ReadInt16Array(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<short>(ref reader, version);
	public static ushort[] ReadUInt16Array(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<ushort>(ref reader, version);
	public static int[] ReadInt32Array(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<int>(ref reader, version);
	public static uint[] ReadUInt32Array(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<uint>(ref reader, version);
	public static long[] ReadInt64Array(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<long>(ref reader, version);
	public static ulong[] ReadUInt64Array(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<ulong>(ref reader, version);
	public static float[] ReadSingleArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<float>(ref reader, version);
	public static double[] ReadDoubleArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArray<double>(ref reader, version);

	public static bool[][] ReadBooleanArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<bool>(ref reader, version);
	public static char[][] ReadCharArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<char>(ref reader, version);
	public static sbyte[][] ReadSByteArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<sbyte>(ref reader, version);
	public static byte[][] ReadByteArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<byte>(ref reader, version);
	public static short[][] ReadInt16ArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<short>(ref reader, version);
	public static ushort[][] ReadUInt16ArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<ushort>(ref reader, version);
	public static int[][] ReadInt32ArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<int>(ref reader, version);
	public static uint[][] ReadUInt32ArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<uint>(ref reader, version);
	public static long[][] ReadInt64ArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<long>(ref reader, version);
	public static ulong[][] ReadUInt64ArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<ulong>(ref reader, version);
	public static float[][] ReadSingleArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<float>(ref reader, version);
	public static double[][] ReadDoubleArrayArray(this ref EndianSpanReader reader, UnityVersion version) => ReadPrimitiveArrayArray<double>(ref reader, version);

	private static bool IsAlignArrays(UnityVersion version) => version.GreaterThanOrEquals(2017);

	[DebuggerHidden]
	private static void ThrowIfNegativeCount(int count)
	{
		if (count < 0)
		{
			throw new InvalidDataException($"Count cannot be negative: {count}");
		}
	}

	[DebuggerHidden]
	private static void ThrowIfNotEnoughSpaceForArray(ref EndianSpanReader reader, int elementNumberToRead, int elementSize)
	{
		int remainingBytes = reader.Length - reader.Position;
		if (remainingBytes < (long)elementNumberToRead * elementSize)
		{
			throw new EndOfStreamException($"Stream only has {remainingBytes} bytes in the stream, so {elementNumberToRead} elements of size {elementSize} cannot be read.");
		}
	}
}
