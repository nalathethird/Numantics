// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.InputGroup
using System;
using System.Collections.Generic;
using System.Reflection;
using Elements.Core;
using Elements.Data;
using FrooxEngine;
using Renderite.Shared;

[DataModelType]
public abstract class InputGroup
{
	public int Priority;

	public bool Active = true;

	public InputBlockMode BlockMode;

	public bool SubmitBlocksToGlobal;

	internal bool LastEvaluated;

	private Action onUpdate;

	private Action onEvaluate;

	private Dictionary<string, InputAction> inputActions = new Dictionary<string, InputAction>();

	private List<int> priorityLevels = new List<int>();

	public abstract string Key { get; }

	public abstract string Name { get; }

	public abstract string Description { get; }

	public abstract Chirality? Side { get; }

	public InputBindingManager Manager { get; private set; }

	public World World => Manager.World;

	public InputBlockManager Block => Manager.BlockManager;

	public InputInterface InputInterface => Manager.World.InputInterface;

	public IWorldElement Owner { get; private set; }

	public float DeltaTime { get; private set; }

	public IEnumerable<KeyValuePair<string, InputAction>> Actions => inputActions;

	protected virtual int DefaultPriority => 0;

	protected virtual InputBlockMode DefaultBlockMode => InputBlockMode.AlwaysBlock;

	public InputGroup()
	{
		Priority = DefaultPriority;
		BlockMode = DefaultBlockMode;
		try
		{
			foreach (FieldInfo f in GetType().EnumerateAllInstanceFields())
			{
				if (f.IsInitOnly && f.GetValue(this) == null)
				{
					InputAction instance = Activator.CreateInstance(f.FieldType, f.Name) as InputAction;
					inputActions.Add(f.Name, instance);
					f.SetValue(this, instance);
				}
			}
		}
		catch (Exception value)
		{
			UniLog.Error($"Exception initializing input group {GetType()}:\n{value}");
			throw;
		}
	}

	public InputAction<T> TryGetAction<T>(string name) where T : struct
	{
		inputActions.TryGetValue(name, out InputAction action);
		return action as InputAction<T>;
	}

	private void RebuildPriorityList()
	{
		priorityLevels.Clear();
		foreach (KeyValuePair<string, InputAction> action in inputActions)
		{
			action.Value.GetPriorityIndexes(priorityLevels);
			action.Value.ClearBindingsChanged();
		}
		priorityLevels.Sort((int a, int b) => -a.CompareTo(b));
	}

	public void Update(float deltaTime)
	{
		if (Owner != null && Owner.IsRemoved)
		{
			return;
		}
		DeltaTime = deltaTime;
		IUpdatable updatable = Owner as IUpdatable;
		if (onEvaluate != null)
		{
			try
			{
				if (updatable != null)
				{
					World.UpdateManager.NestCurrentlyUpdating(updatable);
				}
				onEvaluate();
			}
			catch (Exception value)
			{
				UniLog.Error($"Exception running OnEvaluate gor input group {this} on owner {Owner}\n{value}", stackTrace: false);
			}
			finally
			{
				if (updatable != null)
				{
					World.UpdateManager.PopCurrentlyUpdating(updatable);
				}
			}
		}
		foreach (KeyValuePair<string, InputAction> inputAction in inputActions)
		{
			if (inputAction.Value.BindingsChanged)
			{
				RebuildPriorityList();
				break;
			}
		}
		foreach (KeyValuePair<string, InputAction> inputAction2 in inputActions)
		{
			inputAction2.Value.BeginEvaluation();
		}
		foreach (int priority in priorityLevels)
		{
			foreach (KeyValuePair<string, InputAction> inputAction3 in inputActions)
			{
				inputAction3.Value.Evaluate(this, priority, deltaTime);
			}
			Block.SubmitGroupPriorityLevelBlocks();
		}
		foreach (KeyValuePair<string, InputAction> inputAction4 in inputActions)
		{
			inputAction4.Value.FinishEvaluation(deltaTime);
		}
		if (onUpdate == null)
		{
			return;
		}
		try
		{
			if (updatable != null)
			{
				World.UpdateManager.NestCurrentlyUpdating(updatable);
			}
			onUpdate();
		}
		catch (Exception value2)
		{
			UniLog.Error($"Exception running OnUpdate gor input group {this} on owner {Owner}\n{value2}", stackTrace: false);
		}
		finally
		{
			if (updatable != null)
			{
				World.UpdateManager.PopCurrentlyUpdating(updatable);
			}
		}
	}

	public void ClearBindings()
	{
		foreach (KeyValuePair<string, InputAction> inputAction in inputActions)
		{
			inputAction.Value.ClearBindings();
		}
	}

	public void BlockInputs()
	{
		if (BlockMode == InputBlockMode.NoBlock)
		{
			BlockMode = InputBlockMode.OneTimeBlock;
		}
	}

	public void RegisterManager(InputBindingManager manager, IWorldElement owner, Action onUpdate, Action onEvaluate = null)
	{
		if (Manager != null)
		{
			throw new InvalidOperationException("Manager is already registered!");
		}
		Manager = manager;
		Owner = owner;
		this.onUpdate = onUpdate;
		this.onEvaluate = onEvaluate;
	}
}
