// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.UIX.Button
using System;
using System.Threading.Tasks;
using Elements.Core;
using Elements.Data;
using FrooxEngine;
using FrooxEngine.UIX;

[Category(new string[] { "UIX/Interaction" })]
[OldTypeName("FrooxEngine.UI.Button", null)]
public class Button : InteractionElement, IButton, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	public readonly Sync<VibratePreset> HoverVibrate;

	public readonly Sync<VibratePreset> PressVibrate;

	public readonly Sync<bool> ClearFocusOnPress;

	public readonly Sync<bool> PassThroughHorizontalMovement;

	public readonly Sync<bool> PassThroughVerticalMovement;

	public readonly Sync<bool> RequireLockInToPress;

	public readonly Sync<bool> RequireInitialPress;

	public readonly Sync<float2> PressPoint;

	public readonly Sync<bool> SendSlotEvents;

	[OldName(new string[] { "ButtonPressed", "GeneralButtonPressed" })]
	public readonly SyncDelegate<ButtonEventHandler> Pressed;

	public readonly SyncDelegate<ButtonEventHandler> Pressing;

	[OldName("GeneralButtonReleased")]
	public readonly SyncDelegate<ButtonEventHandler> Released;

	[OldName("ButtonEnter")]
	public readonly SyncDelegate<ButtonEventHandler> HoverEnter;

	public readonly SyncDelegate<ButtonEventHandler> HoverStay;

	[OldName("ButtonLeave")]
	public readonly SyncDelegate<ButtonEventHandler> HoverLeave;

	public override bool RequireLockIn => RequireLockInToPress;

	public override bool TouchEnterLock => RequireInitialPress.Value;

	SyncDelegate<ButtonEventHandler> IButton.Pressed => Pressed;

	SyncDelegate<ButtonEventHandler> IButton.Pressing => Pressing;

	SyncDelegate<ButtonEventHandler> IButton.Released => Released;

	public Text Label => base.Slot.GetComponentInChildren<Text>();

	public string LabelText
	{
		get
		{
			return Label?.Content.Value;
		}
		set
		{
			if (Label != null)
			{
				Label.Content.Value = value;
			}
		}
	}

	public IField<string> LabelTextField => Label?.Content;

	public Image Icon
	{
		get
		{
			foreach (Slot child in base.Slot.Children)
			{
				Image image = child.GetComponent<Image>();
				if (image != null)
				{
					return image;
				}
			}
			return null;
		}
	}

	protected override bool PassOnHorizontalMovement => PassThroughHorizontalMovement.Value;

	protected override bool PassOnVerticalMovement => PassThroughVerticalMovement.Value;

	public event ButtonEventHandler LocalPressed;

	public event ButtonEventHandler LocalPressing;

	public event ButtonEventHandler LocalReleased;

	public event ButtonEventHandler LocalHoverEnter;

	public event ButtonEventHandler LocalHoverStay;

	public event ButtonEventHandler LocalHoverLeave;

	public float3 GetWorldSpacePressPoint()
	{
		if (!IsPressed)
		{
			throw new Exception("Button isn't being pressed");
		}
		return base.Slot.GetComponentInParents<Canvas>().Slot.LocalPointToGlobal((float3)PressPoint.Value);
	}

	public void SimulatePress(float duration, ButtonEventData eventData)
	{
		StartTask(async delegate
		{
			if (!IsPressed.Value)
			{
				IsPressed.Value = true;
				RunPressed(eventData);
				await Task.Delay(TimeSpan.FromSeconds(duration));
				IsPressed.Value = false;
				RunReleased(eventData);
			}
		});
	}

	protected override void OnHoverBegin(Canvas.InteractionData eventData)
	{
		eventData.source.Slot.TryVibrate(HoverVibrate);
		RunHoverEnter(ConstructEventData(eventData));
	}

	protected override void OnHoverStay(Canvas.InteractionData eventData)
	{
		RunHoverStay(ConstructEventData(eventData));
	}

	protected override void OnHoverEnd(Canvas.InteractionData eventData)
	{
		eventData.source.Slot.TryVibrate(HoverVibrate);
		RunHoverLeave(ConstructEventData(eventData));
	}

	protected override void OnPressBegin(Canvas.InteractionData eventData)
	{
		PressPoint.Value = eventData.position;
		eventData.source.Slot.TryVibrate(PressVibrate);
		RunPressed(ConstructEventData(eventData));
	}

	protected override void OnPressStay(Canvas.InteractionData eventData)
	{
		RunPressing(ConstructEventData(eventData));
	}

	protected override void OnPressEnd(Canvas.InteractionData eventData)
	{
		RunReleased(ConstructEventData(eventData));
	}

	protected override bool ProcessInteractionEvent(Canvas.InteractionData eventData)
	{
		return true;
	}

	private ButtonEventData ConstructEventData(Canvas.InteractionData eventData)
	{
		return new ButtonEventData(eventData.source, base.RectTransform.Canvas.Slot.LocalPointToGlobal((float3)eventData.position), eventData.position, base.CurrentGlobalRect.GetNormalizedPoint(eventData.position));
	}

	private void RunPressed(ButtonEventData eventData)
	{
		if ((bool)ClearFocusOnPress)
		{
			base.LocalUser.ClearFocus();
		}
		Pressed.Target?.Invoke(this, eventData);
		this.LocalPressed?.Invoke(this, eventData);
		if (SendSlotEvents.Value)
		{
			base.Slot.ForeachComponent(delegate(IButtonPressReceiver r)
			{
				r.Pressed(this, eventData);
			}, cacheItems: true, exludeDisabled: true);
		}
	}

	private void RunPressing(ButtonEventData eventData)
	{
		Pressing.Target?.Invoke(this, eventData);
		this.LocalPressing?.Invoke(this, eventData);
		if (SendSlotEvents.Value)
		{
			base.Slot.ForeachComponent(delegate(IButtonPressReceiver r)
			{
				r.Pressing(this, eventData);
			}, cacheItems: true, exludeDisabled: true);
		}
	}

	private void RunReleased(ButtonEventData eventData)
	{
		Released.Target?.Invoke(this, eventData);
		this.LocalReleased?.Invoke(this, eventData);
		if (SendSlotEvents.Value)
		{
			base.Slot.ForeachComponent(delegate(IButtonPressReceiver r)
			{
				r.Released(this, eventData);
			}, cacheItems: true, exludeDisabled: true);
		}
	}

	private void RunHoverEnter(ButtonEventData eventData)
	{
		HoverEnter.Target?.Invoke(this, eventData);
		this.LocalHoverEnter?.Invoke(this, eventData);
		if (SendSlotEvents.Value)
		{
			base.Slot.ForeachComponent(delegate(IButtonHoverReceiver r)
			{
				r.HoverEnter(this, eventData);
			}, cacheItems: true, exludeDisabled: true);
		}
	}

	private void RunHoverStay(ButtonEventData eventData)
	{
		HoverStay.Target?.Invoke(this, eventData);
		this.LocalHoverStay?.Invoke(this, eventData);
		if (SendSlotEvents.Value)
		{
			base.Slot.ForeachComponent(delegate(IButtonHoverReceiver r)
			{
				r.HoverStay(this, eventData);
			}, cacheItems: true, exludeDisabled: true);
		}
	}

	private void RunHoverLeave(ButtonEventData eventData)
	{
		HoverLeave.Target?.Invoke(this, eventData);
		this.LocalHoverLeave?.Invoke(this, eventData);
		if (SendSlotEvents.Value)
		{
			base.Slot.ForeachComponent(delegate(IButtonHoverReceiver r)
			{
				r.HoverLeave(this, eventData);
			}, cacheItems: true, exludeDisabled: true);
		}
	}

	protected override void OnAwake()
	{
		base.OnAwake();
		SendSlotEvents.Value = true;
		HoverVibrate.Value = VibratePreset.Short;
		PressVibrate.Value = VibratePreset.Medium;
		ClearFocusOnPress.Value = true;
		PassThroughHorizontalMovement.Value = true;
		PassThroughVerticalMovement.Value = true;
		RequireInitialPress.Value = true;
	}

	protected override void OnAttach()
	{
		base.OnAttach();
		Image image = base.Slot.GetComponent<Image>();
		if (image != null)
		{
			SetupBackgroundColor(image.Tint);
		}
	}

	public void SetupBackgroundColor(Sync<colorX> tint)
	{
		ColorDriver colorDriver = ColorDrivers.Add();
		colorDriver.ColorDrive.Target = tint;
		colorDriver.SetColors((colorX)tint);
	}

	protected override void OnDispose()
	{
		this.LocalPressed = null;
		this.LocalPressing = null;
		this.LocalReleased = null;
		this.LocalHoverEnter = null;
		this.LocalHoverStay = null;
		this.LocalHoverLeave = null;
		base.OnDispose();
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		HoverVibrate = new Sync<VibratePreset>();
		PressVibrate = new Sync<VibratePreset>();
		ClearFocusOnPress = new Sync<bool>();
		PassThroughHorizontalMovement = new Sync<bool>();
		PassThroughVerticalMovement = new Sync<bool>();
		RequireLockInToPress = new Sync<bool>();
		RequireInitialPress = new Sync<bool>();
		PressPoint = new Sync<float2>();
		SendSlotEvents = new Sync<bool>();
		Pressed = new SyncDelegate<ButtonEventHandler>();
		Pressing = new SyncDelegate<ButtonEventHandler>();
		Released = new SyncDelegate<ButtonEventHandler>();
		HoverEnter = new SyncDelegate<ButtonEventHandler>();
		HoverStay = new SyncDelegate<ButtonEventHandler>();
		HoverLeave = new SyncDelegate<ButtonEventHandler>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => BaseColor, 
			4 => ColorDrivers, 
			5 => __legacy_NormalColor, 
			6 => __legacy_HighlightColor, 
			7 => __legacy_PressColor, 
			8 => __legacy_DisabledColor, 
			9 => __legacy_TintColorMode, 
			10 => __legacy_ColorDrive, 
			11 => IsPressed, 
			12 => IsHovering, 
			13 => HoverVibrate, 
			14 => PressVibrate, 
			15 => ClearFocusOnPress, 
			16 => PassThroughHorizontalMovement, 
			17 => PassThroughVerticalMovement, 
			18 => RequireLockInToPress, 
			19 => RequireInitialPress, 
			20 => PressPoint, 
			21 => SendSlotEvents, 
			22 => Pressed, 
			23 => Pressing, 
			24 => Released, 
			25 => HoverEnter, 
			26 => HoverStay, 
			27 => HoverLeave, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static Button __New()
	{
		return new Button();
	}
}
