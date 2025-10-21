// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Slot
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elements.Core;
using Elements.Data;
using FrooxEngine;
using FrooxEngine.UIX;
using FrooxEngine.Undo;
using SkyFrost.Base;

/// <summary>
/// Slot is an empty hiearchy element that forms the base data tree of the World scene.
/// It has a few basic properties, such as transform and name, but majority of functionality is provided
/// by attaching various components on the slot.
/// </summary>
public sealed class Slot : ContainerWorker<Component>, IWorldElement, IUpdatable, IChangeable, IInitializable, IDestroyable, IWorker, ICustomInspector
{
	[Flags]
	private enum TransformElement
	{
		TRS = 1,
		Local2Global = 2,
		Global2Local = 4,
		Local2GlobalPosition = 8,
		Local2GlobalQuaternion = 0x10,
		Local2GlobalScale = 0x20
	}

	private volatile bool _synchronousChangeScheduled;

	private bool _membersInInitPhase;

	[NameOverride("Name")]
	public readonly Sync<string> NameField;

	[NameOverride("Parent")]
	public readonly SyncRef<Slot> ParentReference;

	[NameOverride("Tag")]
	protected readonly Sync<string> tag;

	[NameOverride("Active")]
	public readonly Sync<bool> ActiveSelf_Field;

	[NameOverride("Persistent")]
	[NonPersistent]
	protected readonly Sync<bool> persistent;

	/// <summary>
	/// Field that holds the local position of the slot
	/// </summary>
	[NameOverride("Position")]
	public readonly Sync<float3> Position_Field;

	/// <summary>
	/// Field that holds the local rotation of the slot
	/// </summary>
	[NameOverride("Rotation")]
	public readonly Sync<floatQ> Rotation_Field;

	/// <summary>
	/// Field that holds the local scale of the slot
	/// </summary>
	[NameOverride("Scale")]
	public readonly Sync<float3> Scale_Field;

	[OldName("_orderOffset")]
	[NameOverride("OrderOffset")]
	protected readonly Sync<long> _orderOffset;

	private Slot _currentParent;

	private SlimList<Slot> _children;

	private SlimList<Slot> _localChildren;

	private bool _childrenOrderValid = true;

	private static Func<bool, IField<bool>, bool> _rootActiveFilter = RootActiveFilter;

	private static Func<bool, IField<bool>, bool> _rootPersistentFilter = RootPersistentFilter;

	private static Func<float3, IField<float3>, float3> _rootPositionFilter = RootPositionFilter;

	private static Func<floatQ, IField<floatQ>, floatQ> _rootRotationFilter = RootRotationFilter;

	private static Func<float3, IField<float3>, float3> _rootScaleFilter = RootScaleFilter;

	private static Func<float3, IField<float3>, float3> _positionFilter = PositionFilter;

	private static Func<floatQ, IField<floatQ>, floatQ> _rotationFilter = RotationFilter;

	private static Func<float3, IField<float3>, float3> _scaleFilter = ScaleFilter;

	private bool isTrigerringEvents;

	private int childrenWithTriggeringEvents;

	private Slot registeredParentWithEvents;

	private bool _activeInHierarchy;

	private int _renderableCount;

	private bool _persistentInHierarchy;

	internal SlotMovedEventManagers movedHandlers;

	private static Comparison<Slot> _childComparison = ChildComparison;

	private const int LOCAL2GLOBAL_FLAGS = 62;

	private const int LOCAL2GLOBAL_FLAGS_NO_SCALE = 30;

	private int _transformElementValid;

	private float4x4 _cachedTRS = float4x4.Identity;

	private float4x4 _cachedLocal2Global = float4x4.Identity;

	private float4x4 _cachedGlobal2Local = float4x4.Identity;

	private float3 _cachedLocal2GlobalPosition = float3.Zero;

	private floatQ _cachedLocal2GlobalQuaternion = floatQ.Identity;

	private float3 _cachedLocal2GlobalScale = float3.One;

	private bool _isUniformScale = true;

	/// <summary>
	/// Indicates whether this slot is the root slot of the World, under which all other slots are parented.
	/// </summary>
	public bool IsRootSlot { get; private set; }

	public bool IsStarted { get; private set; }

	public bool IsDestroying { get; private set; }

	int IUpdatable.UpdateOrder => 0;

	public bool IsChangeDirty { get; private set; }

	public int LastChangeUpdateIndex { get; private set; }

	public bool IsProtected { get; private set; }

	public bool ForcedPersistent { get; private set; }

	public bool IsTransformDirty { get; private set; }

	public bool IsScaleDirty { get; private set; }

	/// <summary>
	/// The name of the slot
	/// </summary>
	public new string Name
	{
		get
		{
			return NameField;
		}
		set
		{
			NameField.Value = value;
		}
	}

	/// <summary>
	/// Optional tag of the slot, which allows finding or classifying slots in the scene by a string
	/// </summary>
	public string Tag
	{
		get
		{
			return tag;
		}
		set
		{
			tag.Value = value;
		}
	}

	/// <summary>
	/// Reference to the parent slot. Assigning a new parent automatically preserves world transform
	/// </summary>
	public new Slot Parent
	{
		get
		{
			object obj = _currentParent;
			if (obj == null)
			{
				if (!IsRootSlot)
				{
					return base.World?.RootSlot;
				}
				obj = null;
			}
			return (Slot)obj;
		}
		set
		{
			SetParent(value);
		}
	}

	/// <summary>
	/// Raw reference to the parent slot. This one will return the previous parent slot if it has been already removed from the hierarchy.
	/// It's not guaranteed to be consistent across the clients.
	/// </summary>
	public Slot RawParent => ParentReference.RawTarget;

	/// <summary>
	/// Indicates the hiearchy depth of the slot relative to the root slot of the world
	/// </summary>
	public int HierachyDepth => ComputeHierarchyDepth(base.World.RootSlot);

	/// <summary>
	/// Determines whether the Slot itself is active or not, regardless of the parent hierarchy's active state.
	/// Inactive slots deactivate their whole children hierarchy.
	/// <para>
	/// See <see cref="P:FrooxEngine.Slot.IsActive" /> to query if the slot has been made active or inactive by it's parent hierarchy.
	/// </para>
	/// </summary>
	public bool ActiveSelf
	{
		get
		{
			return ActiveSelf_Field.Value;
		}
		set
		{
			ActiveSelf_Field.Value = value;
		}
	}

	/// <summary>
	/// Ordered index of the slot among all children of its parent. This order is ensured to stay consistent among clients.
	/// Assigning a new value will swap it with the child that's currently at given index.
	/// </summary>
	public int ChildIndex
	{
		get
		{
			return Parent.IndexOfChild(this);
		}
		set
		{
			if (Parent.ChildrenCount <= value)
			{
				throw new Exception("ChildIndex is out of bounds");
			}
			SwapChildren(this, Parent[value]);
		}
	}

	public long OrderOffset
	{
		get
		{
			return _orderOffset.Value;
		}
		set
		{
			_orderOffset.Value = value;
		}
	}

	/// <summary>
	/// Indicates whether this slot is Active within the hierachy. Slot can be inactive in hierarchy if one of its parent
	/// slots are not active, even if it's active itself.
	/// </summary>
	public bool IsActive => _activeInHierarchy;

	public bool IsRenderable
	{
		get
		{
			if (_renderableCount > 0)
			{
				return !IsRemoved;
			}
			return false;
		}
	}

	public bool IsRenderTransformAllocated => RenderTransformIndex >= 0;

	public bool IsRenderTransformDirty { get; internal set; }

	internal int RenderTransformIndex { get; set; } = -1;

	public UserRoot ActiveUserRoot { get; private set; }

	public FrooxEngine.User ActiveUser => ActiveUserRoot?.ActiveUser;

	public bool IsUnderLocalUser
	{
		get
		{
			if (ActiveUserRoot == base.LocalUserRoot)
			{
				return ActiveUserRoot != null;
			}
			return false;
		}
	}

	/// <summary>
	/// Indicates if the slot is persistent in the hiearchy. If any of the parent slots are non-persistent, then
	/// all children are non persistent as well.
	/// Non-persistent elements of the hiearchy do not get saved with the world.
	/// </summary>
	public override bool IsPersistent => _persistentInHierarchy;

	/// <summary>
	/// Indicates if the slot itself is set to be persistent. Setting it to non-persistent will make the whole children
	/// hierarchy non-persistent, whic h means that they do not get saved with the world.
	/// </summary>
	public bool PersistentSelf
	{
		get
		{
			return persistent.Value;
		}
		set
		{
			if (IsRootSlot)
			{
				throw new Exception("Cannot change the persistence of the root slot");
			}
			persistent.Value = value;
		}
	}

	IWorldElement IWorldElement.Parent
	{
		get
		{
			IWorldElement parent = Parent;
			return parent ?? base.World;
		}
	}

	/// <summary>
	/// Current number of children slots
	/// </summary>
	public int ChildrenCount => _children.Count;

	public int LocalChildrenCount => _localChildren.Count;

	/// <summary>
	/// A collection of all the current children slots
	/// </summary>
	public SlimListEnumerableWrapper<Slot> Children
	{
		get
		{
			EnsureChildOrder();
			return _children;
		}
	}

	public SlimListEnumerableWrapper<Slot> LocalChildren => _localChildren;

	/// <summary>
	/// Returns a child slot at specific index from all the current children slots.
	/// The order of children is ensured to stay consistent between clients in presence of no changes.
	/// </summary>
	/// <param name="childIndex">The index of the child slot to return</param>
	/// <returns>The child slot at given index</returns>
	public Slot this[int childIndex]
	{
		get
		{
			if (childIndex < 0 || childIndex >= ChildrenCount)
			{
				throw new ArgumentOutOfRangeException("childIndex");
			}
			EnsureChildOrder();
			return _children[childIndex];
		}
	}

	/// <summary>
	/// Indicates if the slot has identify transform relative to its parent
	/// </summary>
	public bool HasIdentityTransform
	{
		get
		{
			if (LocalPosition == float3.Zero && LocalRotation == floatQ.Identity)
			{
				return LocalScale == float3.One;
			}
			return false;
		}
	}

	/// <summary>
	/// Local position of the slot in the coordinate system of its parent
	/// </summary>
	public float3 LocalPosition
	{
		get
		{
			return Position_Field.Value;
		}
		set
		{
			Position_Field.Value = value;
		}
	}

	/// <summary>
	/// The local rotation of the slot in the coordinate system of its parent
	/// </summary>
	public floatQ LocalRotation
	{
		get
		{
			return Rotation_Field.Value;
		}
		set
		{
			Rotation_Field.Value = value;
		}
	}

	/// <summary>
	/// The local scale of the slot in the coordinate system of its parent
	/// </summary>
	public float3 LocalScale
	{
		get
		{
			return Scale_Field.Value;
		}
		set
		{
			Scale_Field.Value = value;
		}
	}

	/// <summary>
	/// The global position of the slot in the scene coordinate space
	/// </summary>
	public float3 GlobalPosition
	{
		get
		{
			if ((_transformElementValid & 8) == 0)
			{
				_cachedLocal2GlobalPosition = LocalToGlobal_Fast.DecomposedPosition;
				_transformElementValid |= 8;
			}
			return _cachedLocal2GlobalPosition;
		}
		set
		{
			if (!value.IsNaN && !value.IsInfinity)
			{
				LocalPosition = Parent.GlobalPointToLocal(in value);
				if ((Parent._transformElementValid & 0x1E) != 0)
				{
					_cachedLocal2GlobalPosition = value;
					_transformElementValid |= 8;
				}
			}
		}
	}

	/// <summary>
	/// The global rotation of the slot in the scene coorindate space
	/// </summary>
	public floatQ GlobalRotation
	{
		get
		{
			return LocalToGlobalQuaternion;
		}
		set
		{
			if (value.IsValid)
			{
				value = value.FastNormalized;
				LocalRotation = Parent.GlobalRotationToLocal(in value);
				if ((Parent._transformElementValid & 0x1E) != 0)
				{
					_cachedLocal2GlobalQuaternion = value;
					_transformElementValid |= 16;
				}
			}
		}
	}

	/// <summary>
	/// The global scale of the slot in the scene coordinate space
	/// </summary>
	public float3 GlobalScale
	{
		get
		{
			return LocalToGlobalScale;
		}
		set
		{
			if (!value.IsNaN && !value.IsInfinity)
			{
				LocalScale = Parent.GlobalScaleToLocal(in value);
				if ((Parent._transformElementValid & 0x20) != 0)
				{
					_cachedLocal2GlobalScale = value;
					_transformElementValid |= 32;
				}
			}
		}
	}

	/// <summary>
	/// Transformation matrix that transforms from the scene coordinate space into the coordinate space of the slot
	/// </summary>
	public float4x4 GlobalToLocal
	{
		get
		{
			EnsureValidGlobalToLocal();
			return _cachedGlobal2Local;
		}
	}

	public ref readonly float4x4 GlobalToLocal_Fast
	{
		get
		{
			EnsureValidGlobalToLocal();
			return ref _cachedGlobal2Local;
		}
	}

	/// <summary>
	/// The transformation matrix of the slot. It transforms from its local coordinate space into the parent's
	/// </summary>
	public float4x4 TRS
	{
		get
		{
			EnsureValidTRS();
			return _cachedTRS;
		}
		set
		{
			value.Decompose(out var position, out var rotation, out var scale);
			LocalScale = scale;
			LocalPosition = position;
			LocalRotation = rotation;
			_cachedTRS = value;
			_transformElementValid |= 1;
		}
	}

	internal ref float4x4 TRS_Fast
	{
		get
		{
			EnsureValidTRS();
			return ref _cachedTRS;
		}
	}

	/// <summary>
	/// Transformation matrix which transforms from the slot's coordinate space into the scene coordinate space
	/// </summary>
	public float4x4 LocalToGlobal
	{
		get
		{
			EnsureValidLocal2Global();
			return _cachedLocal2Global;
		}
		set
		{
			if (!IsRootSlot)
			{
				TRS = Parent.GlobalToLocal_Fast.MultiplyAffineFast(in value);
				_cachedLocal2Global = value;
				_transformElementValid |= 2;
			}
		}
	}

	internal ref float4x4 LocalToGlobal_Fast
	{
		get
		{
			EnsureValidLocal2Global();
			return ref _cachedLocal2Global;
		}
	}

	/// <summary>
	/// Quaternion representing a rotation from the slot's local coordinate space to the global.
	/// This doesn't include negative scaling transformation.
	/// </summary>
	public floatQ LocalToGlobalQuaternion
	{
		get
		{
			if ((_transformElementValid & 0x10) == 0)
			{
				_cachedLocal2GlobalQuaternion = LocalToGlobal_Fast.DecomposedRotation;
				_transformElementValid |= 16;
			}
			return _cachedLocal2GlobalQuaternion;
		}
	}

	/// <summary>
	/// Quaternions representing a rotation from global coordinate space into slot's local.
	/// This doesn't include negative scaling transformation.
	/// </summary>
	public float3 LocalToGlobalScale
	{
		get
		{
			EnsureValidLocalToGlobalScale();
			return _cachedLocal2GlobalScale;
		}
	}

	/// <summary>
	/// Unit vector indicating the slot's forward direction in the global coodinate space.
	/// </summary>
	public float3 Forward
	{
		get
		{
			return LocalDirectionToGlobal(float3.Forward);
		}
		set
		{
			GlobalRotation = floatQ.LookRotation(in value, Up);
		}
	}

	/// <summary>
	/// Unit vector indicating the slot's upwards direction in the global coodinate space.
	/// </summary>
	public float3 Up
	{
		get
		{
			return LocalDirectionToGlobal(float3.Up);
		}
		set
		{
			GlobalRotation = floatQ.LookRotation(in value, -Forward) * floatQ.FromToRotation(float3.Up, float3.Forward);
		}
	}

	/// <summary>
	/// Unit vector indicating the slot's backward direction in the global coodinate space.
	/// </summary>
	public float3 Backward
	{
		get
		{
			return LocalDirectionToGlobal(float3.Backward);
		}
		set
		{
			GlobalRotation = floatQ.LookRotation(-value, Up);
		}
	}

	/// <summary>
	/// Unit vector indicating the slot's downwards direction in the global coodinate space.
	/// </summary>
	public float3 Down
	{
		get
		{
			return LocalDirectionToGlobal(float3.Down);
		}
		set
		{
			GlobalRotation = floatQ.LookRotation(in value, Forward) * floatQ.FromToRotation(float3.Down, float3.Forward);
		}
	}

	/// <summary>
	/// Unit vector indicating the slot's left direction in the global coodinate space.
	/// </summary>
	public float3 Left
	{
		get
		{
			return LocalDirectionToGlobal(float3.Left);
		}
		set
		{
			GlobalRotation = floatQ.LookRotation(in value, Forward) * floatQ.FromToRotation(float3.Left, float3.Forward);
		}
	}

	/// <summary>
	/// Unit vector indicating the slot's right direction in the global coodinate space.
	/// </summary>
	public float3 Right
	{
		get
		{
			return LocalDirectionToGlobal(float3.Right);
		}
		set
		{
			GlobalRotation = floatQ.LookRotation(in value, Forward) * floatQ.FromToRotation(float3.Right, float3.Forward);
		}
	}

	public RigidTransform GlobalRigidTransform => new RigidTransform(GlobalPosition, GlobalRotation);

	public event SlotEvent OnPrepareDestroy;

	private event Action<IChangeable> _changed;

	public event Action<IDestroyable> Destroyed;

	public event SlotEvent ChildrenOrderInvalidated;

	public event SlotEvent WorldTransformChanged
	{
		add
		{
			if (!IsRemoved)
			{
				GeneralMovedHierarchyEventHandler handler = movedHandlers?.generalHandler;
				if (handler == null)
				{
					handler = base.World.GeneralMovedHierarchyEvents.GetHandler(this, createIfDoesNotExist: true);
				}
				handler.SlotMoved += value;
			}
		}
		remove
		{
			if (!IsRemoved)
			{
				GeneralMovedHierarchyEventHandler handler = movedHandlers?.generalHandler;
				if (handler != null)
				{
					handler.SlotMoved -= value;
				}
			}
		}
	}

	public event SlotEvent PhysicsWorldTransformChanged
	{
		add
		{
			if (!IsRemoved)
			{
				PhysicsMovedHierarchyEventHandler handler = movedHandlers?.physicsHandler;
				if (handler == null)
				{
					handler = base.World.PhysicsMovedHierarchyEvents.GetHandler(this, createIfDoesNotExist: true);
				}
				handler.SlotMoved += value;
			}
		}
		remove
		{
			if (!IsRemoved)
			{
				PhysicsMovedHierarchyEventHandler handler = movedHandlers?.physicsHandler;
				if (handler != null)
				{
					handler.SlotMoved -= value;
				}
			}
		}
	}

	public event SlotEvent PhysicsWorldScaleChanged
	{
		add
		{
			if (!IsRemoved)
			{
				PhysicsMovedHierarchyEventHandler handler = movedHandlers?.physicsHandler;
				if (handler == null)
				{
					handler = base.World.PhysicsMovedHierarchyEvents.GetHandler(this, createIfDoesNotExist: true);
				}
				handler.SlotScaled += value;
			}
		}
		remove
		{
			if (!IsRemoved)
			{
				PhysicsMovedHierarchyEventHandler handler = movedHandlers?.physicsHandler;
				if (handler != null)
				{
					handler.SlotScaled -= value;
				}
			}
		}
	}

	public event Action<IChangeable> Changed
	{
		add
		{
			if (!IsRemoved)
			{
				_changed += value;
				UpdateChangedEventHierarchy();
			}
		}
		remove
		{
			if (!IsRemoved)
			{
				_changed -= value;
				UpdateChangedEventHierarchy();
			}
		}
	}

	public event SlotEvent ActiveChanged;

	public event SlotEvent PersistentChanged;

	public event SlotEvent NameChanged;

	public event SlotEvent OrderOffsetChanged;

	public event SlotEvent ParentChanged;

	public event SlotEvent ActiveUserRootChanged;

	public event SlotChildEvent ChildAdded;

	public event SlotChildEvent ChildRemoved;

	public void MarkProtected(bool forcePersistent)
	{
		IsProtected = true;
		ForcedPersistent = forcePersistent;
		if (Parent != base.World.RootSlot && Parent != null)
		{
			Parent.MarkProtected(forcePersistent);
		}
	}

	/// <summary>
	/// Computes hierchy depth relative to another slot. This indicates a number of slots required to travel upwards
	/// in the hiearrchy to reach the target slot.
	/// </summary>
	/// <param name="root">The slot relative to which the depth is computed</param>
	/// <returns>Hierarchy depth relative to the root slot, -1 if the root isn't a parent of this slot</returns>
	public int ComputeHierarchyDepth(Slot root)
	{
		int depth = 0;
		Slot current = this;
		while (current != root && current != null)
		{
			depth++;
			current = current.Parent;
		}
		if (current == null)
		{
			return -1;
		}
		return depth;
	}

	/// <summary>
	/// Sets a new parent to the slot, while optionally not preserving the global transform
	/// </summary>
	/// <param name="newParent">Reference to the new parent slot</param>
	/// <param name="keepGlobalTransform">Whether to preserve the global transform (position, rotation and scale) of the slot</param>
	public void SetParent(Slot newParent, bool keepGlobalTransform = true)
	{
		if (IsRemoved)
		{
			return;
		}
		if (base.IsInInitPhase && !newParent.IsInInitPhase)
		{
			throw new Exception("Cannot change parent when in initialization phase!");
		}
		if (IsProtected)
		{
			return;
		}
		newParent = newParent ?? base.World.RootSlot;
		if (newParent.IsRemoved)
		{
			UniLog.Warning("Trying to assign a destroyed parent: " + ParentHierarchyToString() + ", destroyedParent: " + newParent.ToString(), stackTrace: true);
			newParent = base.World.RootSlot;
		}
		if (newParent.IsChildOf(this, includeSelf: true))
		{
			return;
		}
		if (keepGlobalTransform)
		{
			float4x4 newTRS = newParent.GlobalToLocal_Fast.MultiplyAffineFast(in LocalToGlobal_Fast);
			ParentReference.Target = newParent;
			if (Position_Field.IsDriven || Rotation_Field.IsDriven || Scale_Field.IsDriven)
			{
				if (!Scale_Field.IsBlockedByDrive)
				{
					LocalScale = newTRS.DecomposedScale;
				}
				if (!Rotation_Field.IsBlockedByDrive)
				{
					LocalRotation = newTRS.DecomposedRotation;
				}
				if (!Position_Field.IsBlockedByDrive)
				{
					LocalPosition = newTRS.DecomposedPosition;
				}
			}
			else
			{
				TRS = newTRS;
			}
		}
		else
		{
			ParentReference.Target = newParent;
		}
	}

	public void TrySetParent(Slot newParent, bool keepGlobalTransform = true)
	{
		if (newParent == null)
		{
			newParent = base.World.RootSlot;
		}
		if (newParent != Parent)
		{
			SetParent(newParent, keepGlobalTransform);
		}
	}

	public static void SwapChildren(Slot a, Slot b)
	{
		int aIndex = a.ChildIndex;
		int bIndex = b.ChildIndex;
		if (aIndex > bIndex)
		{
			int num = bIndex;
			bIndex = aIndex;
			aIndex = num;
			Slot slot = b;
			b = a;
			a = slot;
		}
		a.InsertAtIndex(bIndex);
		b.InsertAtIndex(aIndex);
	}

	/// <summary>
	/// Inserts this slot at specific index in the list of children of the parent slot, shifting other children around.
	/// </summary>
	/// <param name="index">Index at which this slot should be inserted</param>
	public void InsertAtIndex(int index)
	{
		if (index == 0)
		{
			Slot first = Parent[0];
			if (first != this)
			{
				OrderOffset = first.OrderOffset - 100;
			}
			return;
		}
		if (index == Parent.ChildrenCount - 1)
		{
			Slot last = Parent[Parent.ChildrenCount - 1];
			if (last != this)
			{
				OrderOffset = last.OrderOffset + 100;
			}
			return;
		}
		if (index < Parent.ChildrenCount - 1)
		{
			Slot current = Parent[index];
			if (current != this)
			{
				Slot previous = Parent[index - 1];
				if (current.OrderOffset - previous.OrderOffset <= 1)
				{
					MakeSpaceForOrderNumber(index);
				}
				long gap = current.OrderOffset - previous.OrderOffset;
				OrderOffset = previous.OrderOffset + Math.Max(1L, gap / 2);
			}
			return;
		}
		throw new Exception("ChildIndex is out of bounds");
	}

	public void SortChildren(Comparison<Slot> comparison)
	{
		if (ChildrenCount >= 2)
		{
			List<Slot> list = Pool.BorrowList<Slot>();
			list.AddRange(_children);
			list.Sort(comparison);
			for (int i = 0; i < list.Count; i++)
			{
				list[i].OrderOffset = i * 100;
			}
			Pool.Return(ref list);
		}
	}

	private void MakeSpaceForOrderNumber(int index)
	{
		Slot _parent = Parent;
		if (_parent.ChildrenCount <= 1)
		{
			return;
		}
		Slot current = _parent[index];
		if (index == _parent.ChildrenCount - 1)
		{
			current.OrderOffset = _parent[index - 1].OrderOffset + 100;
			return;
		}
		Slot next = _parent[index + 1];
		while (next.OrderOffset - current.OrderOffset <= 1)
		{
			MakeSpaceForOrderNumber(index + 1);
		}
		long gap = next.OrderOffset - current.OrderOffset;
		current.OrderOffset += MathX.Max(1L, gap / 2);
	}

	internal Slot(bool startInInitPhase)
	{
		base.IsInInitPhase = startInInitPhase;
	}

	internal void Initialize(World world, bool isRoot = false)
	{
		base.Initialize(world);
		if (!isRoot)
		{
			ParentReference.OnTargetChange += NewParentSlotAvailable;
		}
		persistent.Value = true;
		_persistentInHierarchy = true;
		Position_Field.Value = float3.Zero;
		Rotation_Field.Value = floatQ.Identity;
		Scale_Field.Value = float3.One;
		ActiveSelf = true;
		_activeInHierarchy = true;
		if (isRoot)
		{
			IsRootSlot = true;
			_transformElementValid = -1;
			ActiveSelf_Field.LocalFilter = _rootActiveFilter;
			persistent.LocalFilter = _rootPersistentFilter;
			Position_Field.LocalFilter = _rootPositionFilter;
			Rotation_Field.LocalFilter = _rootRotationFilter;
			Scale_Field.LocalFilter = _rootScaleFilter;
			ActiveSelf_Field.MarkNonDrivable();
			persistent.MarkNonDrivable();
			Position_Field.MarkNonDrivable();
			Rotation_Field.MarkNonDrivable();
			Scale_Field.MarkNonDrivable();
		}
		else
		{
			Position_Field.LocalFilter = _positionFilter;
			Rotation_Field.LocalFilter = _rotationFilter;
			Scale_Field.LocalFilter = _scaleFilter;
			persistent.OnValueChange += PersistentValueChanged;
			ActiveSelf_Field.OnValueChange += ActiveValueChanged;
			Position_Field.OnValueChange += PositionChanged;
			Rotation_Field.OnValueChange += RotationChanged;
			Scale_Field.OnValueChange += ScaleChanged;
		}
		_orderOffset.OnValueChange += OrderOffsetValueChanged;
		NameField.OnValueChange += NameValueChanged;
		base.UpdateManager.RegisterForStartup(this);
		if (!base.IsInInitPhase)
		{
			EndInitializationStageForMembers();
		}
		else
		{
			_membersInInitPhase = true;
		}
	}

	protected override void RunComponentAdded(Component component)
	{
		base.RunComponentAdded(component);
		base.World.RunComponentAdded(this, component);
	}

	protected override void RunComponentRemoved(Component component)
	{
		base.RunComponentRemoved(component);
		base.World.RunComponentRemoved(this, component);
	}

	private void NameValueChanged(SyncField<string> syncField)
	{
		this.NameChanged?.Invoke(this);
	}

	private void OrderOffsetValueChanged(SyncField<long> syncField)
	{
		if (Parent != null)
		{
			Parent.InvalidateChildrenOrder();
		}
		this.OrderOffsetChanged?.Invoke(this);
	}

	private static bool RootActiveFilter(bool value, IField<bool> field)
	{
		if (!value)
		{
			throw new Exception("Cannot change active status of the Root Node");
		}
		return true;
	}

	private static bool RootPersistentFilter(bool value, IField<bool> field)
	{
		if (!value)
		{
			throw new Exception("Cannot change persistent status of the Root Node");
		}
		return true;
	}

	private static float3 RootPositionFilter(float3 value, IField<float3> field)
	{
		if (value != float3.Zero)
		{
			throw new Exception("Cannot change the position of the Root Node");
		}
		return float3.Zero;
	}

	private static floatQ RootRotationFilter(floatQ value, IField<floatQ> field)
	{
		if (value != floatQ.Identity)
		{
			throw new Exception("Cannot change the rotation of the Root Node");
		}
		return floatQ.Identity;
	}

	private static float3 RootScaleFilter(float3 value, IField<float3> field)
	{
		if (value != float3.One)
		{
			throw new Exception("Cannot change the scale of the Root Node");
		}
		return float3.One;
	}

	private static float3 PositionFilter(float3 value, IField<float3> field)
	{
		if (!value.IsInfinity && !value.IsNaN)
		{
			return value;
		}
		return float3.Zero;
	}

	private static floatQ RotationFilter(floatQ value, IField<floatQ> field)
	{
		if (value.IsValid)
		{
			return value.FastNormalized;
		}
		return floatQ.Identity;
	}

	private static float3 ScaleFilter(float3 value, IField<float3> field)
	{
		if (!value.IsInfinity && !value.IsNaN)
		{
			return value;
		}
		return float3.One;
	}

	private void PositionChanged(SyncField<float3> syncField)
	{
		SlotTransformChanged(scaleChanged: false);
	}

	private void RotationChanged(SyncField<floatQ> syncField)
	{
		SlotTransformChanged(scaleChanged: false);
	}

	private void ScaleChanged(SyncField<float3> syncField)
	{
		float3 scale = LocalScale;
		_isUniformScale = MathX.Abs(scale.x - scale.y) < 0.01f && MathX.Abs(scale.x - scale.z) < 0.01f;
		SlotTransformChanged(scaleChanged: true);
	}

	protected override void SyncMemberChanged(IChangeable member)
	{
		MarkChangeDirty();
	}

	/// <summary>
	/// Creates a new slot in the scene as a child of this one.
	/// </summary>
	/// <param name="name">The name of the newly created slot</param>
	/// <returns></returns>
	public Slot AddSlot(string name = "Slot", bool persistent = true)
	{
		if (IsRemoved)
		{
			throw new Exception("Cannot Add Slot on a removed slot! Removed slot " + ParentHierarchyToString());
		}
		if (base.IsLocalElement)
		{
			return base.World.AddLocalSlot(this, name, persistent);
		}
		return base.World.AddSlot(this, name, persistent);
	}

	public Slot AddLocalSlot(string name = "Local Slot", bool persistent = false)
	{
		return base.World.AddLocalSlot(this, name, persistent);
	}

	/// <summary>
	/// Creates a new slot in the scene as a child of this one and inserts it at specific index among the other children.
	/// </summary>
	/// <param name="index">The index of the new child</param>
	/// <param name="name">The name of the newly created slot</param>
	/// <returns></returns>
	public Slot InsertSlot(int index, string name = "Slot")
	{
		Slot slot = ((!base.IsLocalElement) ? base.World.AddSlot(this, name) : base.World.AddLocalSlot(this, name));
		slot.InsertAtIndex(index);
		return slot;
	}

	public void ReparentChildren(Slot newParent)
	{
		if (newParent.IsChildOf(this, includeSelf: true))
		{
			throw new ArgumentException("New parent must be outside of the hierarchy of the source Slot");
		}
		while (_children.Count > 0)
		{
			_children[_children.Count - 1].Parent = newParent;
		}
		while (_localChildren.Count > 0)
		{
			_localChildren[_localChildren.Count - 1].Parent = newParent;
		}
	}

	private void SendDestroyingEvent()
	{
		IsDestroying = true;
		foreach (KeyValuePair<RefID, Component> item in componentBag)
		{
			item.Value.RunOnDestroying();
		}
		IsDestroying = false;
	}

	private void SendDestroyingEventToHierarchy()
	{
		for (int i = _children.Count - 1; i >= 0; i--)
		{
			_children[i].SendDestroyingEventToHierarchy();
		}
		for (int i2 = _localChildren.Count - 1; i2 >= 0; i2--)
		{
			_localChildren[i2].SendDestroyingEventToHierarchy();
		}
		SendDestroyingEvent();
	}

	/// <summary>
	/// Destroys the Slot, removing it from the world, while parenting all its children under a different slot.
	/// </summary>
	/// <param name="moveChildren">The slot under which all current children will be parented</param>
	public void Destroy(Slot moveChildren, bool sendDestroyingEvent = true)
	{
		if (IsRootSlot)
		{
			throw new InvalidOperationException("Cannot Destroy RootSlot");
		}
		if (!IsProtected && !base.IsDestroyed)
		{
			if (sendDestroyingEvent)
			{
				SendDestroyingEvent();
			}
			ReparentChildren(moveChildren);
			DestroySelf();
		}
	}

	[SyncMethod(typeof(Action), new string[] { })]
	public void Destroy()
	{
		Destroy(sendDestroyingEvent: true);
	}

	[SyncMethod(typeof(Action), new string[] { })]
	public void DestroyPreservingAssets()
	{
		DestroyPreservingAssets(null, sendDestroyingEvent: true);
	}

	/// <summary>
	/// Destroys the Slot and its entire children hierarchy, removing them from the scene.
	/// </summary>
	public void Destroy(bool sendDestroyingEvent = true)
	{
		if (IsRootSlot)
		{
			throw new InvalidOperationException("Cannot Destroy RootSlot");
		}
		if (!IsProtected && !base.IsDestroyed)
		{
			if (sendDestroyingEvent)
			{
				SendDestroyingEventToHierarchy();
			}
			DestroyChildren(preserveAssets: false, sendDestroyingEvent: false, includeLocal: true);
			DestroySelf();
		}
	}

	/// <summary>
	/// Destroys the entire children hiearchy of this slot, but keeps the slot itself.
	/// </summary>
	public void DestroyChildren(bool preserveAssets = false, bool sendDestroyingEvent = true, bool includeLocal = false, Predicate<Slot> filter = null)
	{
		if (base.World.IsDisposed || IsProtected)
		{
			return;
		}
		if (ChildrenCount > 0)
		{
			if (preserveAssets)
			{
				for (int i = _children.Count - 1; i >= 0; i--)
				{
					Slot child = _children[i];
					if (filter == null || filter(child))
					{
						if (child.IsProtected)
						{
							child.Parent = null;
						}
						else
						{
							child.DestroyPreservingAssets(null, sendDestroyingEvent);
						}
					}
				}
			}
			else
			{
				for (int i2 = _children.Count - 1; i2 >= 0; i2--)
				{
					Slot child2 = _children[i2];
					if (filter == null || filter(child2))
					{
						if (child2.IsProtected)
						{
							child2.Parent = null;
						}
						else
						{
							child2.Destroy(sendDestroyingEvent);
						}
					}
				}
			}
		}
		if (LocalChildrenCount <= 0 || !(base.IsLocalElement || includeLocal))
		{
			return;
		}
		if (preserveAssets)
		{
			for (int i3 = _localChildren.Count - 1; i3 >= 0; i3--)
			{
				Slot child3 = _localChildren[i3];
				if (filter == null || filter(child3))
				{
					child3.DestroyPreservingAssets(null, sendDestroyingEvent);
				}
			}
			return;
		}
		for (int i4 = _localChildren.Count - 1; i4 >= 0; i4--)
		{
			Slot child4 = _localChildren[i4];
			if (filter == null || filter(child4))
			{
				child4.Destroy(sendDestroyingEvent);
			}
		}
	}

	/// <summary>
	/// Destroys the slot, but preserves all AssetProviders in its hiearchy that are referenced outside of the destroyed hierachy.
	/// This technically preserves all slots with asset provider components, but destroys all other components on them.
	/// </summary>
	/// <param name="relocateAssets">Optional slot under which to relocate all the assets. If null, it'll be automatically created.</param>
	public void DestroyPreservingAssets(Slot relocateAssets = null, bool sendDestroyingEvent = true)
	{
		if (base.IsDestroyed || IsProtected)
		{
			return;
		}
		if (relocateAssets != null && relocateAssets.IsChildOf(this, includeSelf: true))
		{
			throw new ArgumentException("RelocateAssets target is within the hierarchy that's being destroyed");
		}
		if (sendDestroyingEvent)
		{
			SendDestroyingEventToHierarchy();
		}
		Func<Slot> getRelocationTarget = delegate
		{
			if (relocateAssets == null)
			{
				relocateAssets = base.World.AssetsSlot.AddSlot(Name + " - Assets");
			}
			return relocateAssets;
		};
		HashSet<Slot> hierarchy = Pool.BorrowHashSet<Slot>();
		GenerateHierarchy(hierarchy, includeLocal: true);
		AssetPreserveDependencies dependencies = new AssetPreserveDependencies(this, hierarchy);
		DestroyNonAssetComponents(dependencies);
		AssetPreserveDependencies.Return(ref dependencies);
		RelocateOrDestroyEmpty(getRelocationTarget);
	}

	private void DestroyNonAssetComponents(AssetPreserveDependencies dependencies)
	{
		for (int i = ChildrenCount - 1; i >= 0; i--)
		{
			_children[i].DestroyNonAssetComponents(dependencies);
		}
		for (int i2 = LocalChildrenCount - 1; i2 >= 0; i2--)
		{
			_localChildren[i2].DestroyNonAssetComponents(dependencies);
		}
		RemoveAllComponents((Component c) => CanRemoveNonAssetComponent(c, dependencies));
		if (!base.Components.Any((Component c) => c is IAssetProvider))
		{
			RemoveAllComponents((Component c) => c.PreserveWithAssets);
		}
	}

	private void RelocateOrDestroyEmpty(Func<Slot> getRelocationTarget)
	{
		for (int i = _children.Count - 1; i >= 0; i--)
		{
			_children[i].RelocateOrDestroyEmpty(getRelocationTarget);
		}
		for (int i2 = _localChildren.Count - 1; i2 >= 0; i2--)
		{
			_localChildren[i2].RelocateOrDestroyEmpty(getRelocationTarget);
		}
		if (base.ComponentCount == 0)
		{
			Destroy(sendDestroyingEvent: false);
		}
		else
		{
			SetParent(getRelocationTarget(), keepGlobalTransform: false);
		}
	}

	private bool CanRemoveNonAssetComponent(Component c, AssetPreserveDependencies dependencies)
	{
		if (c.PreserveWithAssets)
		{
			return false;
		}
		if (!(c is IAssetProvider assetProvider))
		{
			return true;
		}
		return !assetProvider.References.Any((IAssetRef r) => IsAnExternalReference(r, dependencies));
	}

	private bool IsAnExternalReference(IAssetRef r, AssetPreserveDependencies dependencies)
	{
		bool? isExternal = dependencies.IsExternalReference(r);
		if (isExternal.HasValue)
		{
			return isExternal.Value;
		}
		dependencies.RegisterWalkedReference(r);
		if (!dependencies.IsInRootHierarchy(r))
		{
			dependencies.RegisterExternalReference(r);
			return true;
		}
		if (r.FindNearestParent<IComponent>() is IAssetProvider provider)
		{
			foreach (IAssetRef nestedRef in provider.References)
			{
				if (IsAnExternalReference(nestedRef, dependencies))
				{
					dependencies.RegisterExternalReference(r);
					return true;
				}
			}
		}
		return false;
	}

	private void DestroySelf()
	{
		if (IsRootSlot)
		{
			throw new InvalidOperationException("Cannot Destroy RootSlot");
		}
		if (!base.IsDestroyed && !base.World.IsDisposed)
		{
			base.World.RemoveSlot(this);
		}
	}

	internal override void PrepareDestruction()
	{
		if (base.IsDestroyed)
		{
			return;
		}
		this.OnPrepareDestroy?.Invoke(this);
		Slot _parent = Parent;
		if (_parent != null)
		{
			_parent.InformChildRemoved(this);
			if (!base.IsLocalElement || _parent.IsLocalElement)
			{
				_parent.ChildRemoved?.Invoke(_parent, this);
			}
		}
		base.PrepareDestruction();
		base.World.UpdateManager.RegisterToDestroy(this);
		if (!IsChangeDirty)
		{
			this._changed?.Invoke(this);
		}
	}

	public void InternalRunStartup()
	{
		IsStarted = true;
		MarkChangeDirty();
	}

	public void InternalRunUpdate()
	{
		throw new Exception("Cannot run update cycle for slots.");
	}

	protected override void OnDispose()
	{
		if (!base.World.IsDisposed)
		{
			if (registeredParentWithEvents != null)
			{
				registeredParentWithEvents?.UnregisterChildForEvents();
			}
			for (int i = 0; i < _localChildren.Count; i++)
			{
				Slot child = _localChildren[i];
				if (!child.IsRemoved)
				{
					child.RunSynchronously(child.Destroy);
				}
			}
			if (base.World.IsAuthority)
			{
				for (int j = 0; j < _children.Count; j++)
				{
					Slot child2 = _children[j];
					if (!child2.IsRemoved)
					{
						child2.RunSynchronously(child2.Destroy);
					}
				}
			}
			if (_renderableCount > 0)
			{
				_renderableCount = 0;
				UnregisterRenderable();
			}
		}
		_children.Clear();
		_localChildren.Clear();
		_currentParent = null;
		this.ActiveChanged = null;
		this.PersistentChanged = null;
		this.NameChanged = null;
		this.OrderOffsetChanged = null;
		this.ParentChanged = null;
		this.ActiveUserRootChanged = null;
		this.ChildAdded = null;
		this.ChildRemoved = null;
		registeredParentWithEvents = null;
		movedHandlers?.Dispose();
		movedHandlers = null;
		this._changed = null;
		this.Destroyed = null;
		this.ChildrenOrderInvalidated = null;
		base.OnDispose();
	}

	public void InternalRunDestruction()
	{
		this.Destroyed?.Invoke(this);
		this.Destroyed = null;
		for (int i = _localChildren.Count - 1; i >= 0; i--)
		{
			_localChildren[i].Destroy();
		}
		Dispose();
	}

	private void UpdateChangedEventHierarchy()
	{
		if (IsRootSlot || IsRemoved)
		{
			return;
		}
		bool updateParent = false;
		if (registeredParentWithEvents != null && Parent != registeredParentWithEvents)
		{
			registeredParentWithEvents.UnregisterChildForEvents();
			registeredParentWithEvents = null;
			updateParent = true;
		}
		bool shouldTriggerEvents = this._changed != null || childrenWithTriggeringEvents > 0;
		if (shouldTriggerEvents != isTrigerringEvents)
		{
			isTrigerringEvents = shouldTriggerEvents;
			updateParent = true;
		}
		if (!updateParent)
		{
			return;
		}
		if (isTrigerringEvents)
		{
			if (registeredParentWithEvents == null)
			{
				registeredParentWithEvents = Parent;
				registeredParentWithEvents.RegisterChildForEvents();
			}
		}
		else if (registeredParentWithEvents != null)
		{
			registeredParentWithEvents.UnregisterChildForEvents();
			registeredParentWithEvents = null;
		}
	}

	private void RegisterChildForEvents()
	{
		childrenWithTriggeringEvents++;
		if (childrenWithTriggeringEvents == 1)
		{
			UpdateChangedEventHierarchy();
		}
	}

	private void UnregisterChildForEvents()
	{
		childrenWithTriggeringEvents--;
		if (childrenWithTriggeringEvents == 0)
		{
			UpdateChangedEventHierarchy();
		}
	}

	private void SlotTransformChanged(bool scaleChanged)
	{
		_transformElementValid &= -2;
		InvalidateLocal2Global(scaleChanged || !_isUniformScale);
		if (!IsTransformDirty || (scaleChanged && !IsScaleDirty))
		{
			movedHandlers?.MarkMoved(scaleChanged);
			IsTransformDirty = true;
			IsScaleDirty |= scaleChanged;
		}
		if (IsRenderTransformAllocated && !IsRenderTransformDirty)
		{
			IsRenderTransformDirty = false;
			base.World.Render.Transforms.RegisterPoseUpdate(this);
		}
	}

	internal void ClearTransformDirty()
	{
		IsTransformDirty = false;
		IsScaleDirty = false;
	}

	private void ActiveValueChanged(SyncField<bool> syncField)
	{
		if (IsRemoved)
		{
			return;
		}
		if (IsProtected && !syncField.Value)
		{
			RunSynchronously(delegate
			{
				syncField.Value = true;
			});
			return;
		}
		if (Parent != null)
		{
			UpdateActiveHierarchy();
		}
		MarkChangeDirty();
	}

	private void UpdateActiveHierarchy()
	{
		bool shouldBeActive = IsRootSlot || (Parent.IsActive && ActiveSelf);
		if (shouldBeActive == _activeInHierarchy)
		{
			return;
		}
		_activeInHierarchy = shouldBeActive;
		if (ChildrenCount > 0)
		{
			foreach (Slot child in _children)
			{
				child.UpdateActiveHierarchy();
			}
		}
		if (LocalChildrenCount > 0)
		{
			foreach (Slot localChild in _localChildren)
			{
				localChild.UpdateActiveHierarchy();
			}
		}
		this.ActiveChanged?.Invoke(this);
		if (base.World.CanMakeSynchronousChanges)
		{
			SendActivatedEvents();
		}
		else
		{
			base.World.UpdateManager.ActiveStateChagned(this);
		}
	}

	internal void SendActivatedEvents()
	{
		try
		{
			if (IsActive)
			{
				foreach (Component component in base.Components)
				{
					component.RunActivated();
				}
				return;
			}
			foreach (Component component2 in base.Components)
			{
				component2.RunDeactivated();
			}
		}
		catch (Exception value)
		{
			UniLog.Error($"Exception when running {(IsActive ? "OnActivated" : "OnDeactivated")} event on {this}\n{value}");
		}
	}

	internal void IncrementRenderable()
	{
		if (_renderableCount++ == 0)
		{
			if (!base.World.Render.IsRenderingSupported)
			{
				throw new InvalidOperationException("Rendering is not supported");
			}
			if (IsRemoved)
			{
				throw new InvalidOperationException("Cannot IncrementRenderable on removed slots. Slot:\n" + this);
			}
			Slot _parent = Parent;
			if (_parent != null && !_parent.IsRemoved)
			{
				_parent.IncrementRenderable();
			}
			base.World.Render.Transforms.RegisterAddedOrRemovedRenderableSlot(this);
		}
	}

	internal void DecrementRenderable()
	{
		if (!IsRemoved && --_renderableCount == 0)
		{
			UnregisterRenderable();
		}
	}

	private void UnregisterRenderable()
	{
		if (!base.World.Render.IsRenderingSupported)
		{
			throw new InvalidOperationException("Rendering is not supported");
		}
		base.World.Render.Transforms.RegisterAddedOrRemovedRenderableSlot(this);
		Parent?.DecrementRenderable();
	}

	internal void RegisterUserRoot(UserRoot userRoot)
	{
		if (userRoot == ActiveUserRoot || (ActiveUserRoot != null && ActiveUserRoot.Slot.IsChildOf(userRoot.Slot)))
		{
			return;
		}
		ActiveUserRoot = userRoot;
		foreach (Slot child in Children)
		{
			child.RegisterUserRoot(ActiveUserRoot);
		}
		MarkChangeDirty();
		this.ActiveUserRootChanged?.Invoke(this);
	}

	internal void UnregisterUserRoot(UserRoot userRoot)
	{
		UnregisterUserRootHiearchy(userRoot);
		UpdateUserRootFromParent();
	}

	private void UnregisterUserRootHiearchy(UserRoot userRoot)
	{
		if (userRoot != ActiveUserRoot)
		{
			return;
		}
		ActiveUserRoot = null;
		foreach (Slot child in Children)
		{
			child.UnregisterUserRootHiearchy(userRoot);
		}
		MarkChangeDirty();
		this.ActiveUserRootChanged?.Invoke(this);
	}

	private void UpdateUserRootFromParent()
	{
		if (ActiveUserRoot != null && ActiveUserRoot.Slot == this)
		{
			return;
		}
		UserRoot parentRoot = Parent?.ActiveUserRoot;
		if (parentRoot != ActiveUserRoot)
		{
			if (ActiveUserRoot != null)
			{
				UnregisterUserRootHiearchy(ActiveUserRoot);
			}
			if (parentRoot != null)
			{
				RegisterUserRoot(parentRoot);
			}
		}
	}

	private void PersistentValueChanged(SyncField<bool> syncField)
	{
		if (ForcedPersistent && !syncField.Value)
		{
			RunSynchronously(delegate
			{
				persistent.Value = true;
			});
		}
		else
		{
			UpdatePersistenceHierarchy();
		}
	}

	private void UpdatePersistenceHierarchy()
	{
		if (Parent == null)
		{
			return;
		}
		bool shouldBePersistent = IsRootSlot || (Parent.IsPersistent && PersistentSelf);
		if (shouldBePersistent == _persistentInHierarchy)
		{
			return;
		}
		_persistentInHierarchy = shouldBePersistent;
		this.PersistentChanged?.Invoke(this);
		if (ChildrenCount > 0)
		{
			foreach (Slot child in _children)
			{
				child.UpdatePersistenceHierarchy();
			}
		}
		if (LocalChildrenCount <= 0)
		{
			return;
		}
		foreach (Slot localChild in _localChildren)
		{
			localChild.UpdatePersistenceHierarchy();
		}
	}

	/// <summary>
	/// Serializes a part of the scene hiearchy starting at this slot into a DataTreeNode hiearchy.
	/// </summary>
	/// <param name="control">Helper object used to control the serialization process.</param>
	/// <returns></returns>
	public override DataTreeNode Save(SaveControl control)
	{
		if (!IsPersistent && !control.SaveNonPersistent)
		{
			throw new NotSupportedException("Cannot save non-persistent objects!");
		}
		DataTreeDictionary dict = null;
		dict = (DataTreeDictionary)base.Save(control);
		DataTreeList childrenList = new DataTreeList();
		foreach (Slot ch in Children)
		{
			bool save = ch.IsPersistent || control.SaveNonPersistent;
			if (ch.ActiveUserRoot?.Slot == ch)
			{
				save = false;
			}
			if (save)
			{
				childrenList.Add(ch.Save(control));
				ch.GetComponents(control.nonpersistentAssets, (IAssetProvider a) => !a.IsPersistent && WorldOptimizer.IsReferencedOutsideHierarchy(a, ch));
			}
			else
			{
				ch.GetComponentsInChildren(control.nonpersistentAssets, (IAssetProvider a) => WorldOptimizer.IsReferencedOutsideHierarchy(a, ch));
			}
		}
		dict.Add("ParentReference", control.ReferenceToString(ParentReference.ReferenceID));
		dict.Add("Children", childrenList);
		return dict;
	}

	/// <summary>
	/// Deserializes part of previously saved scene hieararchy under the hierarchy of this slot.
	/// </summary>
	/// <param name="node">The root DataTreeNode from which to deserialize the scene hiearchy</param>
	/// <param name="control">Helper object used to control the deserializaiton process</param>
	public override void Load(DataTreeNode node, LoadControl control)
	{
		DataTreeDictionary dict = node as DataTreeDictionary;
		DataTreeList childrenList;
		if (IsRootSlot && dict == null)
		{
			childrenList = (DataTreeList)node;
		}
		else
		{
			DataTreeNode parentRefId = dict.TryGetNode("ParentReference");
			if (parentRefId != null)
			{
				control.AssociateReference(ParentReference.ReferenceID, parentRefId);
			}
			base.Load(dict, control);
			if (Tag == "\ud83d\udca9\ud83d\udca9\ud83d\udca9\ud83d\ude18" && Name == "Tailroot")
			{
				control.OnLoaded(this, Destroy);
				return;
			}
			childrenList = (DataTreeList)dict["Children"];
		}
		foreach (DataTreeDictionary slotDict in childrenList.Cast<DataTreeDictionary>())
		{
			AddSlot(null).Load(slotDict, control);
		}
	}

	protected override bool SaveMember(ISyncMember member, SaveControl control)
	{
		if (member == ParentReference)
		{
			return false;
		}
		return true;
	}

	/// <summary>
	/// Returns a first component in the children hierarchy (including this slot) that satisfies given condition.
	/// </summary>
	/// <typeparam name="T">The type of the component to find.</typeparam>
	/// <param name="filter">Optional function used to filter the components</param>
	/// <returns>First found component or null if none found</returns>
	public T GetComponentInChildren<T>(Predicate<T> filter = null, bool includeLocal = false, bool excludeDisabled = false) where T : class
	{
		T c = GetComponent(filter, excludeDisabled);
		if (c != null)
		{
			return c;
		}
		EnsureChildOrder();
		for (int i = 0; i < ChildrenCount; i++)
		{
			c = _children[i].GetComponentInChildren(filter, includeLocal, excludeDisabled);
			if (c != null)
			{
				return c;
			}
		}
		if (includeLocal)
		{
			for (int j = 0; j < LocalChildrenCount; j++)
			{
				c = _localChildren[j].GetComponentInChildren(filter, includeLocal, excludeDisabled);
				if (c != null)
				{
					return c;
				}
			}
		}
		return null;
	}

	public Component GetComponentInChildren(Type type)
	{
		if (IsRemoved)
		{
			return null;
		}
		Component c = GetComponent(type);
		if (c != null)
		{
			return c;
		}
		foreach (Slot child in Children)
		{
			c = child.GetComponentInChildren(type);
			if (c != null)
			{
				return c;
			}
		}
		return null;
	}

	/// <summary>
	/// Returns a list of all components in the children hierarchy (including this slot) that satisfy given condion.
	/// </summary>
	/// <typeparam name="T">The type of the components to find</typeparam>
	/// <param name="filter">Optional function used to filter the components.</param>
	/// <returns>A list of all found components</returns>
	public List<T> GetComponentsInChildren<T>(Predicate<T> filter = null, bool excludeDisabled = false, bool includeLocal = false, Predicate<Slot> slotFilter = null) where T : class
	{
		List<T> list = new List<T>();
		GetComponentsInChildren(list, filter, excludeDisabled, includeLocal, slotFilter);
		return list;
	}

	/// <summary>
	/// Adds all components in the children hiearchy (including this slot) that satisfy given condition to the provided list.
	/// </summary>
	/// <typeparam name="T">The type of components to find</typeparam>
	/// <param name="results">List to which the found components will be added</param>
	/// <param name="filter">Optional function used to filter the components.</param>
	public void GetComponentsInChildren<T>(List<T> results, Predicate<T> filter = null, bool excludeDisabled = false, bool includeLocal = false, Predicate<Slot> slotFilter = null) where T : class
	{
		if ((excludeDisabled && !IsActive) || (slotFilter != null && !slotFilter(this)))
		{
			return;
		}
		GetComponents(results, filter, excludeDisabled);
		EnsureChildOrder();
		for (int i = 0; i < ChildrenCount; i++)
		{
			_children[i].GetComponentsInChildren(results, filter, excludeDisabled, includeLocal, slotFilter);
		}
		if (includeLocal)
		{
			for (int j = 0; j < LocalChildrenCount; j++)
			{
				_localChildren[j].GetComponentsInChildren(results, filter, excludeDisabled, includeLocal, slotFilter);
			}
		}
	}

	public void GetFirstDirectComponentsInChildren<T>(List<T> results, Predicate<T> filter = null, bool excludeDisabled = false, bool includeLocal = false, bool skipSelf = true) where T : class
	{
		if ((excludeDisabled && !IsActive) || (!skipSelf && GetComponents(results, filter, excludeDisabled) > 0))
		{
			return;
		}
		EnsureChildOrder();
		for (int i = 0; i < ChildrenCount; i++)
		{
			_children[i].GetFirstDirectComponentsInChildren(results, filter, excludeDisabled, includeLocal, skipSelf: false);
		}
		if (includeLocal)
		{
			for (int j = 0; j < LocalChildrenCount; j++)
			{
				_localChildren[j].GetFirstDirectComponentsInChildren(results, filter, excludeDisabled, includeLocal, skipSelf: false);
			}
		}
	}

	/// <summary>
	/// Calls the provided function for each component of given type in the children hiearchy (including self)
	/// </summary>
	/// <typeparam name="T">The type of the components to find</typeparam>
	/// <param name="callback">The function which to call for each component.</param>
	public void ForeachComponentInChildren<T>(Action<T> callback, bool includeLocal = false, bool cacheItems = false) where T : class
	{
		ForeachComponentInChildren(callback, null, includeLocal, cacheItems);
	}

	/// <summary>
	/// Calls the provided function for each component of given type in the children hierarchy (including self).
	/// If the function returns false, the walking of the hierachy is stopped.
	/// </summary>
	/// <typeparam name="T">The type of the components to find</typeparam>
	/// <param name="callback">The functio nwhich to call for each component.</param>
	/// <returns>True if the function returned true for all found components, otherwise false</returns>
	public bool ForeachComponentInChildren<T>(Func<T, bool> callback, bool includeLocal = false, bool cacheItems = false) where T : class
	{
		return ForeachComponentInChildren(null, callback, includeLocal, cacheItems);
	}

	protected bool ForeachComponentInChildren<T>(Action<T> callback, Func<T, bool> callbackStopper, bool includeLocal = false, bool cacheItems = false, bool excludeDisabled = false) where T : class
	{
		if (!ForeachComponent(callback, callbackStopper, cacheItems, excludeDisabled))
		{
			return false;
		}
		EnsureChildOrder();
		for (int i = 0; i < ChildrenCount; i++)
		{
			if (!_children[i].ForeachComponentInChildren(callback, callbackStopper, includeLocal, cacheItems, excludeDisabled))
			{
				return false;
			}
		}
		if (includeLocal)
		{
			for (int j = 0; j < LocalChildrenCount; j++)
			{
				if (!_localChildren[j].ForeachComponentInChildren(callback, callbackStopper, includeLocal, cacheItems, excludeDisabled))
				{
					return false;
				}
			}
		}
		return true;
	}

	/// <summary>
	/// Calls the provided function for each component of given type in the parent hierarchy (including self)
	/// </summary>
	/// <typeparam name="T">The type of the components to find</typeparam>
	/// <param name="callback">Function to call for each component</param>
	/// <returns></returns>
	public bool ForeachComponentInParents<T>(Action<T> callback, bool cacheItems = false, bool excludeDisabled = false) where T : class
	{
		return ForeachComponentInParents(callback, null, cacheItems, excludeDisabled);
	}

	/// <summary>
	/// Calls the provided function for each component of given type in the parent hierarchy (including self)
	/// If the function returns false it'll stop.
	/// </summary>
	/// <typeparam name="T">The type of the components to find</typeparam>
	/// <param name="callback">The function to call for each component</param>
	/// <returns>True if the function returned true for all components, false if it was stopped</returns>
	public bool ForeachComponentInParents<T>(Func<T, bool> callback, bool cacheItems = false, bool excludeDisabled = false) where T : class
	{
		return ForeachComponentInParents(null, callback, cacheItems, excludeDisabled);
	}

	protected bool ForeachComponentInParents<T>(Action<T> callback, Func<T, bool> callbackStopper, bool cacheItems, bool excludeDisabled) where T : class
	{
		if (IsRemoved)
		{
			return false;
		}
		Slot current = this;
		while (current != null && !current.IsRootSlot)
		{
			if (!current.ForeachComponent(callback, callbackStopper, cacheItems, excludeDisabled))
			{
				return false;
			}
			current = current.Parent;
		}
		return true;
	}

	/// <summary>
	/// Finds the first component in the parent hierarchy (including self) which matches the given criteria
	/// </summary>
	/// <typeparam name="T">The type of the component to find</typeparam>
	/// <param name="filter">Optional predicate to filter the components</param>
	/// <returns>The first found component or null if none matches the criteria</returns>
	public T GetComponentInParents<T>(Predicate<T> filter = null, bool includeSelf = true, bool excludeDisabled = false) where T : class
	{
		if (IsRemoved)
		{
			return null;
		}
		Slot current = this;
		if (!includeSelf)
		{
			current = this?.Parent;
		}
		while (current != null)
		{
			T c = current.GetComponent(filter, excludeDisabled);
			if (c != null)
			{
				return c;
			}
			current = current.Parent;
		}
		return null;
	}

	public Component GetComponentInParents(Type type, bool includeSelf = true)
	{
		if (IsRemoved)
		{
			return null;
		}
		Slot current = this;
		if (!includeSelf)
		{
			current = this?.Parent;
		}
		while (current != null)
		{
			Component c = current.GetComponent(type);
			if (c != null)
			{
				return c;
			}
			current = current.Parent;
		}
		return null;
	}

	/// <summary>
	/// Fills the list with components in the parent hierarchy (including self) which match the given criteria
	/// </summary>
	/// <typeparam name="T">The type of the component to find</typeparam>
	/// <param name="results">The list to fill with the found components</param>
	/// <param name="filter">Optional predicate to filter the components</param>
	public void GetComponentsInParents<T>(List<T> results, Predicate<T> filter = null) where T : class
	{
		if (!IsRemoved)
		{
			for (Slot current = this; current != null; current = current.Parent)
			{
				current.GetComponents(results, filter);
			}
		}
	}

	/// <summary>
	/// Finds all components in the parent hierarchy (including self) which match the given criteria
	/// </summary>
	/// <typeparam name="T">The type of the component to find</typeparam>
	/// <param name="filter">Optional predicate to filter the components</param>
	/// <returns>List of all found components that match the criteria</returns>
	public List<T> GetComponentsInParents<T>(Predicate<T> filter = null) where T : class
	{
		List<T> list = new List<T>();
		GetComponentsInParents(list, filter);
		return list;
	}

	public T GetComponentInParentsOrChildren<T>(Predicate<T> filter = null, bool includeLocal = false) where T : class
	{
		if (IsRemoved)
		{
			return null;
		}
		return GetComponentInParents(filter) ?? GetComponentInChildren(filter, includeLocal);
	}

	public T GetComponentInChildrenOrParents<T>(Predicate<T> filter = null, bool includeLocal = false) where T : class
	{
		return GetComponentInChildren(filter, includeLocal) ?? GetComponentInParents(filter);
	}

	/// <summary>
	/// Returns the child index of a specific slot.
	/// </summary>
	/// <param name="slot">The slot for which to determine the index</param>
	/// <returns>Child index of the slot or -1 if it's not a child of given slot</returns>
	public int IndexOfChild(Slot slot)
	{
		EnsureChildOrder();
		return _children.IndexOf(slot);
	}

	/// <summary>
	/// Indicates if this slot is within the hierarchy of another
	/// </summary>
	/// <param name="slot">The root of the children hierarchy to check</param>
	/// <param name="includeSelf">Whether to return true if the slot is the same one as this is called on</param>
	/// <returns>True is the slot is within the hierarchy, false otherwise</returns>
	public bool IsChildOf(Slot slot, bool includeSelf = false)
	{
		if (this == slot)
		{
			return includeSelf;
		}
		return InternalIsChildOf(slot, this, 255);
	}

	private void EnsureChildOrder()
	{
		if (!_childrenOrderValid)
		{
			_children.Sort(_childComparison);
			_childrenOrderValid = true;
		}
	}

	private static int ChildComparison(Slot a, Slot b)
	{
		int comp = a.OrderOffset.CompareTo(b.OrderOffset);
		if (comp != 0)
		{
			return comp;
		}
		return a.ReferenceID.CompareTo(b.ReferenceID);
	}

	private void InvalidateChildrenOrder()
	{
		if (_childrenOrderValid)
		{
			_childrenOrderValid = false;
			this.ChildrenOrderInvalidated?.Invoke(this);
		}
	}

	private bool InternalIsChildOf(Slot slot, Slot originator, int max)
	{
		if (Parent == null)
		{
			return false;
		}
		if (Parent == originator)
		{
			return false;
		}
		if (Parent == slot)
		{
			return true;
		}
		return Parent.InternalIsChildOf(slot, originator, max - 1);
	}

	public int ChildDistance(Slot slot)
	{
		Slot currentSlot = slot;
		int distance = 0;
		while (currentSlot != this && currentSlot != null)
		{
			currentSlot = currentSlot.Parent;
			distance++;
		}
		if (currentSlot == null)
		{
			return -1;
		}
		return distance;
	}

	/// <summary>
	/// Copies all components from another slot onto this one
	/// </summary>
	/// <param name="target"></param>
	public void CopyComponents(Slot target)
	{
		target.ForeachComponent(delegate(Component c)
		{
			CopyComponent(c);
		});
	}

	/// <summary>
	/// Finds the first direct child with given name
	/// </summary>
	/// <param name="name">The name to find</param>
	/// <returns>First found slot with given name or null if none matches</returns>
	public Slot FindChild(string name)
	{
		if (ChildrenCount == 0)
		{
			return null;
		}
		EnsureChildOrder();
		foreach (Slot child in _children)
		{
			if (child.Name == name)
			{
				return child;
			}
		}
		return null;
	}

	public Slot FindLocalChild(string name)
	{
		if (LocalChildrenCount == 0)
		{
			return null;
		}
		EnsureChildOrder();
		foreach (Slot child in _localChildren)
		{
			if (child.Name == name)
			{
				return child;
			}
		}
		return null;
	}

	public Slot FindLocalChildOrAdd(string name)
	{
		Slot slot = FindLocalChild(name);
		if (slot != null)
		{
			return slot;
		}
		return AddLocalSlot(name);
	}

	/// <summary>
	/// Finds the first child slot in the hierarchy with given name
	/// </summary>
	/// <param name="name">The name to find</param>
	/// <returns>First found slot with given name or null if none matches</returns>
	public Slot FindChildInHierarchy(string name)
	{
		Slot slot = FindChild(name);
		if (slot != null)
		{
			return slot;
		}
		if (ChildrenCount == 0)
		{
			return null;
		}
		foreach (Slot child in _children)
		{
			slot = child.FindChildInHierarchy(name);
			if (slot != null)
			{
				return slot;
			}
		}
		return null;
	}

	/// <summary>
	/// Finds a direct child slot with given name or adds a new one with that name if none found
	/// </summary>
	/// <param name="name">The name to find</param>
	/// <returns>First found slot with that name or the newly added one if none was found</returns>
	public Slot FindChildOrAdd(string name, bool persistent = true)
	{
		return FindChild(name) ?? AddSlot(name, persistent);
	}

	/// <summary>
	/// Returns a list with all the direct and indirect children in the slot's hiearchy
	/// </summary>
	/// <param name="includeSelf">Whether to include the slot this is called on in the list</param>
	/// <returns>List of all the children</returns>
	public List<Slot> GetAllChildren(bool includeSelf = false)
	{
		List<Slot> list = new List<Slot>();
		GetAllChildren(list, includeSelf);
		return list;
	}

	public void GetAllChildren(ICollection<Slot> slots, bool includeSelf = false)
	{
		if (includeSelf)
		{
			slots.Add(this);
		}
		if (ChildrenCount <= 0)
		{
			return;
		}
		foreach (Slot child in _children)
		{
			child.GetAllChildren(slots, includeSelf: true);
		}
	}

	public void GetAllParents(ICollection<Slot> slots, bool includeSelf = false)
	{
		if (includeSelf)
		{
			slots.Add(this);
		}
		if (!IsRootSlot)
		{
			Parent.GetAllParents(slots, includeSelf: true);
		}
	}

	public bool ForeachChild(Func<Slot, bool> func, bool includeSelf = false)
	{
		if (includeSelf && !func(this))
		{
			return false;
		}
		if (ChildrenCount > 0)
		{
			foreach (Slot child in _children)
			{
				if (!child.ForeachChild(func, includeSelf: true))
				{
					return false;
				}
			}
		}
		return true;
	}

	public void ForeachChildDepthFirst(Action<Slot> action, bool includeSelf = false, bool childrenInReverseOrder = false)
	{
		if (ChildrenCount > 0)
		{
			if (childrenInReverseOrder)
			{
				for (int i = ChildrenCount - 1; i >= 0; i--)
				{
					_children[i].ForeachChildDepthFirst(action, includeSelf: true, childrenInReverseOrder);
				}
			}
			else
			{
				foreach (Slot child in _children)
				{
					child.ForeachChildDepthFirst(action, includeSelf: true);
				}
			}
		}
		if (includeSelf)
		{
			action(this);
		}
	}

	public bool ForeachParent(Func<Slot, bool> func, bool includeSelf = false)
	{
		if (includeSelf && !func(this))
		{
			return false;
		}
		if (!IsRootSlot && !Parent.ForeachParent(func, includeSelf: true))
		{
			return false;
		}
		return true;
	}

	public void ForeachChild(Action<Slot> action, bool includeSelf = false)
	{
		ForeachChild(delegate(Slot s)
		{
			action(s);
			return true;
		}, includeSelf);
	}

	public void ForeachParent(Action<Slot> action, bool includeSelf = false)
	{
		ForeachParent(delegate(Slot s)
		{
			action(s);
			return true;
		}, includeSelf);
	}

	public Slot FindChild(Predicate<Slot> filter, int maxDepth = -1)
	{
		EnsureChildOrder();
		for (int i = 0; i < ChildrenCount; i++)
		{
			Slot ch = _children[i];
			if (filter(ch))
			{
				return ch;
			}
		}
		if (maxDepth < 0 || maxDepth > 0)
		{
			for (int j = 0; j < ChildrenCount; j++)
			{
				Slot child = _children[j].FindChild(filter, maxDepth - 1);
				if (child != null)
				{
					return child;
				}
			}
		}
		return null;
	}

	public Slot FindChild(string name, bool matchSubstring, bool ignoreCase, int maxDepth = -1)
	{
		return FindChild((Slot s) => MatchSlot(s, name, matchSubstring, ignoreCase), maxDepth);
	}

	public Slot FindParent(Predicate<Slot> filter, int maxDepth = -1)
	{
		Slot current = Parent;
		if (current == null)
		{
			return null;
		}
		do
		{
			if (filter(current))
			{
				return current;
			}
			if (maxDepth == 0)
			{
				return null;
			}
			if (maxDepth > 0)
			{
				maxDepth--;
			}
			current = current.Parent;
		}
		while (current != null);
		return null;
	}

	public Slot FindParent(string name, bool matchSubstring, bool ignoreCase, int maxDepth = -1)
	{
		return FindParent((Slot s) => MatchSlot(s, name, matchSubstring, ignoreCase), maxDepth);
	}

	private static bool MatchSlot(Slot slot, string name, bool matchSubstring, bool ignoreCase)
	{
		if (slot.Name == null)
		{
			return name == null;
		}
		if (name == null)
		{
			return false;
		}
		if (slot.Name == "")
		{
			return name == "";
		}
		StringComparison comparisonType = (ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (matchSubstring)
		{
			return slot.Name.IndexOf(name, comparisonType) >= 0;
		}
		return string.Compare(slot.Name, name, comparisonType) == 0;
	}

	public int GetMaxChildDepth()
	{
		int maxDepth = 0;
		foreach (Slot child in Children)
		{
			int childDepth = 1 + child.GetMaxChildDepth();
			if (childDepth > maxDepth)
			{
				maxDepth = childDepth;
			}
		}
		return maxDepth;
	}

	public Slot FindCommonRoot(Slot other)
	{
		Slot current = this;
		while (!current.IsRootSlot && !other.IsChildOf(current, includeSelf: true))
		{
			current = current.Parent;
		}
		return current;
	}

	public Slot Duplicate(Slot duplicateRoot = null, bool keepGlobalTransform = true, DuplicationSettings settings = null, bool duplicateAsLocal = false)
	{
		if (IsRootSlot)
		{
			throw new Exception("Cannot duplicate root slot");
		}
		if (duplicateRoot == null)
		{
			duplicateRoot = Parent ?? base.World.RootSlot;
		}
		else if (duplicateRoot.IsChildOf(this, includeSelf: true))
		{
			throw new Exception("Target for the duplicate hierarchy cannot be within the hierarchy of the source");
		}
		InternalReferences internalRefs = new InternalReferences();
		HashSet<ISyncRef> breakRefs = Pool.BorrowHashSet<ISyncRef>();
		HashSet<Slot> hierarchy = Pool.BorrowHashSet<Slot>();
		List<Action> postDuplication = Pool.BorrowList<Action>();
		ForeachComponentInChildren(delegate(IDuplicationHandler h)
		{
			h.OnBeforeDuplicate(this, out Action onDuplicated);
			if (onDuplicated != null)
			{
				postDuplication.Add(onDuplicated);
			}
		}, includeLocal: false, cacheItems: true);
		GenerateHierarchy(hierarchy);
		CollectInternalReferences(this, internalRefs, breakRefs, hierarchy);
		Slot copy = InternalDuplicate(duplicateRoot, internalRefs, breakRefs, settings, duplicateAsLocal);
		if (keepGlobalTransform)
		{
			copy.CopyTransform(this);
		}
		internalRefs.TransferReferences(preserveMissingTargets: false);
		List<Component> duplicatedComponents = Pool.BorrowList<Component>();
		copy.GetComponentsInChildren(duplicatedComponents);
		foreach (Component item in duplicatedComponents)
		{
			item.RunDuplicate();
		}
		Pool.Return(ref duplicatedComponents);
		Pool.Return(ref breakRefs);
		internalRefs.Dispose();
		foreach (Action item2 in postDuplication)
		{
			item2();
		}
		Pool.Return(ref postDuplication);
		return copy;
	}

	public T DuplicateComponent<T>(T sourceComponent, bool breakExternalReferences = false) where T : Component
	{
		List<Component> sourceComponents = Pool.BorrowList<Component>();
		sourceComponents.Add(sourceComponent);
		List<Component> duplicates = Pool.BorrowList<Component>();
		DuplicateComponents(sourceComponents, breakExternalReferences, duplicates);
		T result = (T)duplicates[0];
		Pool.Return(ref sourceComponents);
		Pool.Return(ref duplicates);
		return result;
	}

	public List<Component> DuplicateComponents(List<Component> sourceComponents, bool breakExternalReferences)
	{
		List<Component> components = new List<Component>();
		DuplicateComponents(sourceComponents, breakExternalReferences, components);
		return components;
	}

	public void DuplicateComponents(List<Component> sourceComponents, bool breakExternalReferences, List<Component> duplicates)
	{
		InternalReferences internalRefs = new InternalReferences();
		HashSet<ISyncRef> breakRefs = Pool.BorrowHashSet<ISyncRef>();
		HashSet<Slot> collectedSlots = Pool.BorrowHashSet<Slot>();
		HashSet<Slot> hierarchy = Pool.BorrowHashSet<Slot>();
		foreach (Component component in sourceComponents)
		{
			if (!hierarchy.Contains(component.Slot))
			{
				component.Slot.GenerateHierarchy(hierarchy);
			}
		}
		foreach (Component component2 in sourceComponents)
		{
			collectedSlots.Add(component2.Slot);
			component2.Slot.CollectInternalReferences(component2.Slot, component2, internalRefs, breakRefs, hierarchy);
		}
		if (!breakExternalReferences)
		{
			breakRefs.Clear();
		}
		foreach (Slot slot in collectedSlots)
		{
			internalRefs.RegisterCopy(slot, this);
			for (int i = 0; i < base.SyncMemberCount; i++)
			{
				if (!InitInfo.syncMemberDontCopy[i] && !(slot.GetSyncMember(i) is WorkerBag<Component>))
				{
					internalRefs.RegisterCopy(slot.GetSyncMember(i), GetSyncMember(i));
				}
			}
		}
		foreach (Component c in sourceComponents)
		{
			Component copyComponent = AttachComponent(c.GetType(), runOnAttachBehavior: false);
			internalRefs.RegisterCopy(c, copyComponent);
			copyComponent.CopyValues(c, delegate(ISyncMember from, ISyncMember to)
			{
				Worker.MemberCopy(from, to, internalRefs, breakRefs, checkTypes: false);
			});
			duplicates.Add(copyComponent);
		}
		internalRefs.TransferReferences(preserveMissingTargets: true);
		foreach (Component duplicate in duplicates)
		{
			duplicate.RunDuplicate();
		}
		internalRefs.Dispose();
		Pool.Return(ref breakRefs);
		Pool.Return(ref collectedSlots);
		Pool.Return(ref hierarchy);
	}

	public void GenerateHierarchy(HashSet<Slot> set, bool includeLocal = false)
	{
		set.Add(this);
		foreach (Slot child in Children)
		{
			child.GenerateHierarchy(set, includeLocal);
		}
		if (!includeLocal)
		{
			return;
		}
		foreach (Slot localChild in LocalChildren)
		{
			localChild.GenerateHierarchy(set, includeLocal);
		}
	}

	private Slot InternalDuplicate(Slot target, InternalReferences internalRefs, HashSet<ISyncRef> breakRefs, DuplicationSettings settings, bool duplicateAsLocal)
	{
		if (ActiveUserRoot != null && ActiveUserRoot.Slot == this)
		{
			return null;
		}
		Slot copy = ((!duplicateAsLocal || target.IsLocalElement) ? target.AddSlot(Name) : target.AddLocalSlot(Name));
		internalRefs.RegisterCopy(this, copy);
		settings?.RegisterCopy(this, copy);
		copy.CopyValues(this, delegate(ISyncMember from, ISyncMember to)
		{
			if (from == ParentReference)
			{
				internalRefs.RegisterCopy(from, to);
			}
			else if (!(from is WorkerBag<Component>))
			{
				Worker.MemberCopy(from, to, internalRefs, breakRefs, checkTypes: false);
			}
		});
		foreach (Component c in base.Components)
		{
			if (c.DontDuplicate || (settings != null && settings.ComponentFilter?.Invoke(c) == false))
			{
				continue;
			}
			Type componentType = c.GetType();
			bool typeChanged = false;
			if (settings != null && settings.TypeRemapper != null)
			{
				Type newType = settings.TypeRemapper(componentType);
				if (newType != componentType)
				{
					componentType = newType;
					typeChanged = true;
				}
			}
			if (!(componentType == null))
			{
				Component copyComponent = copy.AttachComponent(componentType, runOnAttachBehavior: false);
				internalRefs.RegisterCopy(c, copyComponent);
				copyComponent.CopyValues(c, delegate(ISyncMember from, ISyncMember to)
				{
					Worker.MemberCopy(from, to, internalRefs, breakRefs, typeChanged);
				}, typeChanged);
			}
		}
		if (ChildrenCount > 0)
		{
			foreach (Slot ch in _children)
			{
				if (settings?.SlotFilter?.Invoke(ch) ?? true)
				{
					ch.InternalDuplicate(copy, internalRefs, breakRefs, settings, duplicateAsLocal);
				}
			}
		}
		return copy;
	}

	private void CollectInternalReferences(Slot root, InternalReferences internalRefs, HashSet<ISyncRef> breakRefs, HashSet<Slot> hierarchy)
	{
		foreach (Component component in base.Components)
		{
			if (!component.DontDuplicate)
			{
				CollectInternalReferences(root, component, internalRefs, breakRefs, hierarchy);
			}
		}
		if (ChildrenCount <= 0)
		{
			return;
		}
		foreach (Slot child in _children)
		{
			child.CollectInternalReferences(root, internalRefs, breakRefs, hierarchy);
		}
	}

	private void CollectInternalReferences(Slot root, Component component, InternalReferences internalRefs, HashSet<ISyncRef> breakRefs, HashSet<Slot> hierarchy)
	{
		List<ISyncRef> references = Pool.BorrowList<ISyncRef>();
		component.GetSyncMembers(references, skipDontCopy: true);
		foreach (ISyncRef syncRef in references)
		{
			if (syncRef.Target == null)
			{
				if (syncRef.Value != RefID.Null)
				{
					breakRefs.Add(syncRef);
				}
				continue;
			}
			Slot targetSlot = syncRef.Target?.FindNearestParent<Slot>();
			if (hierarchy.Contains(targetSlot))
			{
				internalRefs.AddPair(syncRef, syncRef.Target);
			}
			else if (syncRef is ILinkRef)
			{
				breakRefs.Add(syncRef);
			}
		}
		Pool.Return(ref references);
	}

	/// <summary>
	/// Sets the Tag of the Slot and all of its chlidren recursively
	/// </summary>
	/// <param name="tag">The tag to set</param>
	public void TagHierarchy(string tag)
	{
		Tag = tag;
		if (ChildrenCount <= 0)
		{
			return;
		}
		foreach (Slot child in _children)
		{
			child.TagHierarchy(tag);
		}
	}

	/// <summary>
	/// Gets list of all slots (including this one) with a specific tag
	/// </summary>
	/// <param name="tag">The tag to check for</param>
	/// <returns>The list of all children with the given tag</returns>
	public List<Slot> GetChildrenWithTag(string tag)
	{
		List<Slot> list = new List<Slot>();
		GetChildrenWithTag(tag, list);
		return list;
	}

	/// <summary>
	/// Fills the list with all children with given tag
	/// </summary>
	/// <param name="tag">The tag to check for</param>
	/// <param name="children">The list which to fill with the found slots</param>
	public void GetChildrenWithTag(string tag, List<Slot> children)
	{
		if (Tag == tag)
		{
			children.Add(this);
		}
		if (ChildrenCount <= 0)
		{
			return;
		}
		foreach (Slot child in _children)
		{
			child.GetChildrenWithTag(tag, children);
		}
	}

	public override void EndInitPhase()
	{
		if (_membersInInitPhase)
		{
			EndInitializationStageForMembers();
			_membersInInitPhase = false;
		}
		base.EndInitPhase();
	}

	private void NewParentSlotAvailable(SyncRef<Slot> reference)
	{
		if (base.IsDestroyed)
		{
			return;
		}
		if (IsRootSlot)
		{
			UniLog.Warning("Tried to assign root slot parent. This is not permitted. NewParent: " + reference.Target);
			base.World.RunSynchronously(delegate
			{
				reference.Target = null;
			});
			return;
		}
		Slot restoreParent = _currentParent;
		if (restoreParent == null || restoreParent.IsDestroyed)
		{
			restoreParent = base.World.RootSlot;
		}
		if (IsProtected && _currentParent != null)
		{
			RunSynchronously(delegate
			{
				reference.Target = restoreParent;
			});
			return;
		}
		Slot newParent = reference.Target;
		if (newParent == null)
		{
			UniLog.Warning("New Parent is null, resetting to the root slot.");
			base.World.RunSynchronously(delegate
			{
				reference.Target = base.World.RootSlot;
			});
		}
		else
		{
			if (newParent == _currentParent)
			{
				return;
			}
			if (newParent.IsChildOf(this, includeSelf: true))
			{
				UniLog.Warning("New Parent is child of the current one, reverting to the old one or root.");
				Slot resetParent = base.World.RootSlot;
				if (!restoreParent.IsChildOf(this, includeSelf: true))
				{
					resetParent = restoreParent;
				}
				base.World.RunSynchronously(delegate
				{
					reference.Target = resetParent;
				});
				return;
			}
			Slot lastParent = _currentParent;
			if (_currentParent != null)
			{
				_currentParent.InformChildRemoved(this);
			}
			newParent.InformChildAdded(this);
			_currentParent = newParent;
			MarkChangeDirty();
			SlotTransformChanged(scaleChanged: true);
			UpdatePersistenceHierarchy();
			UpdateActiveHierarchy();
			UpdateChangedEventHierarchy();
			UpdateUserRootFromParent();
			this.ParentChanged?.Invoke(this);
			if (IsRenderable)
			{
				newParent.IncrementRenderable();
				if (IsRenderTransformAllocated)
				{
					base.World.Render.Transforms.RegisterParentUpdate(this);
				}
			}
			if (lastParent != null)
			{
				if (!base.IsLocalElement || lastParent.IsLocalElement)
				{
					lastParent.ChildRemoved?.Invoke(lastParent, this);
				}
				if (IsRenderable)
				{
					lastParent.DecrementRenderable();
				}
			}
			if (!base.IsLocalElement || newParent.IsLocalElement)
			{
				newParent.ChildAdded?.Invoke(newParent, this);
			}
		}
	}

	internal void InformChildRemoved(Slot slot)
	{
		if (slot.IsLocalElement && !base.IsLocalElement)
		{
			_localChildren.Remove(slot);
		}
		else
		{
			_children.Remove(slot);
			InvalidateChildrenOrder();
		}
		MarkChangeDirty();
	}

	internal void InformChildAdded(Slot slot)
	{
		if (slot.IsLocalElement && !base.IsLocalElement)
		{
			_localChildren.Add(slot);
			if (base.IsInInitPhase)
			{
				childInitializables.Add(slot);
			}
		}
		else
		{
			_children.Add(slot);
			InvalidateChildrenOrder();
			if (base.IsInInitPhase)
			{
				childInitializables.Add(slot);
			}
		}
		MarkChangeDirty();
	}

	/// <summary>
	/// Sets the local transform to identity with its parent.
	/// </summary>
	public void SetIdentityTransform()
	{
		LocalPosition = float3.Zero;
		LocalRotation = floatQ.Identity;
		LocalScale = float3.One;
	}

	/// <summary>
	/// Transforms this slot using other slot, as if it was its child. Only this slot is assigned,
	/// the other slot's transform is unchanged
	/// </summary>
	/// <param name="other">The slot to transform by</param>
	/// <param name="globalPosition">The new virtual global position of the other slot</param>
	/// <param name="globalRotation">The new virtual global rotation of the other slot</param>
	/// <param name="globalScale">Thew virtual global scale of the other slot</param>
	public void TransformByAnother(Slot other, in float3 globalPosition, in floatQ globalRotation, in float3 globalScale)
	{
		LocalToGlobal = GetTransformedByAnother(other, in globalPosition, GlobalRotation, in globalScale);
	}

	public float4x4 GetTransformedByAnother(Slot other, in float3 globalPosition, in floatQ globalRotation, in float3 globalScale)
	{
		return float4x4.Transform(in globalPosition, in globalRotation, in globalScale).MultiplyAffineFast(other.GlobalToLocal_Fast.MultiplyAffineFast(in LocalToGlobal_Fast));
	}

	/// <summary>
	///  Copies to global position, rotation and scale of the other slot
	/// </summary>
	/// <param name="slot"></param>
	public void CopyTransform(Slot slot)
	{
		if (slot.Parent == Parent)
		{
			LocalPosition = slot.LocalPosition;
			LocalRotation = slot.LocalRotation;
			LocalScale = slot.LocalScale;
		}
		else
		{
			TRS = Parent.GlobalToLocal_Fast.MultiplyAffineFast(in slot.LocalToGlobal_Fast);
		}
	}

	public void CopyPositionRotation(Slot slot)
	{
		GlobalPosition = slot.GlobalPosition;
		GlobalRotation = slot.GlobalRotation;
	}

	private void InvalidateLocal2Global(bool invalidateScale)
	{
		int flags = (invalidateScale ? 62 : 30);
		InvalidateLocal2Global(flags);
	}

	private void InvalidateLocal2Global(int flags)
	{
		if ((_transformElementValid & flags) == 0)
		{
			return;
		}
		_transformElementValid &= ~flags;
		if (!_isUniformScale)
		{
			flags = 62;
		}
		foreach (Slot child in _children)
		{
			child.InvalidateLocal2Global(flags);
		}
		foreach (Slot localChild in _localChildren)
		{
			localChild.InvalidateLocal2Global(flags);
		}
	}

	private void EnsureValidGlobalToLocal()
	{
		if ((_transformElementValid & 4) == 0)
		{
			float4x4.SetAffineInverseFast(ref LocalToGlobal_Fast, out _cachedGlobal2Local);
			_transformElementValid |= 4;
		}
	}

	private void EnsureValidTRS()
	{
		if ((_transformElementValid & 1) == 0)
		{
			float4x4.SetTransform(Position_Field.Value, Rotation_Field.Value, Scale_Field.Value, out _cachedTRS);
			_transformElementValid |= 1;
		}
	}

	/// <summary>
	/// Computes a transformation matrix from the slot's local coordinate space to another slots'
	/// local coordinate space.
	/// </summary>
	/// <param name="space">The slot that the matrix should transform into</param>
	/// <returns>Transformation matrix</returns>
	public float4x4 GetLocalToSpaceMatrix(Slot space)
	{
		return space.GlobalToLocal_Fast.MultiplyAffineFast(in LocalToGlobal_Fast);
	}

	private void EnsureValidLocal2Global()
	{
		if ((_transformElementValid & 2) == 0)
		{
			float4x4.MultiplyAffineFast(ref Parent.LocalToGlobal_Fast, ref TRS_Fast, out _cachedLocal2Global);
			_transformElementValid |= 2;
		}
	}

	private void EnsureValidLocalToGlobalScale()
	{
		if ((_transformElementValid & 0x20) == 0)
		{
			Parent.EnsureValidLocalToGlobalScale();
			_cachedLocal2GlobalScale = LocalToGlobal_Fast.DecomposedScale;
			_transformElementValid |= 32;
		}
	}

	/// <summary>
	/// Transforms a positional coordinate from slot's local space to global
	/// </summary>
	/// <param name="localPoint">Point in the slot's local coordinate system</param>
	/// <returns>Point the global coordinate system</returns>
	public float3 LocalPointToGlobal(in float3 localPoint)
	{
		if (IsRootSlot)
		{
			return localPoint;
		}
		return LocalToGlobal_Fast.TransformPoint3x4(in localPoint);
	}

	/// <summary>
	/// Transforms a positional coordinate from global space to slot's local
	/// </summary>
	/// <param name="globalPoint">Point in the global coordinate system</param>
	/// <returns>Point in the slot's local coordinate system</returns>
	public float3 GlobalPointToLocal(in float3 globalPoint)
	{
		if (IsRootSlot)
		{
			return globalPoint;
		}
		return GlobalToLocal_Fast.TransformPoint3x4(in globalPoint);
	}

	/// <summary>
	/// Transforms a direction of a vector from the slot's local coordinate space to the global.
	/// This rotates and inverts the vector, but preserves its length (unit input vector will
	/// output unit output vector, regardless of scaling)
	/// </summary>
	/// <param name="localDirection">Directional vector in the slot's local coodinate space</param>
	/// <returns>Directional vector in the global coordinate space</returns>
	public float3 LocalDirectionToGlobal(in float3 localDirection)
	{
		if (IsRootSlot)
		{
			return localDirection;
		}
		return LocalToGlobal_Fast.TransformDirection(in localDirection);
	}

	/// <summary>
	/// Transforms a direction of a vector from the global coordinate space to the slot's local.
	/// This rotates and inverts the vector, but preserves its length (unit input vector will
	/// output unit output vector, regardless of scaling)
	/// </summary>
	/// <param name="globalDirection">Directional vector in the global coordinate space</param>
	/// <returns>Directional vector in the slot's local coordinate space</returns>
	public float3 GlobalDirectionToLocal(in float3 globalDirection)
	{
		if (IsRootSlot)
		{
			return globalDirection;
		}
		return GlobalToLocal_Fast.TransformDirection(in globalDirection);
	}

	/// <summary>
	/// Transforms a vector from the global coordinate space to the slot's local.
	/// This rotates and scales the vector, but the position of the slots doesn't affect it.
	/// </summary>
	/// <param name="globalVector">Vector in the global coordinate space</param>
	/// <returns>Vector in the slot's local coordinate space</returns>
	public float3 GlobalVectorToLocal(in float3 globalVector)
	{
		if (IsRootSlot)
		{
			return globalVector;
		}
		return GlobalToLocal_Fast.TransformVector(in globalVector);
	}

	/// <summary>
	/// Transforms a vector from the slot's local coordinate space to global.
	/// This rotates and scales the vector, but the position of the slots doesn't affect it.
	/// </summary>
	/// <param name="localVector">Vector in the local coordinate space</param>
	/// <returns>Vector in the global coordinate space</returns>
	public float3 LocalVectorToGlobal(in float3 localVector)
	{
		if (IsRootSlot)
		{
			return localVector;
		}
		return LocalToGlobal_Fast.TransformVector(in localVector);
	}

	public floatQ LocalRotationToGlobal(in floatQ localRotation)
	{
		if (IsRootSlot)
		{
			return localRotation;
		}
		return LocalToGlobalQuaternion * localRotation;
	}

	public floatQ GlobalRotationToLocal(in floatQ globalRotation)
	{
		if (IsRootSlot)
		{
			return globalRotation;
		}
		return floatQ.InvertedMultiply(LocalToGlobalQuaternion, in globalRotation);
	}

	public float3 LocalScaleToGlobal(in float3 localScale)
	{
		if (IsRootSlot)
		{
			return localScale;
		}
		return localScale * LocalToGlobalScale;
	}

	public float3 GlobalScaleToLocal(in float3 globalScale)
	{
		if (IsRootSlot)
		{
			return globalScale;
		}
		return globalScale / LocalToGlobalScale;
	}

	public float LocalScaleToGlobal(float localScale)
	{
		if (IsRootSlot)
		{
			return localScale;
		}
		return MathX.AvgComponent(LocalScaleToGlobal(new float3(localScale, localScale, localScale)));
	}

	public float GlobalScaleToLocal(float globalScale)
	{
		if (IsRootSlot)
		{
			return globalScale;
		}
		return MathX.AvgComponent(GlobalScaleToLocal(new float3(globalScale, globalScale, globalScale)));
	}

	public float3 LocalPointToParent(in float3 localPoint)
	{
		if (IsRootSlot)
		{
			return localPoint;
		}
		return TRS.TransformPoint3x4(in localPoint);
	}

	public float3 ParentPointToLocal(in float3 parentPoint)
	{
		if (IsRootSlot)
		{
			return parentPoint;
		}
		return TRS.AffineInverseFast.TransformPoint3x4(in parentPoint);
	}

	public float3 LocalDirectionToParent(in float3 localDirection)
	{
		if (IsRootSlot)
		{
			return localDirection;
		}
		return LocalRotation * localDirection;
	}

	public float3 ParentDirectionToLocal(in float3 parentDirection)
	{
		if (IsRootSlot)
		{
			return parentDirection;
		}
		return floatQ.InvertedMultiply(LocalRotation, in parentDirection);
	}

	public float3 LocalVectorToParent(in float3 localVector)
	{
		if (IsRootSlot)
		{
			return localVector;
		}
		return TRS.TransformVector(in localVector);
	}

	public float3 ParentVectorToLocal(in float3 parentVector)
	{
		if (IsRootSlot)
		{
			return parentVector;
		}
		return TRS.AffineInverseFast.TransformVector(in parentVector);
	}

	public floatQ LocalRotationToParent(in floatQ localRotation)
	{
		if (IsRootSlot)
		{
			return localRotation;
		}
		return LocalRotation * localRotation;
	}

	public floatQ ParentRotationToLocal(in floatQ parentRotation)
	{
		if (IsRootSlot)
		{
			return parentRotation;
		}
		return floatQ.InvertedMultiply(LocalRotation, in parentRotation);
	}

	public float3 LocalPointToSpace(in float3 localPoint, Slot space)
	{
		if (space == this)
		{
			return localPoint;
		}
		if (space == Parent)
		{
			return LocalPointToParent(in localPoint);
		}
		return space.GlobalPointToLocal(LocalPointToGlobal(in localPoint));
	}

	public float3 SpacePointToLocal(in float3 spacePoint, Slot space)
	{
		if (space == this)
		{
			return spacePoint;
		}
		if (space == Parent)
		{
			return ParentPointToLocal(in spacePoint);
		}
		return GlobalPointToLocal(space.LocalPointToGlobal(in spacePoint));
	}

	public float3 LocalDirectionToSpace(in float3 localDirection, Slot space)
	{
		if (space == this)
		{
			return localDirection;
		}
		return space.GlobalDirectionToLocal(LocalDirectionToGlobal(in localDirection));
	}

	public float3 SpaceDirectionToLocal(in float3 spaceDirection, Slot space)
	{
		if (space == this)
		{
			return spaceDirection;
		}
		return GlobalDirectionToLocal(space.LocalDirectionToGlobal(in spaceDirection));
	}

	public float3 LocalVectorToSpace(in float3 localVector, Slot space)
	{
		if (space == this)
		{
			return localVector;
		}
		return space.GlobalVectorToLocal(LocalVectorToGlobal(in localVector));
	}

	public float3 LocalScaleToSpace(in float3 localScale, Slot space)
	{
		if (space == this)
		{
			return localScale;
		}
		return space.GlobalScaleToLocal(LocalScaleToGlobal(in localScale));
	}

	public float LocalScaleToSpace(float localScale, Slot space)
	{
		if (space == this)
		{
			return localScale;
		}
		return space.GlobalScaleToLocal(LocalScaleToGlobal(localScale));
	}

	public float3 LocalScaleToParent(in float3 localScale)
	{
		if (IsRootSlot)
		{
			return localScale;
		}
		return TRS_Fast.DecomposedScale * localScale;
	}

	public float LocalScaleToParent(in float localScale)
	{
		if (IsRootSlot)
		{
			return localScale;
		}
		return MathX.AvgComponent(TRS_Fast.DecomposedScale * new float3(localScale, localScale, localScale));
	}

	public float3 SpaceVectorToLocal(in float3 spaceVector, Slot space)
	{
		if (space == this)
		{
			return spaceVector;
		}
		return GlobalVectorToLocal(space.LocalVectorToGlobal(in spaceVector));
	}

	public float3 SpaceScaleToLocal(in float3 spaceScale, Slot space)
	{
		if (space == this)
		{
			return spaceScale;
		}
		return GlobalScaleToLocal(space.LocalScaleToGlobal(in spaceScale));
	}

	public float SpaceScaleToLocal(float spaceScale, Slot space)
	{
		if (space == this)
		{
			return spaceScale;
		}
		return GlobalScaleToLocal(space.LocalScaleToGlobal(spaceScale));
	}

	public floatQ LocalRotationToSpace(in floatQ localRotation, Slot space)
	{
		if (space == this)
		{
			return localRotation;
		}
		return space.GlobalRotationToLocal(LocalRotationToGlobal(in localRotation));
	}

	public floatQ SpaceRotationToLocal(in floatQ spaceRotation, Slot space)
	{
		if (space == this)
		{
			return spaceRotation;
		}
		return GlobalRotationToLocal(space.LocalRotationToGlobal(in spaceRotation));
	}

	public void InternalRunApplyChanges(int changeUpdateIndex)
	{
		IsChangeDirty = false;
		_synchronousChangeScheduled = false;
		LastChangeUpdateIndex = changeUpdateIndex;
	}

	public void MarkChangeDirty()
	{
		World _world = base.World;
		if (_world == null || IsChangeDirty || !IsStarted)
		{
			return;
		}
		if (!_world.CanCurrentThreadModify)
		{
			if (!_synchronousChangeScheduled)
			{
				_synchronousChangeScheduled = true;
				RunSynchronously(MarkChangeDirty);
			}
			return;
		}
		IsChangeDirty = true;
		if (!base.IsDestroyed)
		{
			_world?.UpdateManager.Changed(this);
		}
		this._changed?.Invoke(this);
	}

	internal void RunOnPaste()
	{
		foreach (Component component in base.Components)
		{
			component.RunOnPaste();
		}
		foreach (Slot child in Children)
		{
			child.RunOnPaste();
		}
	}

	internal override void RunOnSaving(SaveControl control)
	{
		OnSaving(control);
		foreach (Component component in base.Components)
		{
			component.RunOnSaving(control);
		}
		foreach (Slot child in Children)
		{
			child.RunOnSaving(control);
		}
	}

	public void RunSynchronously(Action action, bool immediatellyIfPossible = false)
	{
		base.World?.RunSynchronously(action, immediatellyIfPossible, this);
	}

	public Coroutine RunInSeconds(float seconds, Action action)
	{
		return base.World.RunInSeconds(seconds, WrapSynchronousAction(action));
	}

	public Coroutine RunInUpdates(int updates, Action action)
	{
		return base.World.RunInUpdates(updates, WrapSynchronousAction(action));
	}

	protected void RunInBackground(Action action, WorkType workType = WorkType.Background)
	{
		base.World?.RunInBackground(action, workType);
	}

	private Action WrapSynchronousAction(Action action)
	{
		return delegate
		{
			if (!base.IsDisposed && !base.IsDestroyed)
			{
				action();
			}
		};
	}

	public override string ToString()
	{
		string parentName = Parent?.Name ?? "<null>";
		return $"{Name} ({base.ReferenceID}) <T: {LocalPosition}, R: {LocalRotation}, S: {LocalScale}, IsActive: {IsActive}, ActiveSelf: {ActiveSelf}, Started: {IsStarted}, Destroyed: {base.IsDestroyed}, Disposed: {base.IsDisposed}, Local: {base.IsLocalElement}, RenderableCount: {_renderableCount}> Parent: {parentName}";
	}

	public string ParentHierarchyToString()
	{
		string parentString = "";
		Slot _parent = Parent;
		if (_parent != null)
		{
			parentString = _parent.ParentHierarchyToString();
		}
		return ToString() + "\n" + parentString;
	}

	public string ChildrenHierarchyToString(Func<Slot, string> extraMessage = null)
	{
		StringBuilder str = new StringBuilder();
		BuildChildrenHierarchyString(str, extraMessage);
		return str.ToString();
	}

	private void BuildChildrenHierarchyString(StringBuilder str, Func<Slot, string> extraMessage = null, int level = 0)
	{
		for (int i = 0; i < level; i++)
		{
			str.Append("-");
		}
		str.Append(ToString());
		if (extraMessage != null)
		{
			str.Append(" - ");
			str.Append(extraMessage(this));
		}
		str.AppendLine();
		foreach (Slot child in Children)
		{
			child.BuildChildrenHierarchyString(str, extraMessage, level + 1);
		}
	}

	public void BuildInspectorUI(UIBuilder ui)
	{
		WorkerInspector.BuildInspectorUI(this, ui);
		ui.Panel();
		List<RectTransform> list = ui.SplitHorizontally(1f, 1f, 1f);
		ui.NestOut();
		new UIBuilder(list[0]).Text("Inspector.Slot.Axis.X".AsLocaleKey("<color=#f00>{0}"));
		new UIBuilder(list[1]).Text("Inspector.Slot.Axis.Y".AsLocaleKey("<color=#0f0>{0}"));
		new UIBuilder(list[2]).Text("Inspector.Slot.Axis.Z".AsLocaleKey("<color=#00f>{0}"));
		ui.HorizontalLayout(4f);
		ui.Text("Inspector.Slot.Reset.Label".AsLocaleKey("<b>{0}</b>"));
		ui.Button("Inspector.Slot.Reset.Position".AsLocaleKey(), ResetPosition);
		ui.Button("Inspector.Slot.Reset.Rotation".AsLocaleKey(), ResetRotation);
		ui.Button("Inspector.Slot.Reset.Scale".AsLocaleKey(), ResetScale);
		ui.NestOut();
		ui.Button("Inspector.Slot.CreatePivotAtCenter".AsLocaleKey(), OnCreatePivotAtCenter);
		ui.HorizontalLayout(4f);
		ui.Button("Inspector.Slot.JumpTo".AsLocaleKey(), JumpTo);
		ui.Button("Inspector.Slot.BringTo".AsLocaleKey(), BringTo);
		ui.NestOut();
		ui.HorizontalLayout(4f);
		ui.Text("Inspector.Slot.ParentUnder.Label".AsLocaleKey("<b>{0}</b>"));
		ui.PushStyle();
		ui.Style.MinWidth = 160f;
		ui.Button("Inspector.Slot.ParentUnder.LocalUserSpace".AsLocaleKey(), ParentUnderLocalUserSpace);
		ui.Button("Inspector.Slot.ParentUnder.WorldRoot".AsLocaleKey(), ParentUnderWorldRoot);
		ui.PopStyle();
		ui.NestOut();
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ResetPosition(IButton button, ButtonEventData eventData)
	{
		base.World.BeginUndoBatch("Undo.ResetPosition".AsLocaleKey());
		Position_Field.UndoableSet(float3.Zero, forceNew: true);
		base.World.EndUndoBatch();
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ResetRotation(IButton button, ButtonEventData eventData)
	{
		base.World.BeginUndoBatch("Undo.ResetRotation".AsLocaleKey());
		Rotation_Field.UndoableSet(floatQ.Identity, forceNew: true);
		base.World.EndUndoBatch();
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ResetScale(IButton button, ButtonEventData eventData)
	{
		base.World.BeginUndoBatch("Undo.ResetScale".AsLocaleKey());
		Scale_Field.UndoableSet(float3.One, forceNew: true);
		base.World.EndUndoBatch();
	}

	[SyncMethod(typeof(Delegate), null)]
	private void JumpTo(IButton button, ButtonEventData eventData)
	{
		base.LocalUserRoot.JumpToPoint(GlobalPosition, 1f);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void BringTo(IButton button, ButtonEventData eventData)
	{
		this.CreateTransformUndoState(parent: false, position: true, rotation: false, scale: false).Description = this.GetLocalized("Inspector.Slot.BringTo.Undo".AsLocaleKey("name", Name));
		float3? offset = float3.Right;
		this.PositionInFrontOfUser(null, offset, 0.7f, null, scale: false);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ParentUnderWorldRoot(IButton button, ButtonEventData eventData)
	{
		this.CreateTransformUndoState(parent: true).Description = this.GetLocalized("Inspector.Slot.ParentUnder.WorldRoot.Undo".AsLocaleKey("name", Name));
		Parent = base.World.RootSlot;
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ParentUnderLocalUserSpace(IButton button, ButtonEventData eventData)
	{
		this.CreateTransformUndoState(parent: true).Description = this.GetLocalized("Inspector.Slot.ParentUnder.LocalUserSpace.Undo".AsLocaleKey("name", Name));
		Parent = base.LocalUserSpace;
	}

	[SyncMethod(typeof(Delegate), null)]
	private void OnCreatePivotAtCenter(IButton button, ButtonEventData eventData)
	{
		this.CreatePivotAtCenter();
	}

	public SavedGraph SaveObject(DependencyHandling dependencyHandling, bool saveNonPersistent = false, ReferenceTranslator refTranslator = null)
	{
		Slot _origParent = null;
		if (!IsPersistent && !saveNonPersistent)
		{
			if (!PersistentSelf)
			{
				throw new Exception("Cannot save non-persistent objects");
			}
			_origParent = Parent;
			SetParent(base.World.RootSlot, keepGlobalTransform: false);
		}
		DataTreeDictionary dict = new DataTreeDictionary();
		if (refTranslator == null)
		{
			refTranslator = new ReferenceTranslator();
		}
		SaveControl saveControl = new SaveControl(base.World, this, refTranslator, null);
		saveControl.SaveNonPersistent = saveNonPersistent;
		RunOnSaving(saveControl);
		saveControl.StartSaving();
		dict.Add("VersionNumber", FrooxEngine.Engine.Version.ToString());
		dict.Add("FeatureFlags", saveControl.StoreFeatureFlags(base.Engine));
		DataTreeList types = new DataTreeList();
		dict.Add("Types", types);
		DataTreeDictionary typeVersions = new DataTreeDictionary();
		dict.Add("TypeVersions", typeVersions);
		HashSet<IWorldElement> dependencies = null;
		HashSet<Slot> rootHierarchy = Pool.BorrowHashSet<Slot>();
		GenerateHierarchy(rootHierarchy);
		if (dependencyHandling != DependencyHandling.BreakAll)
		{
			DependencyCollection dependencyCollection = new DependencyCollection(this, dependencyHandling == DependencyHandling.CollectAssets, saveNonPersistent, rootHierarchy);
			CollectDependencies(this, in dependencyCollection);
			dependencies = DependencyCollection.GetDependenciesAndReturn(ref dependencyCollection);
		}
		if (dependencyHandling != DependencyHandling.CollectAll)
		{
			if (dependencies != null)
			{
				foreach (IWorldElement item3 in dependencies)
				{
					if (item3 is Slot slot)
					{
						slot.GenerateHierarchy(rootHierarchy);
					}
				}
			}
			saveControl.ReferenceFilter = delegate(RefID r)
			{
				if (r == RefID.Null)
				{
					return r;
				}
				IWorldElement objectOrNull = base.World.ReferenceController.GetObjectOrNull(in r);
				if (objectOrNull == null)
				{
					return RefID.Null;
				}
				IWorldElement worldElement = objectOrNull;
				if (dependencyHandling == DependencyHandling.CollectAssets)
				{
					if (objectOrNull is IAssetProvider)
					{
						return r;
					}
					if (objectOrNull is Component { PreserveWithAssets: not false })
					{
						return r;
					}
					if (dependencies != null)
					{
						Component item = worldElement.FindNearestParent<Component>();
						if (dependencies.Contains(item))
						{
							return r;
						}
					}
					Slot item2 = worldElement.FindNearestParent<Slot>();
					if (rootHierarchy.Contains(item2))
					{
						return r;
					}
				}
				return (!rootHierarchy.Contains(worldElement.FindNearestParent<Slot>())) ? RefID.Null : r;
			};
		}
		dict.Add("Object", Save(saveControl));
		if (dependencies != null && dependencies.Count > 0)
		{
			DataTreeList list = new DataTreeList();
			if (dependencyHandling == DependencyHandling.CollectAssets)
			{
				saveControl.SaveNonPersistent = true;
			}
			foreach (IWorldElement d in dependencies)
			{
				if (dependencyHandling == DependencyHandling.CollectAll)
				{
					list.Add((d as Slot).Save(saveControl));
				}
				else
				{
					list.Add(WorkerSaveLoad.SaveWorker(d as Component, saveControl));
				}
			}
			dict.Add((dependencyHandling == DependencyHandling.CollectAll) ? "Dependencies" : "Assets", list);
		}
		saveControl.StoreTypeData(types, typeVersions);
		if (_origParent != null)
		{
			SetParent(_origParent, keepGlobalTransform: false);
		}
		saveControl.FinishSave();
		if (rootHierarchy != null)
		{
			Pool.Return(ref rootHierarchy);
		}
		if (dependencies != null)
		{
			Pool.Return(ref dependencies);
		}
		return new SavedGraph(dict);
	}

	public void CollectDependencies(Slot source, in DependencyCollection dependencies)
	{
		foreach (Component c in source.Components)
		{
			CollectDependencies(c, in dependencies);
		}
		foreach (Slot child in source.Children)
		{
			CollectDependencies(child, in dependencies);
		}
	}

	public void CollectDependencies(Component source, in DependencyCollection dependencies)
	{
		List<IWorldElement> referencedObjects = Pool.BorrowList<IWorldElement>();
		source.GetReferencedObjects(referencedObjects, dependencies.assetOnly, !dependencies.saveNonPersistent, skipDontCopy: true);
		foreach (IWorldElement r in referencedObjects)
		{
			if (dependencies.IsInRootHierarchy(r))
			{
				continue;
			}
			if (dependencies.assetOnly)
			{
				Component assetProvider = r.FindNearestParent<Component>();
				if (dependencies.AddElement(assetProvider))
				{
					CollectDependencies(assetProvider, in dependencies);
				}
				continue;
			}
			Slot slot = r.FindNearestParent<Slot>();
			if (!dependencies.Contains(slot) && !dependencies.IsInRootHierarchy(slot) && !dependencies.IsInDependencyHierarchy(slot))
			{
				dependencies.AddDependencySlot(slot);
				CollectDependencies(slot, in dependencies);
			}
		}
		Pool.Return(ref referencedObjects);
	}

	public void LoadObject(DataTreeDictionary node, IRecord record, Slot assetsRoot = null, Predicate<Type> typeFilter = null, ReferenceTranslator refTranslator = null, Func<DataTreeNode, DataTreeNode> loadPostprocessObject = null)
	{
		if (base.IsDestroyed)
		{
			throw new InvalidOperationException("Cannot load object on a destroyed slot");
		}
		if (refTranslator == null)
		{
			refTranslator = new ReferenceTranslator();
		}
		LoadControl loadControl = new LoadControl(base.World, refTranslator, default(VersionNumber), record, typeFilter);
		loadControl.SetLoadRoot(this);
		loadControl.TryLoadVersion(node);
		DataTreeDictionary featureFlags = node.TryGetDictionary("FeatureFlags");
		if (featureFlags != null)
		{
			loadControl.LoadFeatureFlags(featureFlags);
		}
		loadControl.InitializeLoaders();
		DataTreeList types = node.TryGetList("Types");
		DataTreeDictionary typeVersions = node.TryGetDictionary("TypeVersions");
		loadControl.LoadTypeData(types, typeVersions);
		DataTreeNode objectNode = node["Object"];
		objectNode = loadPostprocessObject?.Invoke(objectNode) ?? objectNode;
		Load(objectNode, loadControl);
		DataTreeList dependencies = node.TryGetList("Dependencies");
		DataTreeList assets = node.TryGetList("Assets");
		if (dependencies != null)
		{
			Slot droot = base.World.RootSlot.AddSlot(Name + " - Dependencies");
			foreach (DataTreeNode d in dependencies)
			{
				droot.AddSlot().Load(d, loadControl);
			}
		}
		if (assets != null)
		{
			Slot aroot = assetsRoot ?? base.World.AssetsSlot.AddSlot(Name + " - Assets");
			foreach (DataTreeNode item in assets)
			{
				WorkerSaveLoad.WorkerData data = WorkerSaveLoad.ExtractWorker(item, loadControl);
				if (data.IsValid)
				{
					aroot.AttachComponent(data.workerType, runOnAttachBehavior: false).Load(data.loadNode, loadControl);
					continue;
				}
				MissingComponent missingComponent = aroot.AttachComponent<MissingComponent>();
				missingComponent.Type.Value = data.workerTypename;
				missingComponent.Data.FromRawDataTreeNode(data.loadNode);
			}
		}
		loadControl.FinishLoad();
	}

	public async Task<bool> LoadObjectAsync(Uri uri, Slot assetsRoot = null, ReferenceTranslator refTranslator = null, bool preserveRootTransform = false, bool skipHolder = false, Predicate<Type> typeFilter = null)
	{
		return await StartTask(async () => await LoadObjectTask(uri, null, assetsRoot, refTranslator, preserveRootTransform, skipHolder, typeFilter));
	}

	public async Task<bool> LoadObjectAsync(IRecord record, Slot assetsRoot = null, ReferenceTranslator refTranslator = null, bool preserveRootTransform = false, bool skipHolder = false, Predicate<Type> typeFilter = null)
	{
		return await StartTask(async () => await LoadObjectTask(null, record, assetsRoot, refTranslator, preserveRootTransform, skipHolder, typeFilter));
	}

	private async Task<bool> LoadObjectTask(Uri uri, IRecord record, Slot assetsRoot, ReferenceTranslator refTranslator = null, bool preserveRootTransform = false, bool skipHolder = false, Predicate<Type> typeFilter = null)
	{
		if (uri == null && record == null)
		{
			throw new ArgumentNullException("Both uri and record are null");
		}
		if (uri != null && record != null)
		{
			throw new ArgumentException("Both uri and record cannot be null");
		}
		if (record != null)
		{
			UniLog.Log($"Loading from record: {record}");
		}
		else
		{
			UniLog.Log($"Loading from URI: {uri}");
		}
		Engine _engine = base.Engine;
		await default(ToBackground);
		if (uri != null && uri.Scheme == base.Cloud.Platform.RecordScheme)
		{
			CloudResult<Record> result = await _engine.Cloud.Records.GetRecordCached<Record>(uri).ConfigureAwait(continueOnCapturedContext: false);
			if (result.IsError)
			{
				return false;
			}
			record = result.Entity;
		}
		if (record != null)
		{
			uri = new Uri(record.AssetURI);
		}
		string file = await _engine.AssetManager.GatherAssetFile(uri, 0f);
		if (file == null || !File.Exists(file))
		{
			return false;
		}
		DataTreeDictionary node = DataTreeConverter.Load(file, uri);
		await default(ToWorld);
		if (base.IsDestroyed)
		{
			return false;
		}
		float3 pos = LocalPosition;
		floatQ rot = LocalRotation;
		float3 scl = LocalScale;
		Func<DataTreeNode, DataTreeNode> postProcess = null;
		if (skipHolder)
		{
			postProcess = SkipHolder;
		}
		UniLog.Log($"Loading object from record: {record}");
		Slot slot = this;
		IRecord record2 = record;
		Func<DataTreeNode, DataTreeNode> loadPostprocessObject = postProcess;
		slot.LoadObject(node, record2, assetsRoot, typeFilter, refTranslator, loadPostprocessObject);
		if (preserveRootTransform)
		{
			LocalPosition = pos;
			LocalRotation = rot;
			LocalScale = scl;
		}
		return true;
	}

	private static DataTreeNode SkipHolder(DataTreeNode rootNode)
	{
		DataTreeDictionary dict = (DataTreeDictionary)rootNode;
		if (!((dict.TryGetNode("Name") ?? dict.TryGetNode("NameField")) is DataTreeDictionary nameNode))
		{
			return rootNode;
		}
		if (!(nameNode.TryGetNode("Data") is DataTreeValue valueNode))
		{
			return rootNode;
		}
		if (valueNode.Extract<string>() != "Holder")
		{
			return rootNode;
		}
		DataTreeList childrenList = (DataTreeList)dict["Children"];
		if (childrenList.Count != 1)
		{
			return rootNode;
		}
		return childrenList[0];
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		NameField = new Sync<string>();
		ParentReference = new SyncRef<Slot>();
		tag = new Sync<string>();
		ActiveSelf_Field = new Sync<bool>();
		persistent = new Sync<bool>();
		persistent.MarkNonPersistent();
		Position_Field = new Sync<float3>();
		Rotation_Field = new Sync<floatQ>();
		Scale_Field = new Sync<float3>();
		_orderOffset = new Sync<long>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => componentBag, 
			1 => NameField, 
			2 => ParentReference, 
			3 => tag, 
			4 => ActiveSelf_Field, 
			5 => persistent, 
			6 => Position_Field, 
			7 => Rotation_Field, 
			8 => Scale_Field, 
			9 => _orderOffset, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}
}
