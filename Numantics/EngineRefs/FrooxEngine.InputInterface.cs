// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.InputInterface
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Input;
using Renderite.Shared;

public class InputInterface
{
	private class UpdateBucket
	{
		public readonly List<IInputDriver> inputDrivers;

		public readonly int order;

		public UpdateBucket(int order)
		{
			this.order = order;
			inputDrivers = new List<IInputDriver>();
		}
	}

	public delegate void KeyboardHandler(string text, KeyboardType keyboardType = KeyboardType.Default, bool autocorrection = true, bool multiline = false, bool secure = false, string textPlaceholder = "", int characterLimit = 0);

	public const float DEFAULT_USER_HEIGHT = 1.75f;

	public const float EYE_HEAD_OFFSET = 0.125f;

	private bool _vrHotswitchingSettingLoaded;

	private bool _vrActive;

	private bool _nextVRactive;

	private bool _supressHotswitching;

	private bool _keyboardBindingRegistered;

	private float _lastDeltaTime;

	private bool _seatedMode;

	private int _supressSeatedMode;

	private float3 _actualSeatedModeOffset;

	public readonly List<string> ExtraDeviceInfos = new List<string>();

	private InputState _stateToProcess;

	private FrooxEngine.ITrackedDevice[] bodyNodes;

	private VR_Manager _vrManager;

	private GamepadManager _gamepadManager;

	private MultiTouchManager _multiTouchManager;

	private DisplayManager _displayManager;

	private IStandardController leftController;

	private IStandardController rightController;

	private List<IInputDriver> inputDrivers = new List<IInputDriver>();

	private Dictionary<Key, World> _virtualPresses = new Dictionary<Key, World>();

	private string _virtualTypeDelta;

	private List<UpdateBucket> inputDriverUpdateBuckets = new List<UpdateBucket>();

	private List<IInputDevice> inputDevices = new List<IInputDevice>();

	private List<HapticPoint> hapticPoints = new List<HapticPoint>();

	private List<IBindingGenerator> bindingGenerators = new List<IBindingGenerator>();

	public readonly ActivityHeuristicTracker ActivityHeuristic;

	private bool _platformDashboardOpened;

	private bool _userPresentInHeadset;

	private bool _userPresentInHeadsetDetected;

	private List<FrooxEngine.Pointer> _pointers = new List<FrooxEngine.Pointer>();

	private List<Display> _displays = new List<Display>();

	private RepeatDigital[] keys;

	private World[] keyVirtualState;

	public float KeyRepeatWait = 0.25f;

	public float KeyRepeatInterval = 1f / 30f;

	private List<Action> _onInputStateProcessed = new List<Action>();

	private bool _hapticPointsChanged;

	private object _lastKeyboardRequestee;

	private HashSet<ControllerProperty> _globallyBlockedProperties = new HashSet<ControllerProperty>();

	private Dictionary<Type, IList> _globalBlockers = new Dictionary<Type, IList>();

	public Engine Engine { get; private set; }

	public bool IsWindowFocused { get; private set; }

	public bool IsFullscreen { get; private set; }

	public bool VR_HotswitchingEnabled { get; private set; }

	public bool VR_Active
	{
		get
		{
			return _vrActive;
		}
		set
		{
			_nextVRactive = value;
			_supressHotswitching = true;
		}
	}

	public bool ScreenActive => !VR_Active;

	public float UserHeight { get; private set; } = 1.75f;

	public float3 GlobalTrackingOffset { get; private set; }

	public float3 CustomTrackingOffset { get; set; }

	public float3 SeatedModeOffset { get; private set; }

	public bool SeatedMode
	{
		get
		{
			return _seatedMode;
		}
		set
		{
			if (_seatedMode != value)
			{
				if (value)
				{
					float height = GetBodyNode(BodyNode.Head).Position.y + 0.125f;
					float delta = UserHeight - height;
					SeatedModeOffset = new float3(0f, delta);
				}
				else
				{
					SeatedModeOffset = float3.Zero;
				}
				_seatedMode = value;
			}
		}
	}

	public VirtualController LeftVirtualController { get; private set; }

	public VirtualController RightVirtualController { get; private set; }

	public int InputDeviceCount => inputDevices.Count;

	public int HapticPointCount => hapticPoints.Count;

	public Chirality PrimaryHand { get; private set; }

	public DateTime LastActivity => ActivityHeuristic.LastActivity;

	public float LastActiveSecondsAgo => ActivityHeuristic.LastActiveSecondsAgo;

	public bool AppDashOpened { get; internal set; }

	public bool AppFacetsOpened { get; internal set; }

	public bool PlatformDashboardOpened => _platformDashboardOpened;

	public bool IsUserPresentInHeadset
	{
		get
		{
			if (_userPresentInHeadsetDetected)
			{
				return _userPresentInHeadset;
			}
			return true;
		}
	}

	public bool IsUserPresentInVR
	{
		get
		{
			if (IsUserPresentInHeadset)
			{
				return VR_Active;
			}
			return false;
		}
	}

	public Mouse Mouse { get; private set; }

	public bool IsCursorLocked { get; private set; }

	public int2? CursorLockPosition { get; private set; }

	public int PointerCount => _pointers.Count;

	public IEnumerable<FrooxEngine.Pointer> Pointers => _pointers;

	public int2 PrimaryResolution => PrimaryDisplay?.Resolution ?? WindowResolution;

	public int2 WindowResolution { get; private set; }

	public float WindowAspectRatio => (float)WindowResolution.x / (float)WindowResolution.y;

	public Display PrimaryDisplay => _displays.FirstOrDefault((Display d) => d.IsPrimary);

	public int DisplayCount => _displays.Count;

	public HeadOutputDevice HeadOutputDevice { get; private set; }

	public bool ControllerVibrationEnabled { get; private set; }

	public bool HapticsEnabled { get; private set; }

	public string TypeDelta { get; private set; }

	public bool IsClipboardSupported => Clipboard != null;

	public bool IsInputInjectionSupported => InputInjector != null;

	public bool IsTouchKeyboardSupported => SystemKeyboard != null;

	public IClipboardInterface? Clipboard { get; private set; }

	public ISystemInputInjector InputInjector { get; private set; }

	public ISystemKeyboard SystemKeyboard { get; private set; }

	public bool IsKeyboardActive { get; private set; }

	public event Action<bool> VRActiveChanged;

	public event Action BeforeInputsUpdate;

	public event Action AfterInputsUpdate;

	public event Action<int2> WindowResolutionChanged;

	private event Action<IInputDevice> _inputDeviceAdded;

	public event Action<IInputDevice> InputDeviceAdded
	{
		add
		{
			foreach (IInputDevice device in inputDevices)
			{
				try
				{
					value(device);
				}
				catch (Exception ex)
				{
					UniLog.Error("Exception when calling InputDeviceAdded event:\n" + ex);
				}
			}
			_inputDeviceAdded += value;
		}
		remove
		{
			_inputDeviceAdded -= value;
		}
	}

	public event FilesDropHandler FilesDropped;

	public event OffsetChange GlobalTrackingOffsetChanged;

	public event Action<FrooxEngine.ITrackedDevice> BodyNodeChanged;

	public event Action<IBindingGenerator> BindingGeneratorAdded;

	public event Action HapticPointsChanged;

	public event Action<IStandardController> ControllerRegistered;

	public event Action KeyboardActivated;

	public event Action KeyboardDeactivated;

	public event Action<string> OnTypeAppend;

	public event Action<Key> OnSimulatedPress;

	public void SupressMaintainHeightInNextUpdate()
	{
		_supressSeatedMode = 5;
	}

	internal void ScheduleInputStateProcessing(InputState state)
	{
		if (_stateToProcess != null)
		{
			throw new InvalidOperationException("Input state is already scheduled to be processed");
		}
		_stateToProcess = state;
	}

	internal OutputState CollectOutputState()
	{
		OutputState state = new OutputState();
		state.lockCursor = IsCursorLocked;
		int2? cursorLockPosition = CursorLockPosition;
		state.lockCursorPosition = (cursorLockPosition.HasValue ? new RenderVector2i?(cursorLockPosition.GetValueOrDefault()) : ((RenderVector2i?)null));
		state.keyboardInputActive = IsKeyboardActive;
		_vrManager?.CollectOutputState(state);
		return state;
	}

	public void UpdateUserPresent(bool isPresent)
	{
		_userPresentInHeadset = isPresent;
		_userPresentInHeadsetDetected |= isPresent;
	}

	public void ResetUserPresent()
	{
		if (_userPresentInHeadsetDetected)
		{
			_userPresentInHeadsetDetected = false;
			UniLog.Warning("Resetting user present in headset detected status - detected multiple trigger presses, the presence sensor is likely broken or not reporting correctly.");
		}
	}

	public void UpdatePlatformDashboard(bool opened)
	{
		_platformDashboardOpened = opened;
	}

	public void RegisterPointer(FrooxEngine.Pointer pointer)
	{
		if (!_pointers.AddUnique(pointer))
		{
			throw new Exception("Pointer already registered: " + pointer);
		}
	}

	public void UnregisterPointer(FrooxEngine.Pointer pointer)
	{
		if (!_pointers.Remove(pointer))
		{
			throw new Exception("Pointer wasn't registered: " + pointer);
		}
	}

	public FrooxEngine.Pointer GetPointer(int index)
	{
		return _pointers[index];
	}

	public FrooxEngine.Pointer TryGetPointerById(int id)
	{
		return _pointers.FirstOrDefault((FrooxEngine.Pointer p) => p.Id == id);
	}

	public void RegisterDisplay(Display display)
	{
		if (_displays.Contains(display))
		{
			throw new Exception("This display is already registered");
		}
		_displays.Add(display);
		RegisterInputDevice(display, $"Display {display.DisplayIndex}");
	}

	private void ProcessInputState(InputState state, float deltaTime)
	{
		if (state.mouse != null)
		{
			if (Mouse == null)
			{
				Mouse = new Mouse();
				RegisterInputDevice(Mouse, "Mouse");
				InitDeviceFields(Mouse);
			}
			Mouse.Update(state.mouse, deltaTime);
		}
		if (state.keyboard != null)
		{
			UpdateKeyboardState(state.keyboard, deltaTime);
		}
		if (state.window != null)
		{
			UpdateWindowState(state.window);
		}
		_vrManager.Update(state.vr, deltaTime);
		_gamepadManager.UpdateGamepads(state.gamepads, deltaTime);
		_multiTouchManager.UpdateTouches(state.touches, deltaTime);
		_displayManager.Update(state.displays, deltaTime);
	}

	private void UpdateWindowState(WindowState state)
	{
		RenderVector2i resolution = state.windowResolution;
		IsWindowFocused = state.isWindowFocused;
		IsFullscreen = state.isFullscreen;
		if (WindowResolution != (int2)resolution)
		{
			WindowResolution = resolution;
			try
			{
				this.WindowResolutionChanged?.Invoke(resolution);
			}
			catch (Exception ex)
			{
				UniLog.Error("Exception running WindowResolutionChanged event:\n" + ex);
			}
		}
		if (state.dragAndDropEvent != null)
		{
			try
			{
				List<string> paths = state.dragAndDropEvent.paths;
				if (Engine.Platform == Platform.Linux)
				{
					for (int i = 0; i < paths.Count; i++)
					{
						string path = paths[i];
						if (path.StartsWith("Z:"))
						{
							path = path.Substring("Z:".Length);
							path = path.Replace("\\", "/");
							paths[i] = path;
						}
					}
				}
				this.FilesDropped?.Invoke(paths, (int2)state.dragAndDropEvent.dropPoint);
			}
			catch (Exception value)
			{
				UniLog.Error($"Exception when handling FilesDropped event:\n{value}");
			}
		}
		if (state.resolutionSettingsApplied)
		{
			Engine.RenderSystem.ResolutionSettingsConsumed();
		}
	}

	private void UpdateKeyboardState(KeyboardState keyboardState, float deltaTime)
	{
		if (!_keyboardBindingRegistered)
		{
			_keyboardBindingRegistered = true;
			RegisterBindingGenerator(new KeyboardAndMouseBindingGenerator());
		}
		for (int i = 0; i < keys.Length; i++)
		{
			Key key = (Key)i;
			bool state;
			World virtualPress;
			switch (key)
			{
			case Key.Shift:
				state = keys[304].Held || keys[303].Held;
				virtualPress = keyVirtualState[304] ?? keyVirtualState[303];
				break;
			case Key.Control:
				state = keys[306].Held || keys[305].Held;
				virtualPress = keyVirtualState[306] ?? keyVirtualState[305];
				break;
			case Key.Windows:
				state = keys[311].Held || keys[312].Held;
				virtualPress = keyVirtualState[311] ?? keyVirtualState[312];
				break;
			case Key.Alt:
				state = keys[308].Held || keys[307].Held;
				virtualPress = keyVirtualState[308] ?? keyVirtualState[307];
				break;
			default:
				state = _virtualPresses.TryGetValue(key, out virtualPress) || (keyboardState.heldKeys?.Contains(key) ?? false);
				break;
			}
			keys[i].UpdateState(state, deltaTime);
			keyVirtualState[i] = virtualPress;
		}
		TypeDelta = keyboardState.typeDelta ?? "";
	}

	public Display GetDisplay(int index)
	{
		return _displays[index];
	}

	public Display TryGetDisplay(int index)
	{
		if (index < 0)
		{
			return null;
		}
		if (index >= _displays.Count)
		{
			return null;
		}
		return _displays[index];
	}

	public InputInterface(Engine engine, HeadOutputDevice outputDevice)
	{
		Engine = engine;
		HeadOutputDevice = outputDevice;
		_nextVRactive = HeadOutputDevice.IsVRViewSupported();
		UniLog.Log($"HeadOutputDevice: {HeadOutputDevice}");
		_vrActive = false;
		_vrManager = new VR_Manager(this);
		_gamepadManager = new GamepadManager(this);
		_multiTouchManager = new MultiTouchManager(this);
		_displayManager = new DisplayManager(this);
		Settings.RegisterValueChanges(delegate(GeneralControlsSettings s)
		{
			PrimaryHand = s.PrimaryHand.Value;
		});
		Settings.RegisterValueChanges(delegate(GeneralHapticsSettings s)
		{
			ControllerVibrationEnabled = s.EnableControllerVibration;
			HapticsEnabled = s.EnableHaptics;
		});
		Settings.RegisterValueChanges(delegate(GeneralVRSettings s)
		{
			VR_HotswitchingEnabled = s.UseVRHotswitching;
			if (!VR_HotswitchingEnabled && !_vrHotswitchingSettingLoaded)
			{
				_vrHotswitchingSettingLoaded = true;
				_nextVRactive = true;
			}
		});
		Settings.RegisterValueChanges(delegate(UserMetricsSettings s)
		{
			UserHeight = s.UserHeight;
		});
		FrooxEngine.ITrackedDevice[] array = new TrackedObject[Enum.GetValues(typeof(BodyNode)).Length];
		bodyNodes = array;
		for (int i = 0; i < bodyNodes.Length; i++)
		{
			BodyNode bodyNode = (BodyNode)i;
			TrackedObject trackedObject = new TrackedObject();
			InitDeviceFields(trackedObject);
			trackedObject.Initialize(this, i, bodyNode.ToString());
			trackedObject.CorrespondingBodyNode = bodyNode;
			trackedObject.IsDeviceActive = false;
			bodyNodes[i] = trackedObject;
		}
		int maxValue = 0;
		foreach (object key in Enum.GetValues(typeof(Key)))
		{
			maxValue = MathX.Max(maxValue, (int)(Key)key);
		}
		keys = new RepeatDigital[maxValue + 1];
		keyVirtualState = new World[keys.Length];
		for (int i2 = 0; i2 < keys.Length; i2++)
		{
			keys[i2] = new RepeatDigital(this);
		}
		if (Environment.GetCommandLineArgs().Any((string a) => a.ToLower().EndsWith("useappcamera")))
		{
			UniLog.Log("Using App Camera");
			ExtraDeviceInfos.Add(Engine.Cloud.Platform.Name + " Camera");
		}
		ActivityHeuristic = new ActivityHeuristicTracker(this);
		RegisterBindingGenerator(new DualControllerBindingGenerator(this));
		LeftVirtualController = CreateDevice<VirtualController>("LeftVirtualController");
		RightVirtualController = CreateDevice<VirtualController>("RightVirtualController");
	}

	public void RegisterPostInputStateTask(Action task)
	{
		lock (_onInputStateProcessed)
		{
			_onInputStateProcessed.Add(task);
		}
	}

	public void Update(float deltaTime)
	{
		_lastDeltaTime = deltaTime;
		CursorLock _lock = null;
		foreach (World w in Engine.WorldManager.Worlds)
		{
			if (w.State == World.WorldState.Running && w.Focus != World.WorldFocus.Background)
			{
				CursorLock l = w.Input.LockCursor;
				if (l != null && (_lock == null || _lock.priority < l.priority))
				{
					_lock = l;
				}
			}
		}
		bool unlockCursor = false;
		if (_lock == null)
		{
			unlockCursor = Engine.WorldManager.GetWorld((World world) => world.State == World.WorldState.Running && world.Focus != World.WorldFocus.Background && world.Input.UnlockCursor) != null;
		}
		IsCursorLocked = !unlockCursor && IsWindowFocused;
		CursorLockPosition = _lock?.position;
		ClearBlockers();
		this.BeforeInputsUpdate?.Invoke();
		if (!HeadOutputDevice.IsVRViewSupported())
		{
			_nextVRactive = false;
		}
		else if (!HeadOutputDevice.IsScreenViewSupported())
		{
			_nextVRactive = true;
		}
		else if (VR_HotswitchingEnabled)
		{
			if (_supressHotswitching)
			{
				if (_nextVRactive == _vrActive && _vrActive == _userPresentInHeadset)
				{
					_supressHotswitching = false;
				}
			}
			else
			{
				_nextVRactive = _userPresentInHeadset;
			}
		}
		if (_nextVRactive != _vrActive)
		{
			_vrActive = _nextVRactive;
			try
			{
				this.VRActiveChanged?.Invoke(_vrActive);
			}
			catch (Exception ex)
			{
				UniLog.Error("Exception running VRActiveChanged events:\n" + ex);
			}
		}
		TypeDelta = "";
		if (_stateToProcess != null)
		{
			ProcessInputState(_stateToProcess, deltaTime);
			_stateToProcess = null;
			if (_onInputStateProcessed.Count > 0)
			{
				lock (_onInputStateProcessed)
				{
					foreach (Action item in _onInputStateProcessed)
					{
						item();
					}
				}
				_onInputStateProcessed.Clear();
			}
		}
		TypeDelta = _virtualTypeDelta + TypeDelta;
		_virtualTypeDelta = null;
		_virtualPresses.Clear();
		foreach (UpdateBucket bucket in inputDriverUpdateBuckets)
		{
			List<IInputDriver> toRemove = null;
			foreach (IInputDriver driver in bucket.inputDrivers)
			{
				try
				{
					driver.UpdateInputs(deltaTime);
				}
				catch (Exception value)
				{
					UniLog.Error($"Exception when updating driver: {driver}:\n{value}");
					if (toRemove == null)
					{
						toRemove = new List<IInputDriver>();
					}
					toRemove.Add(driver);
				}
			}
			if (toRemove == null)
			{
				continue;
			}
			foreach (IInputDriver remove in toRemove)
			{
				bucket.inputDrivers.Remove(remove);
			}
		}
		float3 _seatedModeOffset = SeatedModeOffset;
		if (_supressSeatedMode > 0)
		{
			_seatedModeOffset = float3.Zero;
			_supressSeatedMode--;
		}
		_actualSeatedModeOffset = MathX.ConstantLerp(in _actualSeatedModeOffset, in _seatedModeOffset, deltaTime * 2f);
		float3 prevGlobalOffset = GlobalTrackingOffset;
		GlobalTrackingOffset = CustomTrackingOffset + _actualSeatedModeOffset;
		CustomTrackingOffset = float3.Zero;
		if (GlobalTrackingOffset != prevGlobalOffset)
		{
			this.GlobalTrackingOffsetChanged?.Invoke(prevGlobalOffset, GlobalTrackingOffset);
		}
		for (int i = 0; i < inputDevices.Count; i++)
		{
			FrooxEngine.ITrackedDevice trackedDevice = inputDevices[i] as FrooxEngine.ITrackedDevice;
			if (trackedDevice != null && trackedDevice.CorrespondingBodyNode.GetRelativeNode() == BodyNode.Root)
			{
				RefVal<float3> positionRef = trackedDevice.PositionRef;
				positionRef.Value += GlobalTrackingOffset;
			}
			if (trackedDevice != null && trackedDevice.CorrespondingBodyNode != BodyNode.NONE && trackedDevice.IsDeviceActive && trackedDevice.IsTracking)
			{
				int index = (int)trackedDevice.CorrespondingBodyNode;
				FrooxEngine.ITrackedDevice existing = bodyNodes[index];
				if (existing == null || !existing.IsDeviceActive || !existing.IsTracking || existing.Priority < trackedDevice.Priority)
				{
					bodyNodes[index] = trackedDevice;
				}
			}
		}
		LeftVirtualController.IsDeviceActive = LeftVirtualController == GetControllerNode(Chirality.Left);
		RightVirtualController.IsDeviceActive = RightVirtualController == GetControllerNode(Chirality.Right);
		if (_hapticPointsChanged)
		{
			_hapticPointsChanged = false;
			this.HapticPointsChanged?.Invoke();
		}
		this.AfterInputsUpdate?.Invoke();
		ActivityHeuristic.Update();
	}

	public void TriggerBodyNodeChanged(FrooxEngine.ITrackedDevice device)
	{
		this.BodyNodeChanged?.Invoke(device);
	}

	public void RegisterClipboardInterface(IClipboardInterface clipboardInterface)
	{
		if (clipboardInterface == null)
		{
			throw new ArgumentNullException("clipboardInterface");
		}
		if (Clipboard != null)
		{
			throw new InvalidOperationException("Clipboard interface already registered. Existing: " + Clipboard);
		}
		Clipboard = clipboardInterface;
	}

	public void RegisterSystemInputInjector(ISystemInputInjector inputInjector)
	{
		if (inputInjector == null)
		{
			throw new ArgumentNullException("inputInjector");
		}
		if (InputInjector != null)
		{
			throw new InvalidOperationException("Input injection already registered. Existing: " + InputInjector);
		}
		InputInjector = inputInjector;
		RegisterInputDriver(inputInjector);
	}

	public void RegisterTouchKeyboard(ISystemKeyboard systemKeyboard)
	{
		if (systemKeyboard == null)
		{
			throw new ArgumentNullException("systemKeyboard");
		}
		if (SystemKeyboard != null)
		{
			throw new InvalidOperationException("System keyboard already registered. Existing: " + SystemKeyboard);
		}
		SystemKeyboard = systemKeyboard;
	}

	public void SetMousePosition(in int2 point)
	{
		if (Mouse != null)
		{
			Mouse.DesktopPosition.UpdateValue(point, _lastDeltaTime);
		}
		InputInjector?.SetMousePosition(point);
	}

	public FrooxEngine.Pointer InjectTouch()
	{
		return InputInjector?.InjectTouch();
	}

	public void RemoveInjectedTouch(FrooxEngine.Pointer touch)
	{
		InputInjector?.RemoveInjectedTouch(touch);
	}

	public void InjectWrite(string str)
	{
		InputInjector?.InjectString(str);
	}

	public void InjectKeyPress(List<Key> keys)
	{
		InputInjector?.InjectKeyPress(keys);
	}

	public void InjectKeyPress(Key key)
	{
		List<Key> list = Pool.BorrowList<Key>();
		list.Add(key);
		InjectKeyPress(list);
		Pool.Return(ref list);
	}

	public void InjectRightClick(float2 point)
	{
		InputInjector?.SendRightClick((int2)point);
	}

	public void ShowKeyboard(IText targetText, string currentText, KeyboardType keyboardType = KeyboardType.Default, bool autocorrection = true, bool multiline = false, bool secure = false, string textPlaceholder = "", int characterLimit = 0, object requestee = null, float3? point = null, floatQ? rotation = null)
	{
		IsKeyboardActive = true;
		if (SystemKeyboard != null)
		{
			SystemKeyboard.ShowKeyboard(currentText, keyboardType, autocorrection, multiline, secure, textPlaceholder, characterLimit);
		}
		else if (VR_Active)
		{
			Userspace.SummonKeyboard(targetText, point, rotation);
		}
		_lastKeyboardRequestee = requestee;
		this.KeyboardActivated?.Invoke();
	}

	public void HideKeyboard(object requestee = null)
	{
		if (requestee == _lastKeyboardRequestee)
		{
			IsKeyboardActive = false;
			if (SystemKeyboard != null)
			{
				SystemKeyboard.HideKeyboard();
			}
			else
			{
				Userspace.HideKeyboard();
			}
			this.KeyboardDeactivated?.Invoke();
		}
	}

	public List<T> GetDevices<T>(Predicate<T> predicate = null) where T : class, IInputDevice
	{
		List<T> list = new List<T>();
		GetDevices(list, predicate);
		return list;
	}

	public void GetDevices<T>(List<T> list, Predicate<T> predicate = null) where T : class, IInputDevice
	{
		foreach (IInputDevice inputDevice in inputDevices)
		{
			if (inputDevice is T typedDevice && (predicate == null || predicate(typedDevice)))
			{
				list.Add(typedDevice);
			}
		}
	}

	public void ForEachDevice<T>(Action<T> action) where T : class, IInputDevice
	{
		foreach (IInputDevice inputDevice in inputDevices)
		{
			if (inputDevice is T typedDevice)
			{
				action(typedDevice);
			}
		}
	}

	public T GetDevice<T>(Predicate<T> predicate = null) where T : class, IInputDevice
	{
		foreach (IInputDevice inputDevice in inputDevices)
		{
			if (inputDevice is T typedDevice && (predicate == null || predicate(typedDevice)))
			{
				return typedDevice;
			}
		}
		return null;
	}

	public void TypeAppend(string typeDelta, World origin)
	{
		if (origin == Userspace.UserspaceWorld || !Userspace.HasFocus)
		{
			if (_virtualTypeDelta == null)
			{
				_virtualTypeDelta = typeDelta;
			}
			else
			{
				_virtualTypeDelta += typeDelta;
			}
			this.OnTypeAppend?.Invoke(typeDelta);
		}
	}

	public void SimulatePress(Key key, World origin)
	{
		if (origin == Userspace.UserspaceWorld || !Userspace.HasFocus)
		{
			if (!_virtualPresses.ContainsKey(key))
			{
				_virtualPresses.Add(key, origin);
			}
			this.OnSimulatedPress?.Invoke(key);
		}
	}

	public bool GetAnyKey()
	{
		for (int i = 0; i < keys.Length; i++)
		{
			if (keys[i].Held)
			{
				return true;
			}
		}
		return false;
	}

	public bool GetAnyKey(params Key[] keys)
	{
		for (int i = 0; i < keys.Length; i++)
		{
			if (GetKey(keys[i]))
			{
				return true;
			}
		}
		return false;
	}

	public bool IsKeyVirtualPressed(Key key)
	{
		return keyVirtualState[(int)key] != null;
	}

	public World VirtualPressSource(Key key)
	{
		return keyVirtualState[(int)key];
	}

	public bool GetKey(Key key)
	{
		return keys[(int)key].Held;
	}

	public bool GetKeyDown(Key key)
	{
		return keys[(int)key].Pressed;
	}

	public bool GetKeyRepeat(Key key)
	{
		return keys[(int)key].RepeatPressed;
	}

	public bool GetKeyUp(Key key)
	{
		return keys[(int)key].Released;
	}

	public RepeatDigital GetKeyState(Key key)
	{
		return keys[(int)key];
	}

	public void RegisterInputDevice(IInputDevice device, string name)
	{
		device.Initialize(this, inputDevices.Count, name);
		inputDevices.Add(device);
		try
		{
			this._inputDeviceAdded?.Invoke(device);
		}
		catch (Exception ex)
		{
			UniLog.Error("Exception when calling InputDeviceAdded event:\n" + ex);
		}
		if (device is IBindingGenerator generator && !bindingGenerators.Contains(generator))
		{
			RegisterBindingGenerator(generator);
		}
	}

	public void RegisterBindingGenerator(IBindingGenerator generator)
	{
		if (bindingGenerators.Contains(generator))
		{
			throw new InvalidOperationException("Binding generator is already registered: " + generator);
		}
		bindingGenerators.Add(generator);
		try
		{
			this.BindingGeneratorAdded?.Invoke(generator);
		}
		catch (Exception ex)
		{
			UniLog.Error("Exception when calling BindingGeneratorAdded event:\n" + ex);
		}
	}

	public FrooxEngine.ITrackedDevice GetBodyNode(BodyNode node)
	{
		return bodyNodes[(int)node];
	}

	public IStandardController GetControllerNode(Chirality side)
	{
		switch (side)
		{
		case Chirality.Left:
		{
			IStandardController standardController2 = leftController;
			IStandardController result2;
			if (standardController2 == null || !standardController2.IsDeviceActive)
			{
				IStandardController rightVirtualController = LeftVirtualController;
				result2 = rightVirtualController;
			}
			else
			{
				result2 = leftController;
			}
			return result2;
		}
		case Chirality.Right:
		{
			IStandardController standardController = rightController;
			IStandardController result;
			if (standardController == null || !standardController.IsDeviceActive)
			{
				IStandardController rightVirtualController = RightVirtualController;
				result = rightVirtualController;
			}
			else
			{
				result = rightController;
			}
			return result;
		}
		default:
			return null;
		}
	}

	public IStandardController GetVRControllerNode(Chirality side)
	{
		return side switch
		{
			Chirality.Left => leftController, 
			Chirality.Right => rightController, 
			_ => null, 
		};
	}

	public VirtualController GetVirtualController(Chirality side)
	{
		return side switch
		{
			Chirality.Left => LeftVirtualController, 
			Chirality.Right => RightVirtualController, 
			_ => null, 
		};
	}

	public void RegisterInputDriver(IInputDriver driver)
	{
		inputDrivers.Add(driver);
		driver.RegisterInputs(this);
		UpdateBucket bucket = inputDriverUpdateBuckets.FirstOrDefault((UpdateBucket b) => b.order == driver.UpdateOrder);
		if (bucket == null)
		{
			bucket = new UpdateBucket(driver.UpdateOrder);
			inputDriverUpdateBuckets.Add(bucket);
			inputDriverUpdateBuckets.Sort((UpdateBucket a, UpdateBucket b) => a.order.CompareTo(b.order));
		}
		bucket.inputDrivers.Add(driver);
	}

	public void RegisterHapticPoint(HapticPoint point)
	{
		point.Index = hapticPoints.Count;
		hapticPoints.Add(point);
		_hapticPointsChanged = true;
		point.OnPositionUpdated += delegate
		{
			_hapticPointsChanged = true;
		};
	}

	public HapticPoint GetHapticPoint(int index)
	{
		return hapticPoints[index];
	}

	public HapticPoint FindHapticPoint<P>(Predicate<P> predicate)
	{
		foreach (HapticPoint point in hapticPoints)
		{
			if (point.Position is P pos && predicate(pos))
			{
				return point;
			}
		}
		return null;
	}

	public IEnumerable<HapticPoint> GetHapticPoints<P>() where P : HapticPointPosition
	{
		for (int i = 0; i < hapticPoints.Count; i++)
		{
			HapticPoint point = hapticPoints[i];
			if (point.Position is P)
			{
				yield return point;
			}
		}
	}

	public T GetDriver<T>(Predicate<T> filter = null) where T : class, IInputDriver
	{
		foreach (IInputDriver inputDriver in inputDrivers)
		{
			if (inputDriver is T typedDriver && (filter == null || filter(typedDriver)))
			{
				return typedDriver;
			}
		}
		return null;
	}

	public IInputDevice GetDevice(int index)
	{
		return inputDevices[index];
	}

	public T CreateDevice<T>(string name) where T : IInputDevice, new()
	{
		T device = new T();
		InitDeviceFields(device);
		RegisterInputDevice(device, name);
		return device;
	}

	public void RegisterController(IStandardController controller)
	{
		if (!(controller is VirtualController))
		{
			switch (controller.Side)
			{
			case Chirality.Left:
				leftController = controller;
				break;
			case Chirality.Right:
				rightController = controller;
				break;
			default:
				throw new ArgumentOutOfRangeException("Invalid chirality: " + controller.Side);
			}
			this.ControllerRegistered?.Invoke(controller);
		}
	}

	public void Bind(InputGroup group)
	{
		foreach (IBindingGenerator bindingGenerator in bindingGenerators)
		{
			bindingGenerator.Bind(group);
		}
	}

	public void RegisterBlockedProperty(ControllerProperty property)
	{
		_globallyBlockedProperties.Add(property);
	}

	public void RegisterGlobalBlockers(Type type, IList list)
	{
		if (!_globalBlockers.TryGetValue(type, out IList targetList))
		{
			targetList = (IList)Activator.CreateInstance(list.GetType());
			_globalBlockers.Add(type, targetList);
		}
		foreach (object o in list)
		{
			targetList.Add(o);
		}
	}

	public void RegisterGlobalBlock<T>(T blocker)
	{
		List<T> blockers;
		if (!_globalBlockers.TryGetValue(typeof(T), out IList list))
		{
			blockers = new List<T>();
			_globalBlockers.Add(typeof(T), blockers);
		}
		else
		{
			blockers = (List<T>)list;
		}
		blockers.Add(blocker);
	}

	public void GetBlockedProperties(HashSet<ControllerProperty> target)
	{
		foreach (ControllerProperty p in _globallyBlockedProperties)
		{
			target.Add(p);
		}
	}

	public void GetGlobalBlockers(Dictionary<Type, IList> target)
	{
		foreach (KeyValuePair<Type, IList> group in _globalBlockers)
		{
			if (!target.TryGetValue(group.Key, out IList targetList))
			{
				targetList = (IList)Activator.CreateInstance(group.Value.GetType());
				target.Add(group.Key, targetList);
			}
			foreach (object b in group.Value)
			{
				targetList.Add(b);
			}
		}
	}

	public bool IsBlocked<T>(Predicate<T> check)
	{
		if (_globalBlockers.TryGetValue(typeof(T), out IList list))
		{
			foreach (T b in (List<T>)list)
			{
				if (check(b))
				{
					return true;
				}
			}
		}
		return false;
	}

	private void ClearBlockers()
	{
		_globallyBlockedProperties.Clear();
		foreach (KeyValuePair<Type, IList> globalBlocker in _globalBlockers)
		{
			globalBlocker.Value.Clear();
		}
	}

	internal static void InitDeviceFields(IInputDevice device)
	{
		foreach (FieldInfo f in device.GetType().EnumerateAllInstanceFields())
		{
			if (f.IsInitOnly && f.GetValue(device) == null)
			{
				QuantizationDefaultsAttribute quantization = f.GetCustomAttribute<QuantizationDefaultsAttribute>();
				object instance = Activator.CreateInstance(f.FieldType);
				f.SetValue(device, instance);
				if (instance is ControllerProperty controllerProperty)
				{
					controllerProperty.Initialize(device, device.PropertyCount, f.Name, quantization);
					device.RegisterProperty(controllerProperty);
				}
			}
		}
	}

	public static string LogDeviceFields(IInputDevice device)
	{
		StringBuilder str = new StringBuilder();
		Type deviceType = device.GetType();
		str.AppendLine("Device Type: " + deviceType);
		foreach (FieldInfo f in deviceType.EnumerateAllInstanceFields())
		{
			if (f.IsInitOnly)
			{
				object value = f.GetValue(device);
				if (value != null)
				{
					str.AppendLine(f.Name + ":\t" + value.ToString());
				}
			}
		}
		return str.ToString();
	}

	public DataTreeList CollectDeviceInfos()
	{
		DataTreeList list = new DataTreeList();
		foreach (IInputDriver inputDriver in inputDrivers)
		{
			inputDriver.CollectDeviceInfos(list);
		}
		foreach (string info in ExtraDeviceInfos)
		{
			list.Add(new DataTreeValue(info));
		}
		return list;
	}

	public void GetEyeTracking(out bool hasEyeTracking, out bool hasPupilTracking)
	{
		hasEyeTracking = false;
		hasPupilTracking = false;
		foreach (IInputDevice inputDevice in inputDevices)
		{
			if (inputDevice is Eyes eyes)
			{
				hasEyeTracking = true;
				hasPupilTracking |= eyes.SupportsPupilTracking;
				if (hasPupilTracking)
				{
					break;
				}
			}
		}
	}

	public HashSet<MouthParameterGroup> GetMouthTrackingParameters()
	{
		HashSet<MouthParameterGroup> set = new HashSet<MouthParameterGroup>();
		foreach (IInputDevice inputDevice in inputDevices)
		{
			if (!(inputDevice is Mouth mouth))
			{
				continue;
			}
			foreach (MouthParameterGroup param in mouth.SupportedParameters)
			{
				set.Add(param);
			}
		}
		return set;
	}

	public void Dispose()
	{
		Clipboard?.Dispose();
		InputInjector?.Dispose();
		SystemKeyboard?.Dispose();
		foreach (IInputDriver inputDriver in inputDrivers)
		{
			if (inputDriver is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}
	}
}
