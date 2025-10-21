// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.VirtualKeyboard
using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using SkyFrost.Base;

[Category(new string[] { "Userspace/Virtual Keyboard" })]
public class VirtualKeyboard : Component, IItemMetadataSource, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	public readonly Sync<bool> ShiftActive;

	public readonly Sync<bool> HoldShift;

	public readonly FieldDrive<bool> TextPreviewActive;

	public readonly SyncRef<IText> TextPreview;

	private bool _targetIsShown;

	public IText TargetText;

	public override int Version => 1;

	public bool IsShown
	{
		get
		{
			return base.Slot.ActiveSelf;
		}
		set
		{
			_targetIsShown = value;
			RunSynchronously(delegate
			{
				base.Slot.ActiveSelf = _targetIsShown;
				if (!_targetIsShown)
				{
					TargetText = null;
				}
			});
		}
	}

	public string ItemName => base.Slot.Name;

	public IEnumerable<string> ItemTags
	{
		get
		{
			yield return RecordTags.VirtualKeyboard;
		}
	}

	protected override void OnAttach()
	{
		base.Slot.AttachComponent<DestroyBlock>();
		base.Slot.AttachComponent<Grabbable>().Scalable.Value = true;
	}

	protected override void OnDeactivated()
	{
		base.OnDeactivated();
		Userspace.Defocus();
		World w = base.Engine.WorldManager.FocusedWorld;
		w?.RunSynchronously(delegate
		{
			w.LocalUser.ClearFocus();
		});
	}

	protected override void OnCommonUpdate()
	{
		if (ShiftActive.Value && (base.World.IsUserspace() || ShiftActive.WasLastModifiedBy(base.LocalUser)))
		{
			base.InputInterface.SimulatePress(Key.LeftShift, base.World);
		}
		if (TextPreview.Target != null)
		{
			bool active;
			if (TargetText == null || TargetText.IsDestroyed)
			{
				active = false;
				TextPreview.Target.Text = "";
				TextPreview.Target.SelectionStart = -1;
				TextPreview.Target.CaretPosition = -1;
				TextPreview.Target.MaskPattern = null;
				TextPreview.Target.CaretColor = colorX.Clear;
				TextPreview.Target.SelectionColor = colorX.Clear;
			}
			else
			{
				active = true;
				TextPreview.Target.Text = TargetText.Text;
				TextPreview.Target.SelectionStart = TargetText.SelectionStart;
				TextPreview.Target.CaretPosition = TargetText.CaretPosition;
				TextPreview.Target.MaskPattern = TargetText.MaskPattern;
				TextPreview.Target.CaretColor = TargetText.CaretColor.___a;
				TextPreview.Target.SelectionColor = new colorX(0f, 0.5f, 0.2f, 0.5f);
			}
			if (TextPreviewActive.IsLinkValid)
			{
				TextPreviewActive.Target.Value = active;
			}
		}
	}

	public void KeyPressed(VirtualKey key)
	{
		if (!key.IgnoreShift && ShiftActive.Value && !HoldShift.Value)
		{
			base.InputInterface.SimulatePress(Key.LeftShift, base.World);
			ShiftActive.Value = false;
			HoldShift.Value = false;
		}
	}

	public void HidePressed()
	{
		IsShown = false;
	}

	protected override void OnLoading(DataTreeNode node, LoadControl control)
	{
		base.OnLoading(node, control);
		if (control.GetTypeVersion<VirtualKeyboard>() >= 1)
		{
			return;
		}
		control.OnLoaded(this, delegate
		{
			Slot slot = base.Slot.Parent.AddSlot(base.Slot.Name + " - INJECTED ROOT");
			slot.GlobalPosition = base.Slot.GlobalPosition;
			slot.GlobalRotation = base.Slot.GlobalRotation;
			base.Slot.SetParent(slot);
			slot.MoveComponent(this);
			base.Slot.GetComponent<ObjectRoot>()?.Destroy();
			slot.AttachComponent<ObjectRoot>();
			Grabbable component = base.Slot.GetComponent<Grabbable>();
			if (component != null)
			{
				slot.MoveComponent(component);
			}
			Slider component2 = base.Slot.GetComponent<Slider>();
			if (component2 != null)
			{
				slot.MoveComponent(component2);
			}
		});
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		ShiftActive = new Sync<bool>();
		HoldShift = new Sync<bool>();
		TextPreviewActive = new FieldDrive<bool>();
		TextPreview = new SyncRef<IText>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => ShiftActive, 
			4 => HoldShift, 
			5 => TextPreviewActive, 
			6 => TextPreview, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static VirtualKeyboard __New()
	{
		return new VirtualKeyboard();
	}
}
