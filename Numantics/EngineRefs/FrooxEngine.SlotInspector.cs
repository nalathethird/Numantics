// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.SlotInspector
using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

public class SlotInspector : Component
{
	protected readonly RelayRef<SyncRef<Slot>> _selectionReference;

	protected readonly SyncRef<Slot> _rootSlot;

	protected readonly SyncRef<Slot> _childContainer;

	protected readonly Sync<int> _depth;

	protected readonly SyncRef<Expander> _expander;

	protected readonly SyncRef<TextExpandIndicator> _expanderIndicator;

	protected readonly SyncRef<Text> _slotNameText;

	private Slot _setupRoot;

	private bool _childrenValid;

	public void Setup(Slot target, SyncRef<Slot> selectionReference, int depth = 0)
	{
		_selectionReference.Target = selectionReference;
		_rootSlot.Target = target;
		_depth.Value = depth;
		OnChanges();
	}

	protected override void OnAttach()
	{
		VerticalLayout verticalLayout = base.Slot.AttachComponent<VerticalLayout>();
		verticalLayout.ForceExpandHeight.Value = false;
		verticalLayout.ChildAlignment = Alignment.TopLeft;
	}

	private void UpdateText()
	{
		base.Slot.OrderOffset = _setupRoot.OrderOffset;
		_slotNameText.Target.Content.Value = _setupRoot.Name;
		colorX c = RadiantUI_Constants.TEXT_COLOR;
		if (!_setupRoot.IsPersistent)
		{
			c = (_setupRoot.PersistentSelf ? RadiantUI_Constants.MidLight.ORANGE : RadiantUI_Constants.Hero.ORANGE);
		}
		if (_selectionReference.Target?.Target == _setupRoot)
		{
			c = RadiantUI_Constants.Hero.YELLOW;
		}
		if (!_setupRoot.IsActive)
		{
			c = (_setupRoot.ActiveSelf ? c.SetA(0.5f) : c.SetA(0.3f));
		}
		_slotNameText.Target.Color.Value = c;
	}

	private void UnregisterRoot()
	{
		if (_setupRoot != null)
		{
			_setupRoot.ChildAdded -= OnChildAdded;
			_setupRoot.ChildRemoved -= OnChildRemoved;
			_setupRoot.ActiveChanged -= OnActiveChanged;
			_setupRoot.PersistentChanged -= OnPersistentChanged;
			_setupRoot.NameChanged -= OnTargetNameChanged;
			_setupRoot.OrderOffsetChanged -= OnOrderOffsetChanged;
			_setupRoot = null;
		}
	}

	protected override void OnChanges()
	{
		if (_setupRoot != null && _rootSlot.Target == _setupRoot && _slotNameText.Target != null)
		{
			UpdateText();
		}
		if (!base.World.IsAuthority)
		{
			return;
		}
		if (_rootSlot.Target != _setupRoot)
		{
			_childrenValid = false;
			UnregisterRoot();
			base.Slot.DestroyChildren();
			_setupRoot = _rootSlot.Target;
			if (_setupRoot != null)
			{
				UIBuilder ui = new UIBuilder(base.Slot);
				RadiantUI_Constants.SetupEditorStyle(ui);
				ui.Style.ForceExpandHeight = false;
				ui.Style.ChildAlignment = Alignment.TopLeft;
				ui.Style.RequireLockInToPress = true;
				ui.HorizontalLayout(4f).PaddingBottom.Value = 4f;
				ui.Style.MinHeight = 32f;
				ui.Style.MinWidth = 32f;
				Button expandButton = ui.Button();
				Expander expander = expandButton.Slot.AttachComponent<Expander>();
				TextExpandIndicator expanderIndicator = expandButton.Slot.AttachComponent<TextExpandIndicator>();
				expanderIndicator.CustomEmptyCheck.Target = IsTargetEmpty;
				_expander.Target = expander;
				_expander.Target.ExpandedChanged += delegate
				{
					InvalidateChildren();
				};
				_expanderIndicator.Target = expanderIndicator;
				ui.Style.FlexibleWidth = 100f;
				Text nameText = ui.Text((LocaleString)null, bestFit: true, Alignment.MiddleLeft);
				Button button = nameText.Slot.AttachComponent<Button>();
				InteractionElement.ColorDriver colorDriver = button.ColorDrivers.Add();
				colorDriver.ColorDrive.Target = nameText.Color;
				RadiantUI_Constants.SetupLabelDriverColors(colorDriver);
				_slotNameText.Target = nameText;
				UpdateText();
				button.Slot.AttachComponent<SlotRecord>().TargetSlot.Target = _rootSlot;
				ui.Style.FlexibleWidth = -1f;
				ui.BooleanMemberEditor(_rootSlot.Target.ActiveSelf_Field);
				ui.NestOut();
				ui.Style.MinHeight = -1f;
				HorizontalLayout childrenSection = ui.HorizontalLayout(4f);
				ui.Style.MinWidth = 32f;
				ui.Empty("Spacer");
				ui.Style.FlexibleWidth = 100f;
				_childContainer.Target = ui.VerticalLayout().Slot;
				expander.SectionRoot.Target = childrenSection.Slot;
				expanderIndicator.Text.Target = expandButton.Slot.GetComponentInChildren<Text>().Content;
				expanderIndicator.SectionRoot.Target = childrenSection.Slot;
				expanderIndicator.ChildrenRoot.Target = _childContainer.Target;
				base.Slot.PersistentSelf = _depth.Value == 0;
				childrenSection.Slot.ActiveSelf = _depth.Value == 0;
				_setupRoot.ChildAdded += OnChildAdded;
				_setupRoot.ChildRemoved += OnChildRemoved;
				_setupRoot.NameChanged += OnTargetNameChanged;
				_setupRoot.OrderOffsetChanged += OnOrderOffsetChanged;
				_setupRoot.ActiveChanged += OnActiveChanged;
				_setupRoot.PersistentChanged += OnPersistentChanged;
			}
		}
		if (!_childrenValid)
		{
			RebuildChildren();
			_childrenValid = true;
		}
	}

	[SyncMethod(typeof(Delegate), null)]
	private bool IsTargetEmpty()
	{
		return (_rootSlot.Target?.ChildrenCount ?? 0) == 0;
	}

	private void InvalidateChildren()
	{
		if (_childrenValid)
		{
			_childrenValid = false;
			MarkChangeDirty();
		}
	}

	private void RebuildChildren()
	{
		if (_setupRoot == null || _childContainer.Target == null)
		{
			return;
		}
		_expanderIndicator.Target.MarkChangeDirty();
		if (_expander.Target.IsExpanded)
		{
			Dictionary<Slot, SlotInspector> inspectors = Pool.BorrowDictionary<Slot, SlotInspector>();
			List<Slot> toRemove = Pool.BorrowList<Slot>();
			foreach (Slot child in _childContainer.Target.Children)
			{
				SlotInspector inspector = child.GetComponent<SlotInspector>();
				if (inspector._rootSlot.Target == null)
				{
					toRemove.Add(child);
				}
				else
				{
					inspectors.Add(inspector._rootSlot.Target, inspector);
				}
			}
			foreach (Slot item in toRemove)
			{
				item.Destroy();
			}
			foreach (KeyValuePair<Slot, SlotInspector> i in inspectors)
			{
				if (i.Key.Parent != _rootSlot.Target)
				{
					i.Value.Slot.Destroy();
				}
			}
			foreach (Slot child2 in _rootSlot.Target.Children)
			{
				if (!inspectors.ContainsKey(child2))
				{
					_childContainer.Target.AddSlot("SlotInspector").AttachComponent<SlotInspector>().Setup(child2, _selectionReference.Target, _depth.Value + 1);
				}
			}
			Pool.Return(ref inspectors);
			Pool.Return(ref toRemove);
		}
		else
		{
			_childContainer.Target.DestroyChildren();
		}
	}

	private void OnTargetNameChanged(Slot slot)
	{
		MarkChangeDirty();
	}

	private void OnOrderOffsetChanged(Slot slot)
	{
		MarkChangeDirty();
	}

	private void OnActiveChanged(Slot slot)
	{
		MarkChangeDirty();
	}

	private void OnPersistentChanged(Slot slot)
	{
		MarkChangeDirty();
	}

	private void OnChildRemoved(Slot slot, Slot child)
	{
		InvalidateChildren();
	}

	private void OnChildAdded(Slot slot, Slot child)
	{
		InvalidateChildren();
	}

	protected override void OnDispose()
	{
		UnregisterRoot();
		base.OnDispose();
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		_selectionReference = new RelayRef<SyncRef<Slot>>();
		_rootSlot = new SyncRef<Slot>();
		_childContainer = new SyncRef<Slot>();
		_depth = new Sync<int>();
		_expander = new SyncRef<Expander>();
		_expanderIndicator = new SyncRef<TextExpandIndicator>();
		_slotNameText = new SyncRef<Text>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => _selectionReference, 
			4 => _rootSlot, 
			5 => _childContainer, 
			6 => _depth, 
			7 => _expander, 
			8 => _expanderIndicator, 
			9 => _slotNameText, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static SlotInspector __New()
	{
		return new SlotInspector();
	}
}
