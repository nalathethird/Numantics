// Elements.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// Elements.Core.Transform
using System;
using Elements.Core;
using Elements.Data;
using Renderite.Shared;

[DataModelType]
public readonly struct Transform : IEquatable<Transform>
{
	public readonly float3 position;

	public readonly floatQ rotation;

	public readonly float3 scale;

	public Transform(in float3 position, in floatQ rotation, in float3 scale)
	{
		this.position = position;
		this.rotation = rotation;
		this.scale = scale;
	}

	public bool Equals(Transform other)
	{
		if (position == other.position && rotation == other.rotation)
		{
			return scale == other.scale;
		}
		return false;
	}

	public static bool operator ==(Transform a, Transform b)
	{
		return a.Equals(b);
	}

	public static bool operator !=(Transform a, Transform b)
	{
		return !a.Equals(b);
	}

	public static implicit operator RenderTransform(Transform t)
	{
		return new RenderTransform(t.position, t.rotation, t.scale);
	}

	public static implicit operator Transform(RenderTransform t)
	{
		return new Transform((float3)t.position, (floatQ)t.rotation, (float3)t.scale);
	}
}
