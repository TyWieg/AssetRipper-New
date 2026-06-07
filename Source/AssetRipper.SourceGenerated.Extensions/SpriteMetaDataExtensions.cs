using AssetRipper.Assets.Generics;
using AssetRipper.Numerics;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_687078895;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Subclasses.SpriteAtlasData;
using AssetRipper.SourceGenerated.Subclasses.SpriteBone;
using AssetRipper.SourceGenerated.Subclasses.SpriteMetaData;
using AssetRipper.SourceGenerated.Subclasses.SpriteRenderData;
using AssetRipper.SourceGenerated.Subclasses.SpriteVertex;
using AssetRipper.SourceGenerated.Subclasses.SubMesh;
using AssetRipper.SourceGenerated.Subclasses.Vector2f;
using AssetRipper.SourceGenerated.Subclasses.Vector2Int;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace AssetRipper.SourceGenerated.Extensions;

public static class SpriteMetaDataExtensions
{
	public static SpriteAlignment GetAlignment(this ISpriteMetaData data)
	{
		return (SpriteAlignment)data.Alignment;
	}

	public static void FillSpriteMetaData(this ISpriteMetaData instance, ISprite sprite, ISpriteAtlas? atlas)
	{
		sprite.GetSpriteCoordinatesInAtlas(atlas, out RectangleF rect, out Vector2 pivot, out Vector4 border);

		instance.Name = sprite.Name;
		instance.Rect.CopyValues(rect);
		instance.Alignment = (int)SpriteAlignment.Custom;
		instance.Pivot.CopyValues(pivot);
		instance.Border?.CopyValues(border);

		if (instance.Has_Outline())
		{
			GenerateOutline(sprite, atlas, rect, pivot, instance.Outline);
		}

		if (instance.Has_PhysicsShape() && sprite.Has_PhysicsShape())
		{
			GeneratePhysicsShape(sprite, atlas, rect, pivot, instance.PhysicsShape);
		}

		if (instance.Has_Edges() && sprite.RD != null && sprite.RD.Has_Vertices())
		{
			GenerateEdges(sprite, atlas, rect, pivot, instance.Edges);
		}

		instance.TessellationDetail = 0;

		if (instance.Has_Bones() && sprite.Has_Bones() && instance.Has_SpriteID())
		{
			instance.Bones.Clear();
			instance.Bones.Capacity = sprite.Bones.Count;

			float halfWidth = sprite.Rect.Width / 2f;
			float halfHeight = sprite.Rect.Height / 2f;

			foreach (ISpriteBone bone in sprite.Bones)
			{
				ISpriteBone newBone = instance.Bones.AddNew();
				newBone.CopyValues(bone);

				if (newBone.Position is not null)
				{
					newBone.Position.Scale(sprite.PixelsToUnits);
					if (newBone.ParentId == -1)
					{
						newBone.Position.X += halfWidth;
						newBone.Position.Y += halfHeight;
					}
				}
				newBone.Length *= sprite.PixelsToUnits;
			}

			instance.SpriteID = Guid.NewGuid().ToString("N");

			instance.SetBoneGeometry(sprite);
		}
	}

	private static void SetBoneGeometry(this ISpriteMetaData instance, ISprite origin)
	{
		Vector3[]? vertices = null;
		BoneWeight4[]? skin = null;

		if (origin.RD != null && origin.RD.Has_VertexData())
		{
			VertexDataBlob.Create(origin.RD.VertexData, origin.Collection.Version, origin.Collection.EndianType).ReadData(
				out vertices,
				out Vector3[]? _,
				out Vector4[]? _,
				out ColorFloat[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out Vector2[]? _,
				out skin);
		}

		if (instance.Has_Vertices())
		{
			instance.Vertices.Clear();

			if (vertices is null)
			{
				instance.Vertices.Capacity = 0;
			}
			else
			{
				float halfWidth = origin.Rect.Width / 2f;
				float halfHeight = origin.Rect.Height / 2f;

				instance.Vertices.Capacity = vertices.Length;
				for (int i = 0; i < vertices.Length; i++)
				{
					Vector2f vertex = instance.Vertices.AddNew();
					vertex.X = vertices[i].X * origin.PixelsToUnits + halfWidth;
					vertex.Y = vertices[i].Y * origin.PixelsToUnits + halfHeight;
				}
			}
		}

		if (instance.Has_Indices())
		{
			instance.Indices.Clear();
			if (origin.RD != null && origin.RD.Has_IndexBuffer())
			{
				ReadOnlySpan<byte> indexBuffer = origin.RD.IndexBuffer;
				if (indexBuffer.Length != 0)
				{
					int indexCount = indexBuffer.Length / sizeof(short);
					instance.Indices.Capacity = indexCount;

					for (int i = 0; i < indexCount; i++)
					{
						instance.Indices.Add(
							BinaryPrimitives.ReadInt16LittleEndian(
								indexBuffer.Slice(i * sizeof(short), sizeof(short))));
					}
				}
			}
		}

		if (instance.Has_Weights())
		{
			instance.Weights.Clear();
			if (skin is not null)
			{
				instance.Weights.EnsureCapacity(skin.Length);
				for (int i = 0; i < skin.Length; i++)
				{
					instance.Weights.AddNew().CopyValues(skin[i]);
				}
			}
		}
	}

	private static void GeneratePhysicsShape(
		ISprite sprite,
		ISpriteAtlas? atlas,
		RectangleF rect,
		Vector2 pivot,
		AssetList<AssetList<Vector2f>> shape)
	{
		if (shape is null || !sprite.Has_PhysicsShape() || sprite.PhysicsShape.Count == 0)
		{
			return;
		}

		shape.Clear();
		shape.Capacity = sprite.PhysicsShape.Count;

		Vector2 pivotShift = ComputePivotShift(rect, pivot);

		for (int i = 0; i < sprite.PhysicsShape.Count; i++)
		{
			AssetList<Vector2f> sourceList = sprite.PhysicsShape[i];
			AssetList<Vector2f> targetList = shape.AddNew();
			targetList.Capacity = sourceList.Count;
			for (int j = 0; j < sourceList.Count; j++)
			{
				Vector2 point = (Vector2)sourceList[j] * sprite.PixelsToUnits;
				targetList.AddNew().CopyValues(point + pivotShift);
			}
		}
		shape.FixRotation(sprite, atlas);
	}

	private static void GenerateEdges(
		ISprite sprite,
		ISpriteAtlas? atlas,
		RectangleF rect,
		Vector2 pivot,
		AssetList<Vector2Int> edges)
	{
		if (edges is null || sprite.RD is null)
		{
			return;
		}

		AssetList<AssetList<Vector2f>> outlines = new();
		GenerateOutline(sprite.RD, sprite.Collection.Version, outlines);

		int totalPoints = 0;
		foreach (AssetList<Vector2f> outline in outlines)
		{
			totalPoints += outline.Count;
		}

		edges.Clear();
		edges.Capacity = totalPoints;

		Vector2 pivotShift = ComputePivotShift(rect, pivot);

		foreach (AssetList<Vector2f> outline in outlines)
		{
			for (int i = 0; i < outline.Count; i++)
			{
				Vector2 point = (Vector2)outline[i] * sprite.PixelsToUnits + pivotShift;
				Vector2Int edge = edges.AddNew();
				edge.X = (int)point.X;
				edge.Y = (int)point.Y;
			}
		}

		GetPacking(sprite, atlas, out bool isPacked, out SpritePackingRotation rotation);
		if (!isPacked)
		{
			return;
		}

		switch (rotation)
		{
			case SpritePackingRotation.FlipHorizontal:
				for (int i = 0; i < edges.Count; i++)
				{
					edges[i].X = -edges[i].X;
				}
				break;

			case SpritePackingRotation.FlipVertical:
				for (int i = 0; i < edges.Count; i++)
				{
					edges[i].Y = -edges[i].Y;
				}
				break;

			case SpritePackingRotation.Rotate90:
				for (int i = 0; i < edges.Count; i++)
				{
					Vector2Int edge = edges[i];
					int tmp = edge.X;
					edge.X = edge.Y;
					edge.Y = tmp;
				}
				break;

			case SpritePackingRotation.Rotate180:
				for (int i = 0; i < edges.Count; i++)
				{
					edges[i].X = -edges[i].X;
					edges[i].Y = -edges[i].Y;
				}
				break;
		}
	}

	private static void FixRotation(this AssetList<AssetList<Vector2f>> outlines, ISprite sprite, ISpriteAtlas? atlas)
	{
		GetPacking(sprite, atlas, out bool isPacked, out SpritePackingRotation rotation);

		if (!isPacked)
		{
			return;
		}

		switch (rotation)
		{
			case SpritePackingRotation.FlipHorizontal:
				foreach (AssetList<Vector2f> outline in outlines)
				{
					for (int i = 0; i < outline.Count; i++)
					{
						Vector2f vertex = outline[i];
						outline[i].SetValues(-vertex.X, vertex.Y);
					}
				}
				break;

			case SpritePackingRotation.FlipVertical:
				foreach (AssetList<Vector2f> outline in outlines)
				{
					for (int i = 0; i < outline.Count; i++)
					{
						Vector2f vertex = outline[i];
						outline[i].SetValues(vertex.X, -vertex.Y);
					}
				}
				break;

			case SpritePackingRotation.Rotate90:
				foreach (AssetList<Vector2f> outline in outlines)
				{
					for (int i = 0; i < outline.Count; i++)
					{
						outline[i].SetValues(outline[i].Y, outline[i].X);
					}
				}
				break;

			case SpritePackingRotation.Rotate180:
				foreach (AssetList<Vector2f> outline in outlines)
				{
					for (int i = 0; i < outline.Count; i++)
					{
						Vector2f vertex = outline[i];
						outline[i].SetValues(-vertex.X, -vertex.Y);
					}
				}
				break;
		}
	}

	private static void GetPacking(
		ISprite sprite,
		ISpriteAtlas? atlas,
		out bool isPacked,
		out SpritePackingRotation rotation)
	{
		if (atlas is not null
			&& sprite.Has_RenderDataKey()
			&& atlas.RenderDataMap.TryGetValue(sprite.RenderDataKey, out ISpriteAtlasData? atlasData))
		{
			isPacked = atlasData.IsPacked;
			rotation = atlasData.PackingRotation;
		}
		else if (sprite.RD is not null)
		{
			isPacked = sprite.RD.IsPacked;
			rotation = sprite.RD.PackingRotation;
		}
		else
		{
			isPacked = false;
			rotation = SpritePackingRotation.None;
		}
	}

	private static void GenerateOutline(
		ISprite sprite,
		ISpriteAtlas? atlas,
		RectangleF rect,
		Vector2 pivot,
		AssetList<AssetList<Vector2f>> outlines)
	{
		if (outlines is null || sprite.RD is null)
		{
			return;
		}

		GenerateOutline(sprite.RD, sprite.Collection.Version, outlines);
		Vector2 pivotShift = ComputePivotShift(rect, pivot);

		foreach (AssetList<Vector2f> outline in outlines)
		{
			for (int i = 0; i < outline.Count; i++)
			{
				Vector2 point = (Vector2)outline[i] * sprite.PixelsToUnits;
				outline[i].CopyValues(point + pivotShift);
			}
		}

		outlines.FixRotation(sprite, atlas);
	}

	private static void GenerateOutline(
		ISpriteRenderData spriteRenderData,
		UnityVersion version,
		AssetList<AssetList<Vector2f>> outlines)
	{
		if (outlines is null)
		{
			return;
		}

		outlines.Clear();
		if (spriteRenderData.Has_VertexData()
			&& spriteRenderData.SubMeshes is not null
			&& spriteRenderData.SubMeshes.Count != 0)
		{
			for (int i = 0; i < spriteRenderData.SubMeshes.Count; i++)
			{
				Vector3[] vertices = spriteRenderData.VertexData.GenerateVertices(version, spriteRenderData.SubMeshes[i]);
				List<Vector2[]> vectorArrayList = VertexDataToOutline(spriteRenderData.IndexBuffer, vertices, spriteRenderData.SubMeshes[i]);
				outlines.AddRanges(vectorArrayList);
			}
		}
		else if (spriteRenderData.Has_Vertices() && spriteRenderData.Vertices.Count != 0)
		{
			List<Vector2[]> vectorArrayList = VerticesToOutline(spriteRenderData.Vertices, spriteRenderData.Indices);
			outlines.Capacity = vectorArrayList.Count;
			outlines.AddRanges(vectorArrayList);
		}
	}

	private static List<Vector2[]> VerticesToOutline(
		AccessListBase<ISpriteVertex> spriteVertexList,
		AssetList<ushort> spriteIndexArray)
	{
		Vector3[] vertices = new Vector3[spriteVertexList.Count];
		for (int i = 0; i < vertices.Length; i++)
		{
			vertices[i] = spriteVertexList[i].Pos;
		}

		Vector3i[] triangles = new Vector3i[spriteIndexArray.Count / 3];
		for (int i = 0, j = 0; i < triangles.Length; i++)
		{
			int x = spriteIndexArray[j];
			int y = spriteIndexArray[j + 1];
			int z = spriteIndexArray[j + 2];
			j += 3;

			triangles[i] = new Vector3i(x, y, z);
		}

		return new MeshOutlineGenerator(vertices, triangles).GenerateOutlines();
	}

	private static List<Vector2[]> VertexDataToOutline(ReadOnlySpan<byte> indexBuffer, Vector3[] vertices, ISubMesh submesh)
	{
		Vector3i[] triangles = new Vector3i[submesh.IndexCount / 3];
		for (int o = (int)submesh.FirstByte, ti = 0; ti < triangles.Length; o += 3 * sizeof(ushort), ti++)
		{
			ushort x = BinaryPrimitives.ReadUInt16LittleEndian(indexBuffer.Slice(o, sizeof(ushort)));
			ushort y = BinaryPrimitives.ReadUInt16LittleEndian(indexBuffer.Slice(o + sizeof(ushort), sizeof(ushort)));
			ushort z = BinaryPrimitives.ReadUInt16LittleEndian(indexBuffer.Slice(o + 2 * sizeof(ushort), sizeof(ushort)));
			triangles[ti] = new Vector3i(x, y, z);
		}

		return new MeshOutlineGenerator(vertices, triangles).GenerateOutlines();
	}

	private static void AddRanges(this AssetList<AssetList<Vector2f>> instance, List<Vector2[]> vectorArrayList)
	{
		foreach (Vector2[] vectorArray in vectorArrayList)
		{
			AssetList<Vector2f> assetList = instance.AddNew();
			assetList.Capacity = vectorArray.Length;
			foreach (Vector2 v in vectorArray)
			{
				assetList.AddNew().CopyValues(v);
			}
		}
	}

	private static Vector2 ComputePivotShift(RectangleF rect, Vector2 pivot)
	{
		return new Vector2(
			rect.Width * (pivot.X - 0.5f),
			rect.Height * (pivot.Y - 0.5f));
	}
}