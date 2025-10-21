// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Undo.UndoManager
using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Undo;

public class UndoManager : Component
{
	private Stack<BatchAction> _activeBatchActions = new Stack<BatchAction>();

	private Dictionary<User, Slot> _cachedLists = new Dictionary<User, Slot>();

	public readonly Sync<int> MaxUndoSteps;

	[NonPersistent]
	public readonly Sync<bool> UnsavedChanges;

	private UndoInputs _input;

	public event Action LocalUndoChanged;

	protected override void OnAwake()
	{
		MaxUndoSteps.Value = 50;
		base.Slot.MarkProtected(forcePersistent: false);
	}

	protected override void OnAttach()
	{
		base.Slot.PersistentSelf = false;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if (base.World.IsAuthority)
		{
			base.Slot.PersistentSelf = false;
		}
		_input = new UndoInputs();
		base.Input.RegisterInputGroup(_input, this, OnInputUpdate);
	}

	private void OnInputUpdate()
	{
		if (_input.Undo.Pressed)
		{
			Undo();
		}
		if (_input.Redo.Pressed)
		{
			Redo();
		}
		if ((bool)_input.Undo || (bool)_input.Redo)
		{
			_input.BlockInputs();
		}
	}

	protected override void OnDispose()
	{
		this.LocalUndoChanged = null;
		_cachedLists.Clear();
		_activeBatchActions.Clear();
		_cachedLists = null;
		_activeBatchActions = null;
		base.OnDispose();
	}

	public IUndoable GetUndoStep(User user = null)
	{
		return GetLastTopLevelAction(user, performedOnly: true);
	}

	public IUndoable GetRedoStep(User user = null)
	{
		return GetFirstTopLevelUnperformedAction(user);
	}

	public bool HasUndoSteps(User user = null)
	{
		return GetLastTopLevelAction(user, performedOnly: true) != null;
	}

	public bool HasRedoSteps(User user = null)
	{
		return GetFirstTopLevelUnperformedAction(user) != null;
	}

	public Slot GetTopLevelRoot(User user)
	{
		if (_cachedLists.TryGetValue(user, out Slot root))
		{
			if (!root.IsRemoved)
			{
				return root;
			}
			_cachedLists.Remove(user);
		}
		foreach (Slot child in base.Slot.Children)
		{
			UndoListRoot list = child.GetComponent<UndoListRoot>();
			if (list != null && list.OwnerUser.Target == user)
			{
				_cachedLists.Add(user, child);
				return child;
			}
		}
		Slot slot = base.Slot.AddSlot("User: " + user.UserID);
		slot.AttachComponent<UndoListRoot>().OwnerUser.Target = user;
		_cachedLists.Add(user, slot);
		return slot;
	}

	public Slot GetCurrentLocalRoot()
	{
		if (_activeBatchActions.Count > 0)
		{
			Slot slot = _activeBatchActions.Peek().Slot;
			if (slot.IsDestroyed)
			{
				throw new Exception($"The active batch root slot is destroyed! Slot: {slot.ParentHierarchyToString()}\nActiveBatchActions {_activeBatchActions.Count}\n{string.Join("\n", _activeBatchActions)}");
			}
			return slot;
		}
		return GetTopLevelRoot(base.LocalUser);
	}

	public IUndoable GetFirstTopLevelUnperformedAction(User user = null)
	{
		return GetFirstUnperformedAction(GetTopLevelRoot(user ?? base.LocalUser));
	}

	public IUndoable GetLastTopLevelAction(User user = null, bool performedOnly = false)
	{
		user = user ?? base.LocalUser;
		Slot root = GetTopLevelRoot(user);
		return GetLastAction(root, performedOnly);
	}

	private IUndoable GetLastAction(Slot root, bool performedOnly = false, bool cleanUpInvalid = true)
	{
		for (int i = root.ChildrenCount - 1; i >= 0; i--)
		{
			IUndoable action = root[i].GetComponent<IUndoable>();
			if (action != null)
			{
				if (!action.IsActionValid)
				{
					if (cleanUpInvalid)
					{
						root[i].Destroy();
					}
				}
				else if (!performedOnly || action.IsPerformed)
				{
					return action;
				}
			}
		}
		return null;
	}

	private IUndoable GetFirstUnperformedAction(Slot root)
	{
		for (int i = 0; i < root.ChildrenCount; i++)
		{
			IUndoable action = root[i].GetComponent<IUndoable>();
			if (action != null && !action.IsPerformed && action.IsActionValid)
			{
				return action;
			}
		}
		return null;
	}

	private U AttachAction<U>(Slot root = null) where U : Component, IUndoable, new()
	{
		long maxOffset = 0L;
		foreach (Slot child in root.Children)
		{
			maxOffset = MathX.Max(child.OrderOffset, maxOffset);
		}
		Slot slot = root.AddSlot(typeof(U).Name);
		slot.OrderOffset = maxOffset + 1;
		return slot.AttachComponent<U>();
	}

	public void TrimReversedActions(User user = null)
	{
		user = user ?? base.LocalUser;
		IUndoable action;
		while ((action = GetLastTopLevelAction(user)) != null && !action.IsPerformed)
		{
			action.Slot.Destroy();
		}
		this.LocalUndoChanged?.Invoke();
	}

	public void TrimExcessNumber(User user = null, int countOffset = 0)
	{
		user = user ?? base.LocalUser;
		Slot root = GetTopLevelRoot(user);
		while (root.ChildrenCount + countOffset > Math.Max(countOffset, MaxUndoSteps.Value))
		{
			root[0].Destroy();
		}
		this.LocalUndoChanged?.Invoke();
	}

	public U Do<U>(Action<U> action = null, Predicate<U> updateCheck = null, LocaleString description = default(LocaleString)) where U : Component, IUndoable, new()
	{
		if (_activeBatchActions.Count == 0)
		{
			TrimReversedActions(base.LocalUser);
			TrimExcessNumber(base.LocalUser, 1);
		}
		Slot root = GetCurrentLocalRoot();
		IUndoable lastAction = GetLastAction(root, performedOnly: false, _activeBatchActions.Count == 0);
		U actionComponent = null;
		if (updateCheck != null && lastAction != null && lastAction is U)
		{
			actionComponent = (U)lastAction;
			if (!updateCheck(actionComponent))
			{
				actionComponent = null;
			}
		}
		if (actionComponent == null)
		{
			actionComponent = AttachAction<U>(root);
			actionComponent.Description = this.GetLocalized(description);
		}
		action?.Invoke(actionComponent);
		this.LocalUndoChanged?.Invoke();
		UnsavedChanges.Value = true;
		return actionComponent;
	}

	public void ClearUndoPoints<U>(Predicate<U> filter, bool allUsers) where U : class, IUndoable
	{
		(allUsers ? base.Slot : GetTopLevelRoot(base.LocalUser)).GetComponentsInChildren(filter).ForEach(delegate(U u)
		{
			u.Slot.Destroy();
		});
		this.LocalUndoChanged?.Invoke();
	}

	public BatchAction BeginBatch(in LocaleString description)
	{
		BatchAction batch = Do<BatchAction>(null, null, description);
		_activeBatchActions.Push(batch);
		return batch;
	}

	public void SetActiveBatch(BatchAction batch)
	{
		if (batch.IsRemoved)
		{
			throw new InvalidOperationException("The active batch is removed, cannot set as active: " + batch?.ParentHierarchyToString());
		}
		EnsureNoActiveBatches();
		_activeBatchActions.Push(batch);
	}

	public void EndBatch()
	{
		if (_activeBatchActions.Count == 0)
		{
			throw new InvalidOperationException("There's currently no active action batch");
		}
		_activeBatchActions.Pop();
	}

	public void Redo()
	{
		EnsureNoActiveBatches();
		IUndoable firstUnperformed = GetRedoStep();
		if (firstUnperformed != null)
		{
			try
			{
				firstUnperformed.Redo();
			}
			catch (Exception exception)
			{
				base.Debug.Error($"Exception when executing Redo() on {firstUnperformed}:\n{DebugManager.PreprocessException(exception)}");
			}
			this.LocalUndoChanged?.Invoke();
			UnsavedChanges.Value = true;
		}
	}

	public void Undo()
	{
		EnsureNoActiveBatches();
		IUndoable lastAction = GetUndoStep();
		if (lastAction != null)
		{
			try
			{
				lastAction.Undo();
			}
			catch (Exception exception)
			{
				base.Debug.Error($"Exception when executing Undo() on {lastAction}:\n{DebugManager.PreprocessException(exception)}");
			}
			this.LocalUndoChanged?.Invoke();
			UnsavedChanges.Value = true;
		}
	}

	public void Clear()
	{
		EnsureNoActiveBatches();
		GetTopLevelRoot(base.LocalUser).DestroyChildren();
		this.LocalUndoChanged?.Invoke();
	}

	private void EnsureNoActiveBatches()
	{
		if (_activeBatchActions.Count > 0)
		{
			base.Debug.Error($"Active batches during an operation that doesn't expect them. Always call EndBatch() to end each batch. ActiveBatchCount: {_activeBatchActions.Count}\nActive Batches:\n{string.Join(", ", _activeBatchActions)}");
			_activeBatchActions.Clear();
		}
	}

	protected override void OnCommonUpdate()
	{
		EnsureNoActiveBatches();
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		MaxUndoSteps = new Sync<int>();
		UnsavedChanges = new Sync<bool>();
		UnsavedChanges.MarkNonPersistent();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => MaxUndoSteps, 
			4 => UnsavedChanges, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static UndoManager __New()
	{
		return new UndoManager();
	}
}
