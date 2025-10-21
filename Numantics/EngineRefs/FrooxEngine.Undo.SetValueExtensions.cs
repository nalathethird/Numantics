// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Undo.SetValueExtensions
using System;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Undo;

public static class SetValueExtensions
{
	public static bool CanSet<T>(this IField<T> field)
	{
		if (field is RawOutput<T>)
		{
			return false;
		}
		if (field.IsDriven && !field.IsHooked)
		{
			return false;
		}
		return true;
	}

	private static SetValue<T> DoSetValue<T>(this IField<T> field, Action<SetValue<T>> action, bool forceNew)
	{
		Predicate<SetValue<T>> updateCheck = null;
		if (!forceNew)
		{
			updateCheck = (SetValue<T> a) => a.CanUpdate(field);
		}
		return field.World.Do<SetValue<T>>(action, updateCheck);
	}

	public static SetValue<T> UndoableSet<T>(this IField<T> field, T value, bool forceNew = false)
	{
		if (!field.CanSet())
		{
			field.Value = value;
			return null;
		}
		return field.DoSetValue(delegate(SetValue<T> a)
		{
			a.Set(field, value);
		}, forceNew);
	}

	public static SetValue<T> CreateUndoPoint<T>(this IField<T> field, bool forceNew = false)
	{
		if (!field.CanSet())
		{
			return null;
		}
		return field.DoSetValue(delegate(SetValue<T> a)
		{
			a.Update(field);
		}, forceNew);
	}

	public static IUndoable CreateUndoPoint(this IField field, bool forceNew = false)
	{
		Type fieldType = field.GetType().GetGenericArgumentsFromInterface(typeof(IField<>))[0];
		MethodInfo method = ((!(fieldType == typeof(Type))) ? typeof(SetValueExtensions).GetGenericMethod("CreateUndoPoint", BindingFlags.Static | BindingFlags.Public, fieldType) : typeof(SetTypeExtensions).GetMethod("CreateUndoPoint", BindingFlags.Static | BindingFlags.Public));
		return (IUndoable)method.Invoke(null, new object[2] { field, forceNew });
	}
}
