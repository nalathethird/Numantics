// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.ValueField<T>
using System;
using Elements.Core;
using FrooxEngine;

[Category(new string[] { "Data" })]
[GenericTypes(GenericTypesAttribute.Group.EnginePrimitives)]
public class ValueField<T> : Component, IValueSource<T>, IValueSource, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	public readonly Sync<T> Value;

	public static bool IsValidGenericType => Coder<T>.IsEnginePrimitive;

	T IValueSource<T>.Value => Value.Value;

	object IValueSource.BoxedValue => Value.Value;

	protected override void OnAwake()
	{
		base.OnAwake();
		Value.Value = Coder<T>.Default;
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		Value = new Sync<T>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => Value, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static ValueField<T> __New()
	{
		return new ValueField<T>();
	}
}
