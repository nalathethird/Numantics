// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Undo.SetValue<T>
using System;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Undo;

public class SetValue<T> : Component, IUndoable, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	public readonly SyncRef<IField<T>> Target;

	public readonly Sync<T> ValueBefore;

	public readonly Sync<T> ValueAfter;

	[DefaultValue(true)]
	protected readonly Sync<bool> _performed;

	protected readonly Sync<string> _description;

	public static bool IsValidGenericType => Coder<T>.IsEnginePrimitive;

	public bool IsActionValid
	{
		get
		{
			if (Target.Target != null)
			{
				if (Coder<T>.Equals(ValueBefore.Value, ValueAfter.Value))
				{
					return !Coder<T>.Equals(ValueBefore.Value, Target.Target.Value);
				}
				return true;
			}
			return false;
		}
	}

	public string Description
	{
		get
		{
			return _description.Value ?? this.GetLocalized("Undo.SetField", null, ("field_name", Target.Target?.Name), ("value", IsPerformed ? Target.Target.Value : ValueBefore.Value));
		}
		set
		{
			_description.Value = value;
		}
	}

	public bool IsPerformed => _performed;

	public void Redo()
	{
		Target.Target.Value = ValueAfter;
		_performed.Value = true;
	}

	public void Undo()
	{
		ValueAfter.Value = Target.Target.Value;
		if (!Target.Target.IsDriven || Target.Target.IsHooked)
		{
			Target.Target.Value = ValueBefore;
		}
		_performed.Value = false;
	}

	public bool CanUpdate(IField<T> field)
	{
		if (!(Target.Value == RefID.Null))
		{
			return Target.Target == field;
		}
		return true;
	}

	public void Update(IField<T> field)
	{
		if (Target.Target == null)
		{
			ValueBefore.Value = field.Value;
		}
		Target.Target = field;
	}

	public void Set(IField<T> field, T value)
	{
		Update(field);
		Target.Target.Value = value;
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		Target = new SyncRef<IField<T>>();
		ValueBefore = new Sync<T>();
		ValueAfter = new Sync<T>();
		_performed = new Sync<bool>();
		_description = new Sync<string>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => Target, 
			4 => ValueBefore, 
			5 => ValueAfter, 
			6 => _performed, 
			7 => _description, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static SetValue<T> __New()
	{
		return new SetValue<T>();
	}
}
