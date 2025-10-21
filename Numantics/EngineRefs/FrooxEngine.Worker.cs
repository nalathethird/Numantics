// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Worker
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;

public abstract class Worker : IWorker, IWorldElement
{
	protected class InternalReferences : IDisposable
	{
		private struct SyncMemberPair
		{
			public ISyncRef origRef;

			public IWorldElement origTarget;

			public ISyncRef copyRef;

			public IWorldElement copyTarget;
		}

		private DictionaryList<IWorldElement, int> pairsByTarget;

		private Dictionary<ISyncRef, int> pairsByRef;

		private List<SyncMemberPair> pairs;

		public InternalReferences()
		{
			pairsByTarget = Pool.BorrowDictionaryList<IWorldElement, int>();
			pairsByRef = Pool.BorrowDictionary<ISyncRef, int>();
			pairs = Pool.BorrowList<SyncMemberPair>();
		}

		public void AddPair(ISyncRef reference, IWorldElement target)
		{
			SyncMemberPair pair = new SyncMemberPair
			{
				origRef = reference,
				origTarget = target
			};
			pairs.Add(pair);
			int index = pairs.Count - 1;
			pairsByTarget.Add(target, index);
			pairsByRef.Add(reference, index);
		}

		public void RegisterCopy(IWorldElement source, IWorldElement copy)
		{
			List<int> list = pairsByTarget.TryGetList(source);
			if (list == null)
			{
				return;
			}
			foreach (int itemIndex in list)
			{
				SyncMemberPair pair = pairs[itemIndex];
				pair.copyTarget = copy;
				pairs[itemIndex] = pair;
			}
		}

		public bool TryRegisterCopyReference(ISyncRef fromRef, ISyncRef toRef)
		{
			if (toRef == null)
			{
				throw new ArgumentNullException("toRef");
			}
			if (pairsByRef.TryGetValue(fromRef, out var index))
			{
				SyncMemberPair pair = pairs[index];
				pair.copyRef = toRef;
				pairs[index] = pair;
				return true;
			}
			return false;
		}

		public void TransferReferences(bool preserveMissingTargets)
		{
			foreach (SyncMemberPair pair in pairs)
			{
				if (pair.copyRef != null)
				{
					if (pair.copyTarget != null)
					{
						pair.copyRef.Target = pair.copyTarget;
					}
					else if (preserveMissingTargets)
					{
						pair.copyRef.Target = pair.origTarget;
					}
				}
			}
		}

		public void Dispose()
		{
			Pool.Return(ref pairsByTarget);
			Pool.Return(ref pairsByRef);
			Pool.Return(ref pairs);
		}
	}

	private SpinLock coroutineLock = new SpinLock(enableThreadOwnerTracking: false);

	private HashSet<Coroutine> coroutines;

	protected readonly WorkerInitInfo InitInfo;

	private Action<Coroutine> _coroutineFinishedDelegate;

	public World World { get; private set; }

	public Engine Engine => World?.Engine;

	public PhysicsManager Physics => World?.Physics;

	public EngineSkyFrostInterface Cloud => Engine?.Cloud;

	public TimeController Time => World?.Time;

	public AudioManager Audio => World?.Audio;

	public AudioSystem AudioSystem => Engine?.AudioSystem;

	public InputInterface InputInterface => World?.InputInterface;

	public InputBindingManager Input => World?.Input;

	public DebugManager Debug => World?.Debug;

	public PermissionController Permissions => World?.Permissions;

	public User LocalUser => World?.LocalUser;

	public UserRoot LocalUserRoot => LocalUser?.Root;

	public Slot LocalUserSpace => LocalUserRoot?.Slot.Parent ?? World.RootSlot;

	public virtual Type GizmoType => GizmoHelper.GetGizmoType(GetType());

	public virtual int Version => 0;

	public Type WorkerType => GetType();

	public string WorkerTypeName => WorkerType.FullName;

	public virtual string Name => WorkerType.Name;

	public string WorkerCategoryPath => InitInfo.CategoryPath;

	public RefID ReferenceID { get; private set; }

	public bool IsLocalElement { get; private set; }

	public bool IsDisposed { get; private set; }

	public bool IsScheduledForValidation { get; internal set; }

	public virtual bool IsRemoved => IsDisposed;

	public int SyncMemberCount => InitInfo.syncMemberFields.Length;

	public int SyncMethodCount
	{
		get
		{
			SyncMethodInfo[] syncMethods = InitInfo.syncMethods;
			if (syncMethods == null)
			{
				return 0;
			}
			return syncMethods.Length;
		}
	}

	public bool DontDuplicate => InitInfo.DontDuplicate;

	public IEnumerable<ISyncMember> SyncMembers
	{
		get
		{
			for (int i = 0; i < SyncMemberCount; i++)
			{
				yield return GetSyncMember(i);
			}
		}
	}

	public bool PreserveWithAssets => InitInfo.PreserveWithAssets;

	public bool GloballyRegistered => InitInfo.RegisterGlobally;

	public IWorldElement Parent { get; private set; }

	public abstract bool IsPersistent { get; }

	public event Action<Worker> Disposing;

	public FieldInfo GetSyncMemberFieldInfo(int index)
	{
		return InitInfo.syncMemberFields[index];
	}

	public string GetSyncMemberName(int index)
	{
		return InitInfo.syncMemberNames[index];
	}

	public string GetSyncMemberName(ISyncMember member)
	{
		int index = IndexOfMember(member);
		if (index < 0)
		{
			return null;
		}
		return InitInfo.syncMemberNames[index];
	}

	public int IndexOfMember(ISyncMember member)
	{
		for (int i = 0; i < SyncMemberCount; i++)
		{
			if (GetSyncMember(i) == member)
			{
				return i;
			}
		}
		return -1;
	}

	public virtual ISyncMember GetSyncMember(int index)
	{
		return InitInfo.syncMemberFields[index].GetValue(this) as ISyncMember;
	}

	public virtual void GetSyncMethodData(int index, out SyncMethodInfo info, out Delegate method)
	{
		info = InitInfo.syncMethods[index];
		if (info.methodType == typeof(Delegate))
		{
			method = null;
		}
		else if (info.methodType.IsGenericTypeDefinition)
		{
			Type[] args = new Type[info.genericMapping.Count];
			Type[] componentArgs = GetType().GetGenericArguments();
			Type[] genericArgs = GetType().GetGenericTypeDefinition().GetGenericArguments();
			IReadOnlyList<string> mappings = info.genericMapping;
			int i;
			for (i = 0; i < args.Length; i++)
			{
				if (genericArgs.FindIndex((Type t) => t.Name == mappings[i]) < 0)
				{
					method = null;
					return;
				}
				args[i] = componentArgs[i];
			}
			method = info.method.CreateDelegate(info.methodType.MakeGenericType(args), this);
		}
		else
		{
			method = info.method.CreateDelegate(info.methodType, this);
		}
	}

	public virtual Delegate GetSyncMethod(int index)
	{
		SyncMethodInfo info = InitInfo.syncMethods[index];
		return info.method.CreateDelegate(info.methodType, this);
	}

	public virtual Delegate GetSyncMethod(string name)
	{
		int index = InitInfo.syncMethods.FindIndex((SyncMethodInfo info) => info.method.Name == name);
		if (index < 0)
		{
			return null;
		}
		return GetSyncMethod(index);
	}

	public virtual Type GetSyncMethodType(int index)
	{
		return InitInfo.syncMethods[index].methodType;
	}

	private int IndexOfMember(string name)
	{
		if (InitInfo.syncMemberNameToIndex.TryGetValue(name, out var index))
		{
			return index;
		}
		return -1;
	}

	public ISyncMember GetSyncMember(string name)
	{
		int index = IndexOfMember(name);
		if (index < 0)
		{
			return null;
		}
		return GetSyncMember(index);
	}

	public FieldInfo GetSyncMemberFieldInfo(string name)
	{
		return GetSyncMemberFieldInfo(IndexOfMember(name));
	}

	public Worker()
	{
		InitInfo = WorkerInitializer.GetInitInfo(this);
	}

	protected virtual void InitializeSyncMembers()
	{
		for (int i = 0; i < InitInfo.syncMemberFields.Length; i++)
		{
			FieldInfo obj = InitInfo.syncMemberFields[i];
			_ = InitInfo.syncMemberNames[i];
			ISyncMember member = Activator.CreateInstance(obj.FieldType) as ISyncMember;
			obj.SetValue(this, member);
			if (InitInfo.syncMemberNonpersitent[i])
			{
				((SyncElement)member).MarkNonPersistent();
			}
			if (InitInfo.syncMemberNondrivable[i])
			{
				((SyncElement)member).MarkNonDrivable();
			}
		}
	}

	protected virtual void InitializeSyncMemberDefaults()
	{
		for (int i = 0; i < InitInfo.syncMemberFields.Length; i++)
		{
			if (InitInfo.defaultValues[i] != null)
			{
				((IField)GetSyncMember(i)).BoxedValue = InitInfo.defaultValues[i];
			}
		}
	}

	protected void InitializeWorker(IWorldElement parent)
	{
		try
		{
			World = parent.World;
			Parent = parent;
			InitializeSyncMembers();
			ReferenceID = World.ReferenceController.AllocateID();
			IsLocalElement = ReferenceID.IsLocalID;
			if (SyncMemberCount > 0)
			{
				ushort[] _rand = World.GetRandomizationTable(SyncMemberCount);
				if (_rand == null)
				{
					for (int i = 0; i < SyncMemberCount; i++)
					{
						GetSyncMember(i).Initialize(World, this);
					}
				}
				else
				{
					for (int j = 0; j < SyncMemberCount; j++)
					{
						GetSyncMember(_rand[j]).Initialize(World, this);
					}
				}
			}
			InitializeSyncMemberDefaults();
			World.ReferenceController.RegisterReference(this);
			if (GloballyRegistered)
			{
				World.RegisterGlobalWorker(this);
			}
			PostInitializeWorker();
		}
		catch (Exception innerException)
		{
			throw new Exception($"Exception during initializing Worker of type {GetType()}", innerException);
		}
	}

	protected virtual void PostInitializeWorker()
	{
	}

	protected virtual void SyncMemberChanged(IChangeable member)
	{
	}

	protected void EndInitializationStageForMembers()
	{
		for (int i = 0; i < SyncMemberCount; i++)
		{
			GetSyncMember(i).EndInitPhase();
		}
	}

	void IWorldElement.ChildChanged(IWorldElement child)
	{
		SyncMemberChanged(child as IChangeable);
	}

	public void Dispose()
	{
		if (!IsDisposed)
		{
			try
			{
				OnDispose();
				this.Disposing?.Invoke(this);
				this.Disposing = null;
			}
			catch (Exception exception)
			{
				UniLog.Error("Exception running OnDispose():\n" + DebugManager.PreprocessException(exception));
			}
			StopAllCoroutines();
			for (int i = 0; i < SyncMemberCount; i++)
			{
				GetSyncMember(i).Dispose();
			}
			World.ReferenceController.UnregisterReference(this);
			World = null;
			Parent = null;
			IsDisposed = true;
		}
	}

	protected void PrepareMembersForDestroy()
	{
		StopAllCoroutines();
		if (GloballyRegistered)
		{
			World.UnregisterGlobalWorker(this);
		}
		for (int i = 0; i < SyncMemberCount; i++)
		{
			ISyncMember syncMember = GetSyncMember(i);
			if (syncMember is SyncElement syncElement)
			{
				syncElement.PrepareDestroy();
			}
			else if (syncMember is Worker worker)
			{
				worker.PrepareMembersForDestroy();
			}
		}
	}

	public void CopyValues(Worker source, Action<ISyncMember, ISyncMember> copy, bool allowTypeMismatch = false)
	{
		if (!allowTypeMismatch && source?.WorkerType != WorkerType)
		{
			throw new Exception("The source type doesn't match!");
		}
		for (int i = 0; i < SyncMemberCount; i++)
		{
			if (!InitInfo.syncMemberDontCopy[i])
			{
				copy(source.GetSyncMember(i), GetSyncMember(i));
			}
		}
	}

	/// <summary>
	/// Copies values from a Worker of the same type
	/// </summary>
	/// <param name="source"></param>
	public void CopyValues(Worker source)
	{
		CopyValues(source, delegate(ISyncMember from, ISyncMember to)
		{
			to.CopyValues(from);
		});
	}

	/// <summary>
	/// Copies values from a Worker of a different type, matching them by name.
	/// </summary>
	/// <param name="source"></param>
	/// <param name="includePrivate"></param>
	public void CopyProperties(Worker source, bool includePrivate = false, Predicate<ISyncMember> filter = null)
	{
		for (int i = 0; i < source.SyncMemberCount; i++)
		{
			ISyncMember member = source.GetSyncMember(i);
			FieldInfo info = source.GetSyncMemberFieldInfo(i);
			if ((includePrivate || info.IsPublic) && (filter == null || filter(member)))
			{
				ISyncMember targetMember = GetSyncMember(source.GetSyncMemberName(i));
				if (targetMember != null && targetMember.GetType() == member.GetType())
				{
					targetMember.CopyValues(member);
				}
			}
		}
	}

	protected static void MemberCopy(ISyncMember from, ISyncMember to, InternalReferences internalRefs, HashSet<ISyncRef> breakRefs, bool checkTypes)
	{
		internalRefs.RegisterCopy(from, to);
		if (from is ISyncRef fromRef && !(from is SyncObject))
		{
			ISyncDelegate fromDelegate = from as ISyncDelegate;
			if ((fromRef.Value == RefID.Null && (fromDelegate == null || !fromDelegate.IsStaticReference)) || breakRefs.Contains(fromRef))
			{
				return;
			}
			if (internalRefs.TryRegisterCopyReference((ISyncRef)from, (ISyncRef)to))
			{
				if (to is ISyncDelegate syncDelegate)
				{
					syncDelegate.MethodName = ((ISyncDelegate)from).MethodName;
				}
			}
			else
			{
				to.CopyValues(from);
			}
		}
		else if (!checkTypes || !(from.GetType() != to.GetType()))
		{
			to.CopyValues(from, delegate(ISyncMember _from, ISyncMember _to)
			{
				MemberCopy(_from, _to, internalRefs, breakRefs, checkTypes);
			});
		}
	}

	public Task StartTask(Func<Task> task)
	{
		return World?.Coroutines.StartTask(task, this as IUpdatable) ?? NullTask();
	}

	public Task<T> StartTask<T>(Func<Task<T>> task)
	{
		return World?.Coroutines.StartTask(task, this as IUpdatable) ?? NullTask<T>();
	}

	public Task StartTask<T>(Func<T, Task> task, T argument)
	{
		return World?.Coroutines.StartTask(task, argument, this as IUpdatable) ?? NullTask();
	}

	public Task StartGlobalTask(Func<Task> task)
	{
		return World?.Coroutines.StartTask(task) ?? NullTask();
	}

	public Task<T> StartGlobalTask<T>(Func<Task<T>> task)
	{
		return World?.Coroutines.StartTask(task) ?? NullTask<T>();
	}

	public Task DelaySeconds(float seconds)
	{
		return DelayTimeSpan(TimeSpan.FromSeconds(seconds));
	}

	public async Task DelayTimeSpan(TimeSpan timespan)
	{
		await Task.Delay(timespan).ConfigureAwait(continueOnCapturedContext: false);
		await default(ToWorld);
	}

	private static async Task NullTask()
	{
		await NullTask<bool>();
	}

	private static async Task<T> NullTask<T>()
	{
		return await new TaskCompletionSource<T>().Task;
	}

	public Coroutine StartCoroutine(IEnumerator<Context> coroutine)
	{
		if (_coroutineFinishedDelegate == null)
		{
			_coroutineFinishedDelegate = CoroutineFinished;
		}
		Coroutine runningCoroutine = World.Coroutines.StartCoroutine(coroutine, _coroutineFinishedDelegate, this as IUpdatable);
		if (!runningCoroutine.IsDone)
		{
			bool _lockTaken = false;
			try
			{
				coroutineLock.Enter(ref _lockTaken);
				if (coroutines == null)
				{
					coroutines = new HashSet<Coroutine>();
				}
				coroutines.Add(runningCoroutine);
				if (runningCoroutine.IsDone)
				{
					coroutines.Remove(runningCoroutine);
				}
			}
			finally
			{
				if (_lockTaken)
				{
					coroutineLock.Exit();
				}
			}
		}
		return runningCoroutine;
	}

	public void StopAllCoroutines()
	{
		if (coroutines == null)
		{
			return;
		}
		bool _lockTaken = false;
		HashSet<Coroutine> set = Pool.BorrowHashSet<Coroutine>();
		try
		{
			coroutineLock.Enter(ref _lockTaken);
			foreach (Coroutine c in coroutines)
			{
				set.Add(c);
			}
		}
		finally
		{
			if (_lockTaken)
			{
				coroutineLock.Exit();
			}
		}
		foreach (Coroutine item in set)
		{
			item.Stop();
		}
		Pool.Return(ref set);
	}

	private void CoroutineFinished(Coroutine obj)
	{
		if (coroutines == null)
		{
			return;
		}
		bool _lockTaken = false;
		try
		{
			coroutineLock.Enter(ref _lockTaken);
			coroutines.Remove(obj);
		}
		finally
		{
			if (_lockTaken)
			{
				coroutineLock.Exit();
			}
		}
	}

	public bool CheckPermission<T>(Predicate<T> check, User user = null) where T : class, IWorkerPermissions
	{
		return Permissions.Check(this, check, user);
	}

	public virtual void Load(DataTreeNode node, LoadControl control)
	{
		Load(node, control, (string m) => true);
	}

	public void Load(DataTreeNode node, LoadControl control, Predicate<string> memberFilter)
	{
		DataTreeDictionary dict = (DataTreeDictionary)node;
		control.AssociateReference(ReferenceID, dict["ID"]);
		OnBeforeLoad(node, control);
		for (int i = 0; i < SyncMemberCount; i++)
		{
			WorkerInitInfo _initInfo = InitInfo;
			string memberName = _initInfo.syncMemberNames[i];
			if (!memberFilter(memberName))
			{
				continue;
			}
			bool isIDonly = false;
			DataTreeNode load = dict.TryGetNode(memberName);
			if (load == null)
			{
				load = dict.TryGetNode(memberName + "-ID");
				if (load != null)
				{
					isIDonly = true;
				}
			}
			if (load == null && _initInfo.oldSyncMemberNames != null && _initInfo.oldSyncMemberNames.TryGetValue(memberName, out List<string> oldNames))
			{
				foreach (string oldName in oldNames)
				{
					load = dict.TryGetNode(oldName);
					if (load != null)
					{
						break;
					}
					load = dict.TryGetNode(oldName + "-ID");
					if (load != null)
					{
						isIDonly = true;
						break;
					}
				}
			}
			if (load == null)
			{
				continue;
			}
			ISyncMember member = GetSyncMember(i);
			if (isIDonly)
			{
				control.AssociateReference(member.ReferenceID, load);
				continue;
			}
			try
			{
				member.Load(load, control);
			}
			catch (Exception value)
			{
				UniLog.Error($"Exception loading member ({member.Name}) {i}:\n{member.ParentHierarchyToString()}\n{value}");
			}
		}
		OnLoading(node, control);
	}

	public virtual DataTreeNode Save(SaveControl control)
	{
		if (!IsPersistent && !control.SaveNonPersistent)
		{
			throw new Exception("Cannot save non-persistent objects");
		}
		if (Version > 0)
		{
			control.RegisterTypeVersion(GetType(), Version);
		}
		DataTreeDictionary dict = new DataTreeDictionary();
		dict.Add("ID", control.SaveReference(ReferenceID));
		for (int i = 0; i < SyncMemberCount; i++)
		{
			ISyncMember syncMember = GetSyncMember(i);
			if (SaveMember(syncMember, control))
			{
				if ((syncMember.IsPersistent || control.SaveNonPersistent) && !InitInfo.syncMemberDontCopy[i])
				{
					DataTreeNode node = syncMember.Save(control);
					dict.Add(InitInfo.syncMemberNames[i], node);
					MemberSaved(syncMember, node, control);
				}
				else
				{
					dict.Add(InitInfo.syncMemberNames[i] + "-ID", control.SaveReference(syncMember.ReferenceID));
				}
			}
		}
		return dict;
	}

	internal virtual void RunOnSaving(SaveControl control)
	{
		OnSaving(control);
	}

	protected virtual void OnSaving(SaveControl control)
	{
	}

	protected virtual void OnBeforeLoad(DataTreeNode node, LoadControl control)
	{
	}

	protected virtual void OnLoading(DataTreeNode node, LoadControl control)
	{
	}

	protected virtual void MemberSaved(ISyncMember member, DataTreeNode node, SaveControl control)
	{
	}

	protected virtual bool SaveMember(ISyncMember member, SaveControl control)
	{
		return true;
	}

	protected virtual void OnDispose()
	{
	}

	public IField TryGetField(string name)
	{
		if (InitInfo.syncMemberNameToIndex.TryGetValue(name, out var index))
		{
			return GetSyncMember(index) as IField;
		}
		return null;
	}

	public IField<T> TryGetField<T>(string name)
	{
		return TryGetField(name) as IField<T>;
	}

	public void GetReferencedObjects(List<IWorldElement> referencedObjects, bool assetRefOnly, bool persistentOnly = true, bool skipDontCopy = false)
	{
		WorkerInitInfo _initInfo = InitInfo;
		List<ISyncRef> references = Pool.BorrowList<ISyncRef>();
		GetSyncMembers(references, skipDontCopy ? ((Predicate<int>)((int i) => !_initInfo.syncMemberDontCopy[i])) : null);
		foreach (ISyncRef r in references)
		{
			if (r.Target == null)
			{
				continue;
			}
			bool preserve = false;
			if (assetRefOnly)
			{
				if (r is IAssetRef)
				{
					preserve = true;
				}
				else if (r.Target is Component { PreserveWithAssets: not false })
				{
					preserve = true;
				}
				if (!preserve)
				{
					continue;
				}
			}
			if ((!persistentOnly || r.Target.IsPersistent || preserve) && r.Target != null)
			{
				referencedObjects.Add(r.Target);
			}
		}
		Pool.Return(ref references);
	}

	public int ForeachSyncMember<T>(Action<T> action) where T : class, IWorldElement
	{
		return ProcessSyncMembers(null, action);
	}

	public int GetSyncMembers<T>(List<T> list, bool skipDontCopy) where T : class, IWorldElement
	{
		return ProcessSyncMembers(list, null, skipDontCopy ? ((Predicate<int>)((int i) => !InitInfo.syncMemberDontCopy[i])) : null);
	}

	public int GetSyncMembers<T>(List<T> list, Predicate<int> rootMemberFilter = null) where T : class, IWorldElement
	{
		return ProcessSyncMembers(list, null, rootMemberFilter);
	}

	public List<T> GetSyncMembers<T>() where T : class, IWorldElement
	{
		List<T> list = new List<T>();
		GetSyncMembers(list);
		return list;
	}

	public int GetSyncMembers<T>(int syncMemberIndex, List<T> list) where T : class, IWorldElement
	{
		int count = 0;
		ProcessSyncMembers(list, null, GetSyncMember(syncMemberIndex), ref count);
		return count;
	}

	private int ProcessSyncMembers<T>(List<T> list, Action<T> action, Predicate<int> rootMemberFilter = null) where T : class, IWorldElement
	{
		int count = 0;
		for (int i = 0; i < SyncMemberCount; i++)
		{
			if (rootMemberFilter == null || rootMemberFilter(i))
			{
				ProcessSyncMembers(list, action, GetSyncMember(i), ref count);
			}
		}
		return count;
	}

	private void ProcessSyncMembers<T>(List<T> list, Action<T> action, ISyncMember member, ref int count) where T : class, IWorldElement
	{
		if (member is T typedMember)
		{
			list?.Add(typedMember);
			action?.Invoke(typedMember);
			count++;
		}
		if (!(member is SyncObject syncObject))
		{
			if (!(member is SyncVar syncVar))
			{
				if (!(member is ISyncList syncList))
				{
					if (member is ISyncDictionary syncDictionary)
					{
						{
							foreach (ISyncMember m in syncDictionary.Values)
							{
								ProcessSyncMembers(list, action, m, ref count);
							}
							return;
						}
					}
					if (!(member is ISyncBag syncBag))
					{
						return;
					}
					{
						foreach (IWorldElement value in syncBag.Values)
						{
							if (value is ISyncMember m2)
							{
								ProcessSyncMembers(list, action, m2, ref count);
							}
						}
						return;
					}
				}
				for (int i = 0; i < syncList.Count; i++)
				{
					ProcessSyncMembers(list, action, syncList.GetElement(i), ref count);
				}
			}
			else
			{
				ProcessSyncMembers(list, action, syncVar.Element, ref count);
			}
		}
		else
		{
			count += syncObject.ProcessSyncMembers(list, action);
		}
	}

	public bool PublicMembersEqual(Worker other)
	{
		if (GetType() != other.GetType())
		{
			return false;
		}
		List<IWorldElement> aElements = Pool.BorrowList<IWorldElement>();
		List<IWorldElement> bElements = Pool.BorrowList<IWorldElement>();
		try
		{
			GetWorldElementsForComparison(this, aElements);
			GetWorldElementsForComparison(other, bElements);
			if (aElements.Count != bElements.Count)
			{
				return false;
			}
			for (int i = 0; i < aElements.Count; i++)
			{
				IWorldElement ae = aElements[i];
				IWorldElement be = bElements[i];
				if (ae.GetType() != be.GetType())
				{
					return false;
				}
				if (!(ae is ISyncArray aArray))
				{
					if (!(ae is ISyncRef aRef))
					{
						if (!(ae is IField aField))
						{
							continue;
						}
						IField bField = (IField)be;
						object aValue = aField.BoxedValue;
						object bValue = bField.BoxedValue;
						if (aValue != null || bValue != null)
						{
							if (aValue == null || bValue == null)
							{
								return false;
							}
							if (!aField.BoxedValue.Equals(bField.BoxedValue))
							{
								return false;
							}
						}
					}
					else
					{
						ISyncRef bRef = (ISyncRef)be;
						IAsset obj = (aRef.Target as IAssetProvider)?.GenericAsset;
						IAsset bAsset = (bRef.Target as IAssetProvider)?.GenericAsset;
						if (obj != bAsset && aRef.Target != bRef.Target)
						{
							return false;
						}
					}
					continue;
				}
				ISyncArray bArray = (ISyncArray)be;
				if (aArray.Count != bArray.Count)
				{
					return false;
				}
				for (int n = 0; n < aArray.Count; n++)
				{
					if (!aArray.GetElement(n).Equals(bArray.GetElement(n)))
					{
						return false;
					}
				}
			}
		}
		finally
		{
			Pool.Return(ref aElements);
			Pool.Return(ref bElements);
		}
		return true;
	}

	private static void GetWorldElementsForComparison(Worker worker, List<IWorldElement> elements)
	{
		for (int i = 0; i < worker.SyncMemberCount; i++)
		{
			if (worker.GetSyncMemberFieldInfo(i).IsPublic)
			{
				worker.GetSyncMembers(i, elements);
			}
		}
	}

	public override string ToString()
	{
		return this.ParentHierarchyToString();
	}

	public string MembersToString()
	{
		StringBuilder str = Pool.BorrowStringBuilder();
		StringBuilder stringBuilder = str;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 2, stringBuilder);
		handler.AppendLiteral("Members on: ");
		handler.AppendFormatted(GetType());
		handler.AppendLiteral(" - ");
		handler.AppendFormatted(ReferenceID);
		stringBuilder2.AppendLine(ref handler);
		for (int i = 0; i < SyncMemberCount; i++)
		{
			ISyncMember member = GetSyncMember(i);
			stringBuilder = str;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(8, 4, stringBuilder);
			handler.AppendFormatted(member.Name);
			handler.AppendLiteral(" - ");
			handler.AppendFormatted(member.GetType());
			handler.AppendLiteral(" - ");
			handler.AppendFormatted(ReferenceID);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(member.ToString());
			stringBuilder3.AppendLine(ref handler);
		}
		string result = str.ToString();
		Pool.Return(ref str);
		return result;
	}
}
