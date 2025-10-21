// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.UIX.TextField
using System;
using System.Collections.Generic;
using Elements.Core;
using Elements.Data;
using FrooxEngine;
using FrooxEngine.UIX;

[OldTypeName("FrooxEngine.UI.TextField", null)]
[Category(new string[] { "UIX/Interaction" })]
public class TextField : UIComponent, IButtonPressReceiver, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable, IUIGrabbable, IUIGrabReceiver, IUIInteractionTarget
{
	public const float SELECT_ALL_INTERVAL = 0.25f;

	public readonly SyncRef<TextEditor> Editor;

	[OldName("Text")]
	[NonPersistent]
	protected readonly SyncRef<Text> __text;

	[NameOverride("EditingStarted")]
	[NonPersistent]
	protected readonly SyncDelegate<Action<TextEditor>> __editingStarted;

	[NameOverride("EditingChanged")]
	[NonPersistent]
	protected readonly SyncDelegate<Action<TextEditor>> __editingChanged;

	[NameOverride("EditingFinished")]
	[NonPersistent]
	protected readonly SyncDelegate<Action<TextEditor>> __editingFinished;

	private double _lastPress;

	public override int Version => 1;

	public Text Text
	{
		get
		{
			return (Text)Editor.Target.Text.Target;
		}
		set
		{
			Editor.Target.Text.Target = value;
		}
	}

	public string TargetString
	{
		get
		{
			return Text.Content.Value;
		}
		set
		{
			Text.Content.Value = value;
		}
	}

	public IField<string> TargetStringField => Text.Content;

	public int InteractionTargetPriority => 10;

	public string InteractionName => "Type";

	public InteractionCursor GetCursor(InteractionLaser laser)
	{
		return laser.CursorFactory?.Text() ?? default(InteractionCursor);
	}

	protected override void OnAttach()
	{
		Editor.Target = base.Slot.AttachComponent<TextEditor>();
	}

	void IButtonPressReceiver.Pressed(IButton button, ButtonEventData eventData)
	{
		if (base.Time.WorldTime - _lastPress <= 0.25)
		{
			Editor.Target.SelectAll();
		}
		_lastPress = base.Time.WorldTime;
		if (!Editor.Target.IsEditing)
		{
			Editor.Target.Focus();
		}
		Editor.Target.EnsureKeyboard();
	}

	public void Pressing(IButton button, ButtonEventData eventData)
	{
	}

	public void Released(IButton button, ButtonEventData eventData)
	{
	}

	public IGrabbable TryGrab(Component grabber, Canvas.InteractionData eventData, in float3 point)
	{
		if (TargetString != null)
		{
			return ValueProxy<string>.Construct(base.World, TargetString);
		}
		return null;
	}

	public bool TryReceive(IEnumerable<IGrabbable> items, Component grabber, Canvas.InteractionData eventData, in float3 point)
	{
		foreach (IGrabbable item in items)
		{
			ValueProxy<string> stringProxy = item.Slot.GetComponentInChildren<ValueProxy<string>>();
			if (stringProxy != null)
			{
				Text.Content.Value = stringProxy.Value;
				Editor.Target.ForceEditingChangedEvent();
				return true;
			}
		}
		foreach (IGrabbable item2 in items)
		{
			IValueSource valueSource = item2.Slot.GetComponentInChildren<IValueSource>();
			if (valueSource != null)
			{
				Text.Content.Value = valueSource.BoxedValue?.ToString();
				Editor.Target.ForceEditingChangedEvent();
				return true;
			}
		}
		return false;
	}

	protected override void OnLoading(DataTreeNode node, LoadControl control)
	{
		if (control.GetTypeVersion(typeof(TextField)) == 0 && Editor.Target == null && __text.Target != null)
		{
			control.OnLoaded(this, delegate
			{
				Editor.Target = base.Slot.AttachComponent<TextEditor>();
				Editor.Target.Text.Target = __text.Target;
				Editor.Target.EditingStarted.Target = __editingStarted.Target;
				Editor.Target.EditingChanged.Target = __editingChanged.Target;
				Editor.Target.EditingFinished.Target = __editingFinished.Target;
			});
		}
	}

	IGrabbable IUIGrabbable.TryGrab(Component grabber, Canvas.InteractionData eventData, in float3 globalPoint)
	{
		return TryGrab(grabber, eventData, in globalPoint);
	}

	bool IUIGrabReceiver.TryReceive(IEnumerable<IGrabbable> items, Component grabber, Canvas.InteractionData eventData, in float3 globalPoint)
	{
		return TryReceive(items, grabber, eventData, in globalPoint);
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		Editor = new SyncRef<TextEditor>();
		__text = new SyncRef<Text>();
		__text.MarkNonPersistent();
		__editingStarted = new SyncDelegate<Action<TextEditor>>();
		__editingStarted.MarkNonPersistent();
		__editingChanged = new SyncDelegate<Action<TextEditor>>();
		__editingChanged.MarkNonPersistent();
		__editingFinished = new SyncDelegate<Action<TextEditor>>();
		__editingFinished.MarkNonPersistent();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => Editor, 
			4 => __text, 
			5 => __editingStarted, 
			6 => __editingChanged, 
			7 => __editingFinished, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static TextField __New()
	{
		return new TextField();
	}
}
