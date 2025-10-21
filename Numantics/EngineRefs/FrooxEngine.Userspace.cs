// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Userspace
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using EnumsNET;
using FrooxEngine;
using FrooxEngine.Commands;
using FrooxEngine.CommonAvatar;
using FrooxEngine.Store;
using FrooxEngine.UIX;
using Renderite.Shared;
using SkyFrost.Base;

public class Userspace : Component, IEmptyAvatarSlotHandler, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	private readonly struct AutoLoginData
	{
		public readonly string userId;

		public readonly string token;

		public readonly bool machineBound;

		public bool HasCredentials
		{
			get
			{
				if (userId != null)
				{
					return token != null;
				}
				return false;
			}
		}

		public AutoLoginData(string userId, string token, bool machineBound)
		{
			this.userId = userId;
			this.token = token;
			this.machineBound = machineBound;
		}
	}

	public class AnchorData
	{
		public World lastPoseSource;

		public float3 position;

		public floatQ orientation;

		public float scale;

		public bool? overrideState;

		public bool HasPose
		{
			get
			{
				World world = lastPoseSource;
				if (world == null)
				{
					return false;
				}
				return world.Focus == FrooxEngine.World.WorldFocus.Focused;
			}
		}
	}

	public class ControllerData
	{
		public bool toolInUse;

		public float3 pointOffset;

		public float3 forward;

		public float distance;

		public bool userspaceLaserActive;

		public bool userspaceLaserHitTarget;

		public bool userspaceHoldingThings;

		public InteractionHandler userspaceController;

		public bool userspaceToggleHeld;

		public bool worldHoldingThings;
	}

	/// <summary>
	/// Encapsulates a request to load home, with an error if it occured.
	/// </summary>
	public readonly struct HomeWorldResult
	{
		public readonly World world;

		public readonly HttpStatusCode state;

		public readonly string error;

		public HomeWorldResult(World world)
		{
			this.world = world;
			error = null;
			state = HttpStatusCode.OK;
		}

		public HomeWorldResult(HttpStatusCode state, string error)
		{
			world = null;
			this.state = state;
			this.error = error;
		}
	}

	public enum WorldRelation
	{
		Independent,
		Nest,
		Replace
	}

	public static bool Autojoin;

	public static bool DoNotAutoLoadHome;

	public static string BootstrapClass;

	private static bool _userInterfaceEditmode;

	private static List<Func<Task>> _exitTasks = new List<Func<Task>>();

	private static bool ResetUserspace;

	private static Userspace thisobj;

	private int lastWorldIndex;

	private FocusManager _userspaceFocusManager;

	private WorldSwitcher _worldSwitcher;

	private double _lastEscapePress;

	private static VirtualKeyboard _keyboard;

	public static bool DEBUG_TEST;

	public static List<UserspacePointer> Pointers = new List<UserspacePointer>();

	private static AnchorData[] _anchorData = new AnchorData[Enums.GetMemberCount<FacetAnchorPoint>()];

	private static Dictionary<World, Dictionary<object, Grabber>> _worldsGrabbers = new Dictionary<World, Dictionary<object, Grabber>>();

	private static ControllerData[] _controllerData;

	private static DateTime _lastThumbnailUpdate;

	private static Dictionary<World, SessionThumbnailData> _sessionsThumbnailData = new Dictionary<World, SessionThumbnailData>();

	public const string HOME_WORLDS_FOLDER = "Worlds/HomeTemplates/";

	private static volatile int _activeSaveTaskCount;

	private static int worldIndex = 0;

	private static Func<World, bool> _localHomePredicate = (World w) => IdUtil.GetOwnerType(w.CorrespondingRecord?.OwnerId) == OwnerType.Machine && w.CorrespondingRecord.RecordId == "R-Home";

	public const float USER_DISCONNECT_TIMEOUT = 5f;

	private static bool _cloudHomeFirstOpened;

	public override bool UserspaceOnly => true;

	public static bool ForceLANOnly { get; private set; }

	public static bool KioskMode { get; private set; }

	public static Uri BenchmarkUrl { get; private set; }

	public static bool AnnounceHomeOnLAN { get; private set; }

	public static bool IsExitingApp { get; private set; }

	public static bool IsLoggingOut { get; internal set; }

	public static bool UserInterfaceEditMode
	{
		get
		{
			return _userInterfaceEditmode;
		}
		set
		{
			if (_userInterfaceEditmode != value)
			{
				Settings.UpdateActiveSetting(delegate(CustomizationSettings s)
				{
					s.UserInterfaceEditMode.Value = value;
				});
			}
		}
	}

	public static Userspace Current => thisobj;

	public static World UserspaceWorld => thisobj?.World;

	private WorldManager WorldManager => base.Engine.WorldManager;

	public static bool HasFocus => thisobj?._userspaceFocusManager?.Focus != null;

	public static bool? AutoLoginInProgress { get; private set; }

	public static bool ContextMenuOpened { get; private set; }

	public bool LeftControllerLoaded { get; private set; }

	public bool RightControllerLoaded { get; private set; }

	public CommandServer CommandServer { get; private set; }

	public static int ActiveSaveTaskCount => _activeSaveTaskCount;

	public static World LocalHome => thisobj.Engine.WorldManager.GetWorld(_localHomePredicate);

	public static World CloudHome
	{
		get
		{
			ProfileManager profile = thisobj.Cloud.Profile;
			Uri homeUrl = profile.GetCurrentFavorite(FavoriteEntity.Home) ?? thisobj.Cloud.Platform.GetRecordUri(profile.CurrentUserID, "R-Home");
			if (homeUrl == null)
			{
				return null;
			}
			World world = thisobj.WorldManager.GetWorld(homeUrl);
			if (world == null || world.IsDestroyed)
			{
				return null;
			}
			return world;
		}
	}

	public static event Action<bool> UserInterfaceEditModeChanged;

	public static void RegisterExitTask(Func<Task> callback)
	{
		lock (_exitTasks)
		{
			_exitTasks.Add(callback);
		}
	}

	public static void UnregisterExitTask(Func<Task> callback)
	{
		lock (_exitTasks)
		{
			_exitTasks.Remove(callback);
		}
	}

	public static void Defocus()
	{
		FocusManager focus = thisobj?._userspaceFocusManager;
		if (focus != null)
		{
			thisobj.RunSynchronously(delegate
			{
				focus.Focus = null;
			});
		}
	}

	public bool GetTutorialControllerLoaded(Chirality node)
	{
		return node switch
		{
			Chirality.Left => LeftControllerLoaded, 
			Chirality.Right => RightControllerLoaded, 
			_ => throw new ArgumentException("Invalid node: " + node), 
		};
	}

	public void SetTutorialControllerLoaded(Chirality node)
	{
		switch (node)
		{
		case Chirality.Left:
			LeftControllerLoaded = true;
			break;
		case Chirality.Right:
			RightControllerLoaded = true;
			break;
		default:
			throw new ArgumentException("Invalid node: " + node);
		}
	}

	public static World SetupUserspace(Engine engine)
	{
		if (engine.VerboseInit)
		{
			UniLog.Log("Starting Userspace world");
		}
		World world = engine.WorldManager.StartLocal(delegate(World w)
		{
			if (engine.VerboseInit)
			{
				UniLog.Log("Userspace world started, attaching Userspace component");
			}
			w.AddSlot("Userspace").AttachComponent<Userspace>();
		}, null, GlobalTypeRegistry.CoreAssemblies.Concat(GlobalTypeRegistry.UserspaceCoreAssemblies));
		engine.WorldManager.PrivateOverlayWorld(world);
		return world;
	}

	public static World StartUtilityWorld(WorldAction preset, DataTreeDictionary load = null)
	{
		World world = StartSession(preset, 0, null, load);
		if (AnnounceHomeOnLAN)
		{
			world.AccessLevel = SessionAccessLevel.LAN;
		}
		else
		{
			world.AccessLevel = SessionAccessLevel.Private;
		}
		return world;
	}

	private MethodInfo FindBootstrapMethod(string name)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		for (int i = 0; i < assemblies.Length; i++)
		{
			MethodInfo method = assemblies[i].GetType(name)?.GetMethod("Bootstrap");
			if (method != null)
			{
				return method;
			}
		}
		return null;
	}

	protected override void OnAttach()
	{
		if (thisobj != null)
		{
			throw new Exception("There can be only one instance of Userspace running!");
		}
		if (base.Engine.VerboseInit)
		{
			UniLog.Log("Userspace on Attach, running setup");
		}
		thisobj = this;
		if (base.Engine.VerboseInit)
		{
			UniLog.Log("Processing commandline arguments");
		}
		if (base.Engine.IsMobilePlatform)
		{
			DoNotAutoLoadHome = true;
		}
		string[] args = Environment.GetCommandLineArgs();
		MethodInfo bootstrapMethod = null;
		if (BootstrapClass != null)
		{
			bootstrapMethod = FindBootstrapMethod(BootstrapClass);
		}
		if (args != null)
		{
			int i;
			for (i = 0; i < args.Length; i++)
			{
				string _arg = args[i].ToLower();
				bool hasNext = i + 1 < args.Length;
				if (_arg.EndsWith("kiosk"))
				{
					KioskMode = true;
				}
				if (_arg.EndsWith("bench") && hasNext)
				{
					if (Uri.TryCreate(args[i + 1], UriKind.Absolute, out Uri benchmarkUrl))
					{
						BenchmarkUrl = benchmarkUrl;
					}
					else
					{
						UniLog.Warning("Invalid bench URL: " + args[i + 1]);
					}
				}
				if (_arg.EndsWith("resetuserspace"))
				{
					ResetUserspace = true;
				}
				if (_arg.EndsWith("forcelanonly"))
				{
					ForceLANOnly = true;
				}
				if (_arg.EndsWith("watchdog") && hasNext)
				{
					StartTask(async delegate
					{
						await Watchdog(args[i + 1]);
					});
				}
				if (_arg.EndsWith("bootstrap") && hasNext)
				{
					MethodInfo method = FindBootstrapMethod(args[i + 1]);
					if (method != null)
					{
						bootstrapMethod = method;
					}
				}
				if (_arg.EndsWith("deleteunsyncedcloudrecords"))
				{
					LocalDB db = base.Engine.LocalDB;
					foreach (FrooxEngine.Store.Record record in db.FetchRecordsAsync((FrooxEngine.Store.Record r) => true).Result)
					{
						if (IdUtil.GetOwnerType(record.OwnerId) != OwnerType.Machine && !record.IsSynced)
						{
							db.DeleteRecordAsync(record).Wait();
						}
					}
				}
				if (_arg.EndsWith("forcesyncconflictingcloudrecords"))
				{
					base.Engine.RecordManager.ForceConflictSync = true;
				}
			}
		}
		base.Slot.AttachComponent<ScreenModeController>();
		if (bootstrapMethod != null)
		{
			UniLog.Log($"Running custom boostrap: {bootstrapMethod.DeclaringType} {bootstrapMethod}");
			MethodInfo methodInfo = bootstrapMethod;
			object[] parameters = new Engine[1] { base.Engine };
			methodInfo.Invoke(null, parameters);
		}
		else
		{
			UniLog.Log("Running default bootstrap");
			Bootstrap();
		}
		SetupCommandServer();
		Settings.RegisterValueChanges<CustomizationSettings>(OnCustomizationSettingsChanged);
	}

	private void OnCustomizationSettingsChanged(CustomizationSettings settings)
	{
		if (settings.UserInterfaceEditMode.Value != _userInterfaceEditmode)
		{
			_userInterfaceEditmode = settings.UserInterfaceEditMode.Value;
			Userspace.UserInterfaceEditModeChanged?.Invoke(_userInterfaceEditmode);
		}
	}

	private async Task Watchdog(string file)
	{
		UniLog.Log("Watchdog file: " + file);
		while (true)
		{
			await DelaySeconds(1f);
			await Task.Run(delegate
			{
				try
				{
					File.WriteAllText(file, DateTime.Now.Ticks.ToString());
				}
				catch
				{
				}
			});
		}
	}

	private void CheckTutorialController(Chirality side)
	{
		if (GetTutorialControllerLoaded(side))
		{
			return;
		}
		IStandardController controller = base.InputInterface.GetControllerNode(side);
		if (controller == null)
		{
			return;
		}
		string modelPath = ((side == Chirality.Left) ? controller.LeftTutorialModel : controller.RightTutorialModel);
		SetTutorialControllerLoaded(side);
		if (modelPath == null)
		{
			return;
		}
		Slot s = base.LocalUserRoot.GetControllerSlot(side).AddSlot("TutorialController");
		s.StartTask(async delegate
		{
			await s.LoadObjectAsync(base.Cloud.Platform.GetSpawnObjectUri(modelPath));
			s = s.GetComponent<InventoryItem>()?.Unpack() ?? s;
			if (controller is TouchController touch)
			{
				if (touch.TouchModel == TouchControllerModel.CV1)
				{
					if (side == Chirality.Left)
					{
						s.LocalPosition = new float3(-0.01f, 0.04f, 0.03f);
					}
					else
					{
						s.LocalPosition = new float3(0.01f, 0.04f, 0.03f);
					}
					s.LocalRotation = floatQ.Euler(45f, -180f, 0f);
				}
				else
				{
					s.LocalPosition = float3.Zero;
					s.LocalRotation = floatQ.Identity;
				}
			}
			else
			{
				s.LocalPosition = float3.Zero;
				s.LocalRotation = floatQ.Euler(0f, 180f, 0f);
			}
			s.LocalScale = float3.One;
			s.GetComponentsInChildren<IGrabbable>().ForEach(delegate(IGrabbable g)
			{
				g.Destroy();
			});
			s.GetComponentsInChildren<ICollider>().ForEach(delegate(ICollider c)
			{
				c.Destroy();
			});
		});
	}

	protected override void OnCommonUpdate()
	{
		ContextMenuOpened = base.LocalUserRoot?.GetRegisteredComponent((ContextMenu m) => m.IsOpened) != null;
		base.LocalUser.UserID = base.Engine.Cloud.CurrentUserID;
		base.LocalUser.UserSessionId = base.Engine.Cloud.Status.UserSessionId;
		base.LocalUser.UserName = base.Engine.LocalUserName;
		if (base.InputInterface.GetKeyDown(Key.Escape) && base.InputInterface.GetKey(Key.LeftShift))
		{
			if (base.Time.WorldTime - _lastEscapePress < 0.5)
			{
				if (WorldManager.FocusedWorld == LocalHome)
				{
					ExitApp();
				}
				else
				{
					ExitWorld(WorldManager.FocusedWorld);
				}
			}
			_lastEscapePress = base.Time.WorldTime;
		}
		if (base.InputInterface.GetKey(Key.LeftControl) && base.InputInterface.GetKey(Key.LeftShift) && base.InputInterface.GetKeyDown(Key.Q))
		{
			if (WorldManager.FocusedWorld == LocalHome)
			{
				ExitApp();
			}
			else
			{
				ExitWorld(WorldManager.FocusedWorld);
			}
		}
		if (base.InputInterface.GetKey(Key.LeftControl) && base.InputInterface.GetKeyDown(Key.Tab))
		{
			for (int i = 0; i < WorldManager.WorldCount - 1; i++)
			{
				lastWorldIndex++;
				lastWorldIndex %= WorldManager.WorldCount;
				World world = WorldManager[lastWorldIndex];
				if (world.Focus == FrooxEngine.World.WorldFocus.Background && world.State == FrooxEngine.World.WorldState.Running)
				{
					WorldManager.FocusWorld(world);
					break;
				}
			}
		}
		if (base.InputInterface.GetKeyDown(Key.F3))
		{
			UniLog.Log("------------------------------------------");
			UniLog.Log("DEBUG_TEST");
			UniLog.Log("------------------------------------------");
			UniLog.Flush();
			DEBUG_TEST = true;
			base.Engine.RenderSystem.DebugLogNextFrame = true;
		}
		if (base.InputInterface.GetKeyDown(Key.F11))
		{
			UniLog.Log("IPC memory dump:\n" + base.Engine.RenderSystem.GenerateSharedMemoryDebugDiagnostic());
		}
		if (base.InputInterface.GetKeyDown(Key.S) && base.InputInterface.GetKey(Key.LeftControl))
		{
			Mouse mouse = base.InputInterface.Mouse;
			if ((mouse == null || !mouse.RightButton.Held) && !base.InputInterface.ScreenActive)
			{
				World focusedWorld = base.Engine.WorldManager.FocusedWorld;
				if (focusedWorld.IsAllowedToSaveWorld())
				{
					SaveType saveType = SaveType.Overwrite;
					if (base.InputInterface.GetKey(Key.LeftShift) || !CanSave(focusedWorld))
					{
						saveType = SaveType.SaveAs;
					}
					SaveWorldAuto(base.Engine.WorldManager.FocusedWorld, saveType, exitOnSave: false);
				}
			}
		}
		if (base.InputInterface.GetKeyDown(Key.F9))
		{
			StringBuilder str = new StringBuilder();
			EngineDebugDialog.GenerateBackgroundWorkerDiagnostic(base.Engine, str);
			UniLog.Log("BACKGROUND WORKER SNAPSHOT:\n" + str);
		}
		if (base.InputInterface.GetKeyDown(Key.F10))
		{
			StartTask(async delegate
			{
				string str2 = await WorldAssetReport.GenerateStaticAssetReport(WorldManager.FocusedWorld);
				if (base.InputInterface.IsClipboardSupported)
				{
					base.InputInterface.Clipboard.SetText(str2);
					UniLog.Log("Report Generated and copied to clipboard");
				}
				else
				{
					UniLog.Warning("Clipboard not supported on the platform");
				}
			});
		}
		UpdateSessionThumbnails();
	}

	private async Task PlatformUpdate()
	{
		while (true)
		{
			await DelaySeconds(15f);
			World world = WorldManager.FocusedWorld;
			if (world != null)
			{
				if (base.Cloud.Status.OnlineStatus == OnlineStatus.Invisible)
				{
					base.Engine.PlatformInterface.ClearCurrentStatus();
					continue;
				}
				bool isPrivate = !world.IsPublic || world.MaxUsers == 1 || world.HideFromListing;
				base.Engine.PlatformInterface.SetCurrentStatus(world, isPrivate, WorldManager.WorldCount - 1);
			}
		}
	}

	public static void ExitApp(bool saveHomes = true)
	{
		if (IsExitingApp)
		{
			return;
		}
		UniLog.Log("Exiting. Save Homes: " + saveHomes);
		IsExitingApp = true;
		List<Task> saveTasks = new List<Task>();
		lock (_exitTasks)
		{
			foreach (Func<Task> task in _exitTasks)
			{
				saveTasks.Add(Task.Run(task));
			}
		}
		if (thisobj.Engine.Cloud.CurrentUser != null)
		{
			saveTasks.Add(Task.Run(async delegate
			{
				await thisobj.Engine.Cloud.FinalizeSession();
			}));
		}
		else
		{
			saveTasks.Add(Task.Run(async delegate
			{
				await thisobj.Engine.Cloud.Variables.SaveAllChangedVariables();
			}));
		}
		thisobj.RunSynchronously(delegate
		{
			Slot rootSlot = thisobj.World.RootSlot;
			Slot slot = rootSlot.GetComponentInChildren<WorldSwitcher>()?.Slot;
			if (slot != null)
			{
				slot.ActiveSelf = false;
			}
			UserspaceRadiantDash componentInChildren = rootSlot.GetComponentInChildren<UserspaceRadiantDash>();
			if (componentInChildren != null)
			{
				componentInChildren.Slot.ActiveSelf = false;
				componentInChildren.Dash.Open.Value = false;
				componentInChildren.BlockOpenClose.Value = true;
			}
		});
		foreach (World w in thisobj.Engine.WorldManager.Worlds)
		{
			bool isHome = w.CorrespondingRecord?.RecordId == "R-Home" || w == LocalHome || w == CloudHome;
			if (((saveHomes && isHome) || w.SaveOnExit) && CanSave(w))
			{
				saveTasks.Add(SaveWorld(w));
			}
		}
		WorldAction enderTemplate = WorldPresets.UtilityWorld;
		if (thisobj.Engine.UniverseId != null)
		{
			enderTemplate = WorldPresets.VoidWorld;
		}
		World enderWorld = StartLocal(enderTemplate);
		enderWorld.RunSynchronously(delegate
		{
			CommonAvatarBuilder componentInChildren = enderWorld.RootSlot.GetComponentInChildren<CommonAvatarBuilder>();
			componentInChildren.LoadCloudAvatars.Value = false;
			componentInChildren.SetupLocomotion.Value = false;
			componentInChildren.SetupIconBadges.Value = false;
			componentInChildren.SetupNameBadges.Value = false;
			componentInChildren.SetupItemShelves.Value = false;
			componentInChildren.SetupServerTools.Value = false;
			componentInChildren.SetupServerVoice.Value = false;
			Slot slot = enderWorld.AddSlot("Ender");
			AppEnder ender = slot.AttachComponent<AppEnder>();
			ender.Mode.Value = AppEnder.EndMode.Exit;
			ender.Initialized = true;
			ender.StartTask(async delegate
			{
				try
				{
					await Task.WhenAll(saveTasks);
				}
				catch (Exception ex)
				{
					UniLog.Error("Exception in save tasks:\n" + ex);
				}
				ender.ChangesSaved.Value = true;
			});
		});
	}

	public static void LogoutUser()
	{
		if (IsLoggingOut)
		{
			return;
		}
		IsLoggingOut = true;
		Task.Run(async delegate
		{
			await thisobj.ClearAutoLoginData();
			await thisobj.Cloud.FinalizeSession();
			World userHome = CloudHome;
			List<Task> saveTasks = new List<Task>();
			if (userHome != null && CanSave(userHome))
			{
				saveTasks.Add(SaveWorld(userHome));
			}
			lock (_exitTasks)
			{
				foreach (Func<Task> task in _exitTasks)
				{
					saveTasks.Add(Task.Run(task));
				}
			}
			World enderWorld = StartLocal(WorldPresets.UtilityWorld);
			enderWorld.RunSynchronously(delegate
			{
				Slot slot = enderWorld.AddSlot("Ender");
				AppEnder ender = slot.AttachComponent<AppEnder>();
				ender.Mode.Value = AppEnder.EndMode.Logout;
				ender.Initialized = true;
				enderWorld.Coroutines.StartTask(async delegate
				{
					if (saveTasks.Count > 0)
					{
						await Task.WhenAll(saveTasks);
					}
					try
					{
						List<World> worlds = Pool.BorrowList<World>();
						thisobj.Engine.WorldManager.GetWorlds(worlds);
						foreach (World world in worlds)
						{
							if (world.IsAuthority && world.HostUser.UserID != null && !world.IsUserspace() && world != LocalHome && world != enderWorld)
							{
								await ExitWorld(world);
							}
						}
						Pool.Return(ref worlds);
					}
					catch (Exception ex)
					{
						UniLog.Error("Exception exiting user world:\n" + ex);
					}
					ender.ChangesSaved.Value = true;
				});
			});
		});
	}

	public static Task ExitWorld(World world)
	{
		if (world == LocalHome)
		{
			throw new Exception("Cannot Exit LocalHome, use Engine Shutdown to exit.");
		}
		if (world.IsAuthority)
		{
			return EndSession(world);
		}
		return LeaveSession(world);
	}

	public static bool ShouldSave(World world)
	{
		if (CanSave(world))
		{
			return true;
		}
		if (world.IsAuthority && world.CorrespondingRecord == null)
		{
			return true;
		}
		return false;
	}

	public static bool CanSave(World world)
	{
		_ = thisobj.Engine.Cloud;
		if (world.CorrespondingRecord == null)
		{
			return false;
		}
		if (world.CorrespondingRecord.IsReadOnly)
		{
			return false;
		}
		string ownerId = world.CorrespondingRecord.OwnerId;
		return thisobj.Engine.RecordManager.CanModify(ownerId);
	}

	private IEnumerator<Context> AutoJoin(string onlyUser)
	{
		onlyUser = onlyUser?.Trim();
		UniLog.Log("Running Autojoin, HostFilter: " + onlyUser);
		List<string> _joinTimeout = new List<string>();
		List<World> _worlds = new List<World>();
		List<SessionInfo> _fetchedSessions = new List<SessionInfo>();
		while (true)
		{
			_fetchedSessions.Clear();
			base.Engine.Cloud.Sessions.GetSessions(_fetchedSessions);
			_fetchedSessions.RemoveAll((SessionInfo s) => s.LAN_URL != null);
			string hostName = null;
			if (onlyUser != null && onlyUser.StartsWith("U-"))
			{
				break;
			}
			hostName = onlyUser;
			IEnumerable<SessionInfo> candidateWorlds = _fetchedSessions.Where((SessionInfo info) => !_joinTimeout.Contains(info.SessionId) && WorldManager.GetWorld((World w) => w.SessionId == info.SessionId) == null && WorldManager.GetWorld((World w) => w.SourceURLs.Any((Uri u) => info.GetSessionURLs().Contains(u))) == null);
			if (base.Engine.Platform == Platform.Android)
			{
				candidateWorlds = candidateWorlds.Where((SessionInfo info) => info.MobileFriendly);
			}
			candidateWorlds = ((hostName == null) ? candidateWorlds.Where((SessionInfo info) => info.IsOnLAN) : candidateWorlds.Where((SessionInfo info) => info.HostUsername.Trim() == hostName));
			SessionInfo target = candidateWorlds.FirstOrDefault();
			if (target != null)
			{
				_joinTimeout.Add(target.SessionId);
				RunInSeconds(30f, delegate
				{
					_joinTimeout.Remove(target.SessionId);
				});
				UniLog.Log("Joining session " + target.SessionId + ", URLs: " + string.Join(",", target.SessionURLs));
				JoinSession(target.GetSessionURLs()).WorldDestroyed += delegate(World w)
				{
					UniLog.Log($"Session {target.SessionId} {string.Join(", ", w.SourceURLs)} world destroyed, adding timeout");
					_joinTimeout.Add(target.SessionId);
					RunInSeconds(20f, delegate
					{
						_joinTimeout.Remove(target.SessionId);
					});
				};
			}
			_worlds.Clear();
			_worlds.AddRange(WorldManager.Worlds.Where((World w) => w.State == FrooxEngine.World.WorldState.Running && !w.IsAuthority && !w.LocalUser.DisconnectRequested));
			int maxUsers = 0;
			World maxUsersWorld = null;
			if (onlyUser == null)
			{
				for (int i = 0; i < _worlds.Count; i++)
				{
					int activeUsers = _worlds[i].AllUsers.Count((FrooxEngine.User u) => !u.IsLocalUser && u.IsPresentInWorld && u.HeadDevice != HeadOutputDevice.StaticCamera && u.HeadDevice != HeadOutputDevice.StaticCamera360);
					if (activeUsers > maxUsers)
					{
						maxUsers = activeUsers;
						maxUsersWorld = _worlds[i];
					}
					if (activeUsers == maxUsers && _worlds[i].Focus == FrooxEngine.World.WorldFocus.Focused)
					{
						maxUsersWorld = _worlds[i];
					}
				}
			}
			else
			{
				maxUsersWorld = _worlds.FirstOrDefault((World w) => w.TryGetUser((FrooxEngine.User u) => u.UserID == onlyUser || u.UserName == onlyUser)?.IsPresentInWorld ?? false);
			}
			if (maxUsersWorld != null && maxUsersWorld != WorldManager.FocusedWorld)
			{
				WorldManager.FocusWorld(maxUsersWorld);
			}
			yield return Context.WaitForSeconds(1.0);
		}
		throw new NotImplementedException();
	}

	public static UserRoot SetupProxyAvatar(World world, out InteractionHandler leftTool, out InteractionHandler rightTool, out FocusManager focusManager)
	{
		_ = world.InputInterface;
		CommonAvatarBuilder commonAvatarBuilder = world.AddSlot("Avatar Builder").AttachComponent<CommonAvatarBuilder>();
		commonAvatarBuilder.SetupServerVoice.Value = false;
		commonAvatarBuilder.SetupClientVoice.Value = false;
		commonAvatarBuilder.AllowLocomotion.Value = true;
		commonAvatarBuilder.FillEmptySlots.Value = false;
		commonAvatarBuilder.SetupLocomotion.Value = false;
		Slot userRootSlot = world.AddSlot("UserRoot");
		UserRoot userRoot = userRootSlot.AttachComponent<UserRoot>();
		world.LocalUser.Root = userRoot;
		leftTool = null;
		rightTool = null;
		focusManager = world.LocalUser.AttachComponent<FocusManager>();
		commonAvatarBuilder.BuildDevices(world.LocalUser, userRoot, userRootSlot, out Slot _, out List<InteractionHandler> commonTools);
		foreach (InteractionHandler commonTool in commonTools)
		{
			UserspacePointer userspacePointer = world.AddSlot("UserspaceTip").AttachComponent<UserspacePointer>();
			commonTool.Equip(userspacePointer, lockEquip: true);
			userspacePointer.Slot.SetIdentityTransform();
			commonTool.EquippingEnabled.Value = false;
			commonTool.UserScalingEnabled.Value = false;
			commonTool.VisualEnabled.Value = false;
			if ((Chirality)commonTool.Side == Chirality.Left)
			{
				leftTool = commonTool;
			}
			else if ((Chirality)commonTool.Side == Chirality.Right)
			{
				rightTool = commonTool;
			}
		}
		AvatarManager avatarManager = userRootSlot.AttachComponent<AvatarManager>();
		avatarManager.AutoAddNameBadge.Value = false;
		avatarManager.AutoAddIconBadge.Value = false;
		avatarManager.AutoAddLiveIndicator.Value = false;
		avatarManager.EmptySlotHandler.Target = thisobj;
		avatarManager.FillEmptySlots();
		return userRoot;
	}

	public void SpawnVirtualKeyboard()
	{
		SpawnVirtualKeyboard(base.World);
	}

	public static void SpawnVirtualKeyboard(World world)
	{
		world.Coroutines.StartTask(async delegate
		{
			await default(ToWorld);
			VirtualKeyboard keyboard = await world.AddSlot("Virtual Keyboard").SpawnEntity(FavoriteEntity.Keyboard, (Slot s) => s.AttachComponent<SimpleVirtualKeyboard>().Slot.GetComponent<VirtualKeyboard>());
			if (_keyboard != null && !_keyboard.IsRemoved)
			{
				keyboard.Slot.CopyTransform(_keyboard.Slot);
				keyboard.IsShown = _keyboard.IsShown;
				_keyboard.Slot.Destroy();
			}
			else
			{
				keyboard.IsShown = false;
			}
			_keyboard = keyboard;
		});
	}

	private void OnActiveKeyboardChanged(Uri url)
	{
		SpawnVirtualKeyboard();
	}

	private async Task BootstrapAsync()
	{
		_ = 4;
		try
		{
			UniLog.Log("BOOTSTRAP: Processing launch arguments");
			ProcessLaunchArgs(out bool noUI, out bool autoJoin, out string autojoinHost, out bool forceTutorial, out bool skipTutorial);
			UniLog.Log("BOOTSTRAP: Checking auto-login");
			AutoLoginData autoLoginData = ((!forceTutorial && !(BenchmarkUrl != null)) ? (await GetAutoLoginData()) : default(AutoLoginData));
			if (FrooxEngine.Engine.Config.DisableIntroTutorial)
			{
				skipTutorial = true;
				forceTutorial = false;
			}
			if (autoLoginData.HasCredentials)
			{
				skipTutorial = true;
				AutoLoginInProgress = true;
			}
			else
			{
				AutoLoginInProgress = false;
			}
			AppConfig config = FrooxEngine.Engine.Config;
			int num;
			if (config == null || !(config.AutoStartWorlds?.Count > 0))
			{
				AppConfig config2 = FrooxEngine.Engine.Config;
				num = ((config2 != null && config2.AutoJoinSessions?.Count > 0) ? 1 : 0);
			}
			else
			{
				num = 1;
			}
			bool hasAutostart = (byte)num != 0;
			UniLog.Log("BOOTSTRAP: Setting up settings");
			await SetupSettings();
			UniLog.Log("BOOTSTRAP: Setting up core UI");
			SetupCoreUI(noUI);
			if (base.LocalUser.HeadDevice != HeadOutputDevice.Headless)
			{
				UniLog.Log("BOOTSTRAP: Loading local home");
				await StartTask(async delegate
				{
					await OpenLocalHomeAsync();
				});
			}
			UniLog.Log($"BOOTSTRAP: Checking tutorial start. Force: {forceTutorial}, Skip: {skipTutorial}");
			await StartTutorial(hasAutostart, forceTutorial, skipTutorial);
			if (autoLoginData.HasCredentials)
			{
				UniLog.Log("BOOTSTRAP: Running automatic login");
				await RunAutomaticLogin(autoLoginData);
			}
			if (autoJoin)
			{
				UniLog.Log("BOOTSTRAP: Running autojoin");
				base.World.Coroutines.StartCoroutine(AutoJoin(autojoinHost));
			}
			if (hasAutostart)
			{
				UniLog.Log("BOOTSTRAP: Running autostart");
				RunAutostart();
			}
			UniLog.Log("BOOTSTRAP: Starting platform update");
			StartTask(PlatformUpdate);
			if (BenchmarkUrl != null)
			{
				StartTask(BenchWorker);
			}
			UniLog.Log("BOOTSTRAP: Bootstrap complete");
		}
		catch (Exception ex)
		{
			UniLog.Error("BOOTSTRAP EXCEPTION!\n" + ex);
			base.Engine.RequestShutdown();
		}
	}

	private void ProcessLaunchArgs(out bool noUI, out bool autoJoin, out string autojoinHost, out bool forceTutorial, out bool skipTutorial)
	{
		string[] args = Environment.GetCommandLineArgs();
		noUI = false;
		autoJoin = Autojoin;
		autojoinHost = null;
		forceTutorial = false;
		skipTutorial = false;
		if (args != null)
		{
			for (int i = 0; i < args.Length; i++)
			{
				string lowerArg = args[i].ToLower();
				if (lowerArg.EndsWith("forceintrotutorial"))
				{
					forceTutorial = true;
				}
				if (lowerArg.EndsWith("skipintrotutorial"))
				{
					skipTutorial = true;
				}
				if (lowerArg.EndsWith("onlyhost") && args.Length > i + 1)
				{
					autojoinHost = args[i + 1];
					if (autojoinHost[0] == '"' && autojoinHost[autojoinHost.Length - 1] == '"')
					{
						autojoinHost = autojoinHost.Substring(1, autojoinHost.Length - 2);
					}
				}
				if (args[i].Contains("Join") && args.Length > i + 1)
				{
					if (args[i + 1].Contains("Auto"))
					{
						autoJoin = true;
						noUI = true;
					}
					else if (Uri.IsWellFormedUriString(args[i + 1], UriKind.Absolute))
					{
						JoinSession(new Uri(args[i + 1]));
					}
					else
					{
						string address = args[i + 1];
						JoinSession(new Uri("lnl://" + address));
					}
				}
				if ((lowerArg.EndsWith("open") || lowerArg.EndsWith("openunsafe")) && args.Length > i + 1)
				{
					string urlStr = args[i + 1];
					if (Uri.IsWellFormedUriString(urlStr, UriKind.Absolute))
					{
						bool unsafeMode = lowerArg.EndsWith("openunsafe");
						if (unsafeMode && FrooxEngine.Engine.Config?.UnsafeModeWhitelist?.Contains(urlStr) != true)
						{
							UniLog.Warning("Cannot open " + urlStr + " in unsafe mode, make sure to add it to the Config.json");
							continue;
						}
						WorldStartSettings startInfo = new WorldStartSettings(new Uri(urlStr))
						{
							AutoFocus = true,
							UnsafeMode = unsafeMode
						};
						UniLog.Log("Opening URL from command line: " + urlStr);
						StartTask(async delegate
						{
							while (!base.Engine.IsReady)
							{
								await default(NextUpdate);
							}
							while (AutoLoginInProgress == true)
							{
								await default(NextUpdate);
							}
							await default(NextUpdate);
							await OpenWorld(startInfo);
						});
					}
					else
					{
						UniLog.Warning("Invalid URI with the open command: " + urlStr);
					}
				}
				if (lowerArg.EndsWith("noui"))
				{
					noUI = true;
				}
			}
		}
		if (BenchmarkUrl != null)
		{
			noUI = true;
		}
		AppConfig config = FrooxEngine.Engine.Config;
		if (config != null && config.NoUI)
		{
			noUI = true;
		}
		if (File.Exists(Path.Combine(base.Engine.ConfigFileRoot, "NoUI")))
		{
			noUI = true;
		}
		AnnounceHomeOnLAN = args?.Any((string arg) => arg.ToLower().Contains("AnnounceHomeOnLAN".ToLower())) ?? false;
		if (KioskMode)
		{
			noUI = true;
		}
	}

	private void SetupCoreUI(bool noUI)
	{
		Slot worldSwitcherRoot = null;
		InteractionHandler leftTool = null;
		InteractionHandler rightTool = null;
		if (base.InputInterface.HeadOutputDevice != HeadOutputDevice.Headless)
		{
			SetupProxyAvatar(base.World, out leftTool, out rightTool, out _userspaceFocusManager);
			UniLog.Log($"Has LeftTool: {leftTool != null}, Has RightTool: {rightTool != null}");
			if (!noUI && leftTool != null && rightTool != null)
			{
				worldSwitcherRoot = rightTool.GrabIgnore.AddSlot("WorldSwitcher");
				worldSwitcherRoot.LocalPosition = float3.Forward * 0.1f;
				SpawnVirtualKeyboard();
				base.Engine.Cloud.Profile.RegisterListener(FavoriteEntity.Keyboard, OnActiveKeyboardChanged);
			}
		}
		if (base.InputInterface.HeadOutputDevice == HeadOutputDevice.StaticCamera || base.InputInterface.HeadOutputDevice == HeadOutputDevice.StaticCamera360 || base.InputInterface.HeadOutputDevice == HeadOutputDevice.Headless || noUI)
		{
			return;
		}
		Slot appDashSlot = base.World.AddSlot("Dash");
		UserspaceRadiantDash appDash = appDashSlot.AttachComponent<UserspaceRadiantDash>();
		Slot appFacetSlot = base.World.AddSlot("FacetAnchors");
		UserspaceFacetAnchorsManager appFacets = appFacetSlot.AttachComponent<UserspaceFacetAnchorsManager>();
		appFacets.Dash.Target = appDash;
		base.Engine.OnReady += delegate
		{
			RunInSeconds(2f, delegate
			{
				appDash.Open = true;
			});
		};
		if (leftTool != null)
		{
			leftTool._userspaceToggle = delegate(InteractionHandler tool)
			{
				if (!leftTool.IsRemoved && !tool.IsRemoved)
				{
					LegacyFeatureSettings? activeSetting = Settings.GetActiveSetting<LegacyFeatureSettings>();
					if (activeSetting != null && activeSetting.UseLegacyInventorySessionShortcuts.Value && tool.Inputs.Grab.Held)
					{
						appDash.ToggleLegacyInventory();
					}
					else if (appFacets.UseFacetAnchors.Value && appFacets.Toggle.Value == Chirality.Left)
					{
						appFacets.ToggleOpen();
					}
					else
					{
						appDash.Open = !appDash.Open;
					}
				}
			};
		}
		if (!base.InputInterface.HeadOutputDevice.IsNewInteractionMode() || worldSwitcherRoot == null)
		{
			return;
		}
		Slot switcherSlot = worldSwitcherRoot.AddSlot("WorldSwitcher");
		WorldSwitcher switcher = switcherSlot.AttachComponent<WorldSwitcher>();
		switcher.IgnoreTouchesFrom.Target = switcherSlot.GetComponentInParents<InteractionHandler>().Slot;
		switcherSlot.LocalPosition = new float3(0f, -0.12f, -0.04f);
		switcherSlot.ActiveSelf = false;
		Settings.RegisterValueChanges(delegate(LegacyFeatureSettings s)
		{
			if (!switcherSlot.IsDestroyed)
			{
				switcherSlot.RunSynchronously(delegate
				{
					switcherSlot.ActiveSelf = s.UseLegacyWorldSwitcher.Value;
				});
			}
		});
		rightTool._userspaceToggle = delegate(InteractionHandler tool)
		{
			if (!rightTool.IsRemoved && !tool.IsRemoved)
			{
				bool flag = Settings.GetActiveSetting<LegacyFeatureSettings>()?.UseLegacyInventorySessionShortcuts.Value ?? false;
				if (appFacets.UseFacetAnchors.Value && appFacets.Toggle.Value == Chirality.Right)
				{
					appFacets.ToggleOpen();
				}
				else if (!appFacets.UseFacetAnchors.Value && switcherSlot.ActiveSelf)
				{
					if (flag && tool.Inputs.Grab.Held)
					{
						appDash.ToggleSessionControl();
					}
					else
					{
						switcher.Show.Value = !switcher.Show.Value;
					}
				}
				else
				{
					appDash.Open = !appDash.Open;
				}
			}
		};
	}

	private async Task SetupSettings()
	{
		await base.World.RootSlot.AddSlot("Settings").AttachComponent<SettingManagersManager>().LoadLocalSettings();
	}

	private async Task StartTutorial(bool hasAutostart, bool forceTutorial, bool skipTutorial)
	{
		bool startTutorial = true;
		if (hasAutostart)
		{
			startTutorial = false;
		}
		if (BenchmarkUrl != null)
		{
			startTutorial = false;
		}
		if (AutoLoginInProgress == true || base.Engine.Cloud.Session.CurrentSession != null)
		{
			startTutorial = false;
		}
		if (await base.Engine.LocalDB.GetRecordCount() > 20)
		{
			startTutorial = false;
		}
		if (base.Engine.InputInterface.HeadOutputDevice == HeadOutputDevice.Headless || base.Engine.InputInterface.HeadOutputDevice.IsCamera() || base.Engine.InputInterface.HeadOutputDevice.IsCameraMode())
		{
			startTutorial = false;
		}
		if (base.Engine.SystemInfo.Platform != Platform.Windows && base.Engine.SystemInfo.Platform != Platform.Linux)
		{
			startTutorial = false;
		}
		if (skipTutorial)
		{
			startTutorial = false;
		}
		if (forceTutorial)
		{
			startTutorial = true;
		}
		if (startTutorial)
		{
			UniLog.Log("BOOTSTRAP: Starting Tutorial");
			StartTask(async delegate
			{
				await new Updates(90);
				UserspaceRadiantDash globallyRegisteredComponent = base.World.GetGloballyRegisteredComponent<UserspaceRadiantDash>();
				TutorialScreen tutorialScreen = globallyRegisteredComponent.Dash.AttachScreen<TutorialScreen>();
				globallyRegisteredComponent.Dash.CurrentScreen.Target = tutorialScreen;
			});
		}
	}

	private async Task LaunchAutoStartWorld(WorldStartupParameters startParams)
	{
		_ = 2;
		try
		{
			UniLog.Log("Auto-startup world URL: " + startParams.LoadWorldURL + ", Preset: " + startParams.LoadWorldPresetName);
			WorldStartSettings startupInfo = await startParams.GenerateStartSettings(base.Engine.PlatformProfile);
			UniLog.Log($"Auto starting world:\n{startupInfo}");
			World world = await OpenWorld(startupInfo);
			if (world == null)
			{
				UniLog.Log("World failed to startup!");
			}
			else
			{
				await startParams.SetWorldParameters(world);
			}
		}
		catch (Exception value)
		{
			UniLog.Error($"Exception auto-starting up world:\n{value}", stackTrace: false);
		}
	}

	private async Task LaunchAutoStartSession(SessionJoinParameters session)
	{
		_ = 1;
		try
		{
			UniLog.Log("Auto joining session: " + session);
			if (await OpenWorld(await session.GenerateStartSettings(base.World.Engine)) == null)
			{
				UniLog.Log("Session join failed!");
			}
		}
		catch (Exception value)
		{
			UniLog.Error($"Exception auto-joining session:\n{value}", stackTrace: false);
		}
	}

	public void RunAutostart()
	{
		bool isUserLoggedIn = base.Engine.Cloud.Session.CurrentSession != null;
		StartTask(async delegate
		{
			await default(ToWorld);
			if (FrooxEngine.Engine.Config.AutoStartWorlds != null)
			{
				foreach (WorldStartupParameters startParams in FrooxEngine.Engine.Config.AutoStartWorlds)
				{
					if (startParams.IsEnabled && !startParams.Processed && (!startParams.WaitForLogin || isUserLoggedIn))
					{
						startParams.Processed = true;
						UniLog.Log("Running " + startParams.LoadWorldURL);
						await LaunchAutoStartWorld(startParams);
					}
				}
			}
			if (FrooxEngine.Engine.Config.AutoJoinSessions != null)
			{
				foreach (SessionJoinParameters session in FrooxEngine.Engine.Config.AutoJoinSessions)
				{
					if (!session.Processed && !(session.WaitForLogin && !isUserLoggedIn))
					{
						session.Processed = true;
						await LaunchAutoStartSession(session);
					}
				}
			}
		});
	}

	private void Bootstrap()
	{
		UniLog.Log("BOOTSTRAP: Running userspace bootstrap");
		base.Engine.OnShutdownRequest += Engine_OnShutdownRequest;
		base.Engine.OnShutdown += Engine_OnShutdown;
		base.Engine.LocalDB.DatabaseCorruptionDetected += LocalDB_DatabaseCorruptionDetected;
		DynamicVariableSpace dynamicVariableSpace = base.World.RootSlot.AttachComponent<DynamicVariableSpace>();
		dynamicVariableSpace.SpaceName.Value = "World";
		dynamicVariableSpace.OnlyDirectBinding.Value = true;
		base.World.Name = "Userspace";
		StartTask(BootstrapAsync);
	}

	private async Task BenchWorker()
	{
		UniLog.Log("Running benchmark, loading world: " + BenchmarkUrl);
		World world = await OpenWorld(new WorldStartSettings(BenchmarkUrl)
		{
			DefaultAccessLevel = SessionAccessLevel.Private,
			CreateLoadIndicator = false
		});
		if (world == null)
		{
			UniLog.Warning("Failed to load world! Aborting...");
			base.Engine.RequestShutdown();
			return;
		}
		while (world.State != FrooxEngine.World.WorldState.Running)
		{
			await default(NextUpdate);
		}
		UniLog.Log("World Started, waiting 30 seconds to stabilize...");
		await DelaySeconds(10f);
		world.RunSynchronously(delegate
		{
			world.LocalUser.Root.Slot.GlobalPosition = float3.Backward * 10;
		});
		await DelaySeconds(20f);
		UniLog.Log("Running benchmark...");
		CancellationTokenSource cancellationToken = new CancellationTokenSource();
		cancellationToken.CancelAfter(TimeSpan.FromSeconds(30L));
		await StartTask(async delegate
		{
			await EngineDebugDialog.RecordPerfMetrics(base.Engine, cancellationToken.Token);
		});
		UniLog.Log("Finishing benchmark");
		UniLog.Log("Quitting");
		base.Engine.RequestShutdown();
	}

	private void LocalDB_DatabaseCorruptionDetected()
	{
		StartTask(async delegate
		{
			await default(ToWorld);
			Slot slot = base.World.AddSlot("Message");
			slot.AttachComponent<UserInterfacePositioner>();
			Slot slot2 = slot.AddSlot("Text");
			slot2.LocalPosition = float3.Forward * 2;
			TextRenderer textRenderer = slot2.AttachComponent<TextRenderer>();
			textRenderer.Text.Value = "Database Corrupted!\n<size=50%>Please restart for automatic repair</size>";
			textRenderer.Color.Value = new colorX(1f, 0.5f, 0.5f);
		});
	}

	private async Task<AutoLoginData> GetAutoLoginData()
	{
		return new AutoLoginData(await base.Engine.LocalDB.ReadVariableAsync("User.AutoLoginId"), await base.Engine.LocalDB.ReadVariableAsync("User.AutoLoginToken"), await base.Engine.LocalDB.ReadVariableAsync("User.AutoLoginMachineBound", def: false));
	}

	private async Task RunAutomaticLogin(AutoLoginData data)
	{
		if (!data.HasCredentials)
		{
			throw new ArgumentException("Auto login data has no credentials!");
		}
		try
		{
			string token = data.token;
			if (data.machineBound)
			{
				token = CryptoHelper.HashIDToToken(token, base.Cloud.SecretMachineId + base.Cloud.UID);
			}
			if ((await base.Engine.Cloud.Session.Login(data.userId, new SessionTokenLogin(token), base.Engine.LocalDB.SecretMachineID, rememberMe: true, null)).IsError)
			{
				await ClearAutoLoginData();
			}
		}
		catch (Exception ex)
		{
			UniLog.Error("Exception in autologin process:\n" + ex);
			await ClearAutoLoginData();
		}
		finally
		{
			AutoLoginInProgress = false;
		}
	}

	private async Task ClearAutoLoginData()
	{
		await base.Engine.LocalDB.DeleteVariableAsync("User.AutoLoginId");
		await base.Engine.LocalDB.DeleteVariableAsync("User.AutoLoginToken");
		await base.Engine.LocalDB.DeleteVariableAsync("User.AutoLoginMachineBound");
	}

	private void Engine_OnShutdownRequest(string obj)
	{
		if (!IsExitingApp && base.Engine.InputInterface.HeadOutputDevice != HeadOutputDevice.Headless)
		{
			base.Engine.CancelShutdown();
			ExitApp();
		}
	}

	private void Engine_OnShutdown()
	{
	}

	public static void SummonKeyboard(IText targetText = null, float3? point = null, floatQ? rotation = null)
	{
		if (_keyboard == null)
		{
			return;
		}
		_keyboard.World.RunSynchronously(delegate
		{
			if (!_keyboard.IsShown || _keyboard.Slot.DistanceFromUserHead() > 1f || _keyboard.Slot.AngleFromUserHead() > 70f)
			{
				_keyboard.Slot.PositionInFrontOfUser(float3.Backward, float3.Down * 0.2f);
				if (point.HasValue)
				{
					_keyboard.Slot.GlobalPosition = point.Value;
				}
				if (rotation.HasValue)
				{
					_keyboard.Slot.GlobalRotation = rotation.Value;
				}
			}
			_keyboard.TargetText = targetText;
			_keyboard.IsShown = true;
		});
	}

	public static void HideKeyboard()
	{
		if (_keyboard != null)
		{
			_keyboard.World.RunSynchronously(delegate
			{
				_keyboard.IsShown = false;
			});
		}
	}

	public async Task FillEmptySlot(BodyNode node, AvatarManager manager, CancellationToken cancellationToken)
	{
		await DefaultAvatarHelper.FillEmptySlot(node, manager, userspace: true, null, cancellationToken);
	}

	public static Job<Slot> Paste(SavedGraph data, Slot source, float3 userspacePos, floatQ userspaceRot, float3 userspaceScale, World targetWorld = null)
	{
		Job<Slot> task = new Job<Slot>();
		World world = targetWorld ?? thisobj.Engine.WorldManager.FocusedWorld;
		world.RunSynchronously(delegate
		{
			Slot slot = null;
			if (world.CanSpawnObjects())
			{
				float3 globalPosition = FrooxEngine.WorldManager.TransferPoint(userspacePos, thisobj.World, world);
				floatQ globalRotation = FrooxEngine.WorldManager.TransferRotation(userspaceRot, thisobj.World, world);
				float3 globalScale = FrooxEngine.WorldManager.TransferScale(userspaceScale, thisobj.World, world);
				if (source?.World == world && !source.IsDestroyed)
				{
					source.ActiveSelf = true;
					source.GlobalPosition = globalPosition;
					source.GlobalRotation = globalRotation;
					source.GlobalScale = globalScale;
					task.SetResultAndFinish(source);
				}
				else
				{
					slot = world.AddSlot("Paste");
					slot.LoadObject(data.Root, null);
					slot.GlobalPosition = globalPosition;
					slot.GlobalRotation = globalRotation;
					slot.GlobalScale = globalScale;
					slot.RunOnPaste();
				}
			}
			if (source != null && !source.IsDestroyed)
			{
				source.World.RunSynchronously(source.Destroy);
			}
			task.SetResultAndFinish(slot);
		});
		return task;
	}

	public static bool TryTransferToUserspaceGrabber(Slot root, object linkingKey)
	{
		if (thisobj == null)
		{
			return false;
		}
		if (root == null)
		{
			return false;
		}
		if (root.World == thisobj.World)
		{
			return false;
		}
		Grabber grabber = TryGetUserspaceGrabber(linkingKey);
		if (grabber == null)
		{
			return false;
		}
		TransferToUserspaceGrabber(root, grabber);
		return true;
	}

	private static void TransferToUserspaceGrabber(Slot root, Grabber grabber)
	{
		TransferGrabbableToUserspace(root).OnResultDone += delegate(TransferGrabbable transferGrabbable)
		{
			grabber.Grab(transferGrabbable);
		};
	}

	public static Job<TransferGrabbable> TransferGrabbableToUserspace(Slot root)
	{
		Job<TransferGrabbable> task = new Job<TransferGrabbable>();
		SavedGraph saved = root.SaveObject(DependencyHandling.CollectAssets, saveNonPersistent: true);
		UserspaceWorld.Coroutines.StartCoroutine(TransferGrabbableToUserspace(saved, root, task));
		return task;
	}

	private static IEnumerator<Context> TransferGrabbableToUserspace(SavedGraph saved, Slot root, Job<TransferGrabbable> task)
	{
		float3 point = root.GlobalPosition;
		floatQ rotation = root.GlobalRotation;
		point = FrooxEngine.WorldManager.TransferPoint(point, root.World, UserspaceWorld);
		rotation = FrooxEngine.WorldManager.TransferRotation(rotation, root.World, UserspaceWorld);
		yield return Context.ToBackground();
		DataTreeSaver saver = new DataTreeSaver(thisobj.Engine);
		yield return Context.WaitFor(saver.EnsurePermanentStorage(saved));
		yield return Context.ToWorld();
		Slot slot = thisobj.World.AddSlot("Transfer");
		slot.LoadObject(saved.Root, null, slot, delegate(Type type)
		{
			if (typeof(MeshRenderer).IsAssignableFrom(type))
			{
				return true;
			}
			if (typeof(TextRenderer).IsAssignableFrom(type))
			{
				return true;
			}
			if (typeof(Canvas).IsAssignableFrom(type))
			{
				return true;
			}
			if (typeof(RectTransform).IsAssignableFrom(type))
			{
				return true;
			}
			if (typeof(UIComponent).IsAssignableFrom(type))
			{
				return true;
			}
			if (typeof(IAssetProvider).IsAssignableFrom(type))
			{
				return true;
			}
			return typeof(Light).IsAssignableFrom(type) ? true : false;
		});
		slot.GlobalPosition = point;
		slot.GlobalRotation = rotation;
		TransferGrabbable transferGrabbable = slot.AttachComponent<TransferGrabbable>();
		transferGrabbable.Saved = saved;
		task.SetResultAndFinish(transferGrabbable);
	}

	private void SetupCommandServer()
	{
		if (base.Engine.InputInterface.HeadOutputDevice != HeadOutputDevice.Headless)
		{
			CommandServer = new CommandServer(ProcessCommand);
			base.Engine.OnShutdown += delegate
			{
				CommandServer.Dispose();
			};
		}
	}

	private void ProcessCommand(BaseCommand obj)
	{
		StartTask(async delegate
		{
			await ProcessCommandAsync(obj);
		});
	}

	private async Task ProcessCommandAsync(BaseCommand command)
	{
		await default(ToWorld);
		while (!base.Engine.IsReady)
		{
			await default(NextUpdate);
		}
		if (AutoLoginInProgress == true)
		{
			while (AutoLoginInProgress == true)
			{
				await default(NextUpdate);
			}
			await DelaySeconds(1f);
		}
		if (!(command is OpenURL openUrl))
		{
			if (command is OpenFile)
			{
			}
		}
		else
		{
			await HandleOpenURL(openUrl);
		}
	}

	private async Task HandleOpenURL(OpenURL openUrl)
	{
		UniLog.Log("OpenURL command: " + openUrl.URL);
		Uri uRL = openUrl.URL;
		World targetWorld = base.Engine.WorldManager.FocusedWorld;
		targetWorld.LocalUser.GetPointInFrontOfUser(out var point, out var rotation, float3.Backward);
		await UniversalImporter.Import(uRL.ToString(), targetWorld, point, rotation);
	}

	public static void OpenContextMenu(Slot pointer, ContextMenuOptions options, Func<ContextMenu, Task> setup)
	{
		InteractionHandler commonTool = pointer.FindInteractionHandler();
		Chirality side = pointer.World.InputInterface.PrimaryHand;
		if (commonTool != null)
		{
			side = commonTool.Side.Value;
		}
		UserspaceWorld.Coroutines.StartTask(async delegate
		{
			await default(ToWorld);
			InteractionHandler commonTool2 = UserspaceWorld.LocalUser.Root.GetRegisteredComponent((InteractionHandler t) => t.Side.Value == side);
			ContextMenu menu = await UserspaceWorld.LocalUser.OpenContextMenu(commonTool2, commonTool2.PointReference, options);
			if (menu != null)
			{
				await setup(menu);
			}
		});
	}

	public static void SetFacetAnchorPose(FacetAnchorPoint point, float3 position, floatQ orientation, float scale, bool? overrideState, World source)
	{
		if (!point.IsValid(EnumValidation.IsDefined))
		{
			throw new ArgumentOutOfRangeException("point");
		}
		AnchorData anchorData = GetAnchorData(point);
		anchorData.lastPoseSource = source;
		anchorData.position = position;
		anchorData.orientation = orientation;
		anchorData.scale = scale;
		anchorData.overrideState = overrideState;
	}

	public static void ClearFacetAnchorPose(FacetAnchorPoint point, World source)
	{
		AnchorData data = GetAnchorData(point);
		if (data.lastPoseSource == source)
		{
			data.lastPoseSource = null;
		}
	}

	public static AnchorData GetAnchorData(FacetAnchorPoint point)
	{
		AnchorData data = _anchorData[(int)point];
		if (data == null)
		{
			data = new AnchorData();
			_anchorData[(int)point] = data;
		}
		return data;
	}

	public static void SetUserspaceLaserActive(Chirality node, bool active, bool hasHitTarget)
	{
		ControllerData controllerData = GetControllerData(node);
		controllerData.userspaceLaserActive = active;
		controllerData.userspaceLaserHitTarget = hasHitTarget;
	}

	public static bool IsUserspaceLaserActive(Chirality node)
	{
		return GetControllerData(node).userspaceLaserActive;
	}

	public static bool HasUserspaceLaserHitTarget(Chirality node)
	{
		return GetControllerData(node).userspaceLaserHitTarget;
	}

	public static void SetWorldControllerData(Chirality node, bool toolInUse, float3 offset, float3 forward, float distance)
	{
		ControllerData data = GetControllerData(node);
		offset = MathX.Clamp(MathX.FilterInvalid(offset), -float3.One, float3.One);
		forward = MathX.FilterInvalid(forward.Normalized, float3.Forward);
		if (forward.Magnitude <= 0f)
		{
			forward = float3.Forward;
		}
		distance = MathX.Clamp(MathX.FilterInvalid(distance), 0f, 100000f);
		data.toolInUse = toolInUse;
		data.distance = distance;
		if (!ContextMenuOpened)
		{
			data.pointOffset = offset;
			data.forward = forward;
		}
	}

	public static ControllerData GetControllerData(Chirality side)
	{
		if (_controllerData == null)
		{
			_controllerData = new ControllerData[Enum.GetValues(typeof(Chirality)).Length];
			for (int i = 0; i < _controllerData.Length; i++)
			{
				_controllerData[i] = new ControllerData();
			}
		}
		return _controllerData[(int)side];
	}

	private static Dictionary<object, Grabber> GetWorldGrabbers(World world)
	{
		if (_worldsGrabbers.TryGetValue(world, out Dictionary<object, Grabber> dict))
		{
			return dict;
		}
		dict = new Dictionary<object, Grabber>();
		_worldsGrabbers.Add(world, dict);
		return dict;
	}

	public static void RegisterGrabber(object linkingKey, Grabber grabber)
	{
		Dictionary<object, Grabber> grabbers = GetWorldGrabbers(grabber.World);
		if (grabbers.TryGetValue(linkingKey, out var registeredGrabber))
		{
			UniLog.Error($"Userspace Grabber for device {linkingKey} is already registered, new grabber:\n" + grabber.ParentHierarchyToString() + "\nRegistered grabber: " + registeredGrabber.ParentHierarchyToString());
		}
		else
		{
			grabbers.Add(linkingKey, grabber);
		}
	}

	public static void UnregisterGrabber(object linkingKey, Grabber grabber)
	{
		Dictionary<object, Grabber> grabbers = GetWorldGrabbers(grabber.World);
		grabbers.TryGetValue(linkingKey, out var storedGrabber);
		if (storedGrabber == grabber)
		{
			grabbers.Remove(linkingKey);
			if (grabbers.Count == 0)
			{
				_worldsGrabbers.Remove(grabber.World);
			}
		}
	}

	public static Grabber TryGetWorldGrabber(World world, object linkingkey)
	{
		if (linkingkey == null)
		{
			return null;
		}
		GetWorldGrabbers(world).TryGetValue(linkingkey, out Grabber grabber);
		return grabber;
	}

	public static Grabber TryGetUserspaceGrabber(object linkingKey)
	{
		return TryGetWorldGrabber(UserspaceWorld, linkingKey);
	}

	public static Grabber TryGetGrabberWithItems(Grabber originalGrabber)
	{
		if (originalGrabber == null)
		{
			return null;
		}
		if ((originalGrabber.HolderSlot?.ChildrenCount ?? 0) > 0 || originalGrabber.LocalExternallyHeldItem != null)
		{
			return originalGrabber;
		}
		if (originalGrabber.World == UserspaceWorld)
		{
			Grabber worldGrabber = TryGetWorldGrabber(originalGrabber.Engine.WorldManager.FocusedWorld, originalGrabber.LinkingKey);
			if (worldGrabber != null && ((worldGrabber.HolderSlot?.ChildrenCount ?? 0) > 0 || worldGrabber.LocalExternallyHeldItem != null))
			{
				return worldGrabber;
			}
		}
		return null;
	}

	public static SessionThumbnailData GetThumbnailData(World world)
	{
		if (world == null)
		{
			return null;
		}
		lock (_sessionsThumbnailData)
		{
			_sessionsThumbnailData.TryGetValue(world, out SessionThumbnailData data);
			return data;
		}
	}

	public static void InvalidateThumbnailData(World world)
	{
		lock (_sessionsThumbnailData)
		{
			if (_sessionsThumbnailData.TryGetValue(world, out SessionThumbnailData data))
			{
				data.InvalidateThumbnail();
			}
		}
	}

	private void UpdateSessionThumbnails()
	{
		if ((DateTime.UtcNow - _lastThumbnailUpdate).TotalSeconds < 1.0)
		{
			return;
		}
		_lastThumbnailUpdate = DateTime.UtcNow;
		lock (_sessionsThumbnailData)
		{
			List<World> worlds = Pool.BorrowList<World>();
			HashSet<World> updatedWorlds = Pool.BorrowHashSet<World>();
			WorldManager.GetWorlds(worlds);
			foreach (World w in worlds)
			{
				if (w.Focus == FrooxEngine.World.WorldFocus.Overlay || w.Focus == FrooxEngine.World.WorldFocus.PrivateOverlay)
				{
					continue;
				}
				if (!_sessionsThumbnailData.TryGetValue(w, out SessionThumbnailData data))
				{
					data = new SessionThumbnailData(w);
					_sessionsThumbnailData.Add(w, data);
				}
				data.UploadPublic = w.AnnounceOnWAN || w.AccessLevel == SessionAccessLevel.LAN;
				if (data.ShouldCapture())
				{
					if (w.Render.IsRenderingSupported && w.Focus == FrooxEngine.World.WorldFocus.Focused && w.State == FrooxEngine.World.WorldState.Running)
					{
						StartTask(async delegate
						{
							await CaptureThumbnail(w, data);
						});
					}
					else
					{
						List<FrooxEngine.User> users = w.FindUsers((FrooxEngine.User u) => !u.IsLocalUser && u.ThumbnailUrl != null);
						users.Sort((FrooxEngine.User a, FrooxEngine.User b) => a.ThumbnailAge.CompareTo(b.ThumbnailAge));
						Uri thumbnailURL;
						if (users.Count > 0)
						{
							data.UpdateLocalThumbnail(users[0].ThumbnailUrl, null);
						}
						else if (Uri.TryCreate(w.CorrespondingRecord?.ThumbnailURI, UriKind.Absolute, out thumbnailURL))
						{
							data.UpdateLocalThumbnail(thumbnailURL, null);
						}
					}
				}
				updatedWorlds.Add(w);
			}
			Pool.Return(ref worlds);
			List<KeyValuePair<World, SessionThumbnailData>> removedWorlds = Pool.BorrowList<KeyValuePair<World, SessionThumbnailData>>();
			foreach (KeyValuePair<World, SessionThumbnailData> data2 in _sessionsThumbnailData)
			{
				if (!updatedWorlds.Contains(data2.Key))
				{
					removedWorlds.Add(data2);
				}
			}
			foreach (KeyValuePair<World, SessionThumbnailData> removed in removedWorlds)
			{
				Task.Run((Action)removed.Value.Destroy);
				_sessionsThumbnailData.Remove(removed.Key);
			}
			Pool.Return(ref removedWorlds);
			Pool.Return(ref updatedWorlds);
		}
	}

	private async Task CaptureThumbnail(World world, SessionThumbnailData thumbnailData)
	{
		thumbnailData.UpdateCaptureTime();
		Bitmap2D texture = await world.CaptureThumbnail<ISessionThumbnailSource>(new int2(1024, 512), excludeUsers: false).ConfigureAwait(continueOnCapturedContext: false);
		if (texture == null)
		{
			UniLog.Warning($"Failed to capture thumbnail for: {world}");
		}
		else
		{
			await default(ToBackground);
			Uri uri = await base.Engine.LocalDB.SaveAssetAsync(texture, "webp", 60, preserveColorInAlpha: false).ConfigureAwait(continueOnCapturedContext: false);
			string path = (await base.Engine.LocalDB.TryFetchAssetRecordAsync(uri).ConfigureAwait(continueOnCapturedContext: false)).path;
			thumbnailData.UpdateLocalThumbnail(uri, path);
		}
	}

	public static Task<World> CreateHome(OwnerType homeType, string ownerId, WorldStartSettings? startInfo = null)
	{
		return thisobj.StartTask(async () => await thisobj.CreateHomeTask(homeType, ownerId, startInfo));
	}

	public static async Task OpenHomeOrCreate(string ownerId, string ownerName)
	{
		await thisobj.StartTask(async delegate
		{
			await thisobj.OpenHomeOrCreateTask(ownerId, ownerName);
		});
	}

	public static Task OpenLocalHomeAsync(bool focus = true)
	{
		return thisobj.OpenHomeOrCreateTask(thisobj.Engine.LocalDB.LocalOwnerID, "Local", focus);
	}

	private string GetHomeName(OwnerType ownerType)
	{
		return ownerType switch
		{
			OwnerType.Group => "Group", 
			OwnerType.User => "Cloud", 
			_ => "Local", 
		};
	}

	private string GetHomePath(OwnerType ownerType)
	{
		return "Worlds/HomeTemplates/" + GetHomeName(ownerType);
	}

	/// <summary>
	/// Retrieves a Universe's home template, based on the currently active Universe
	/// </summary>
	/// <param name="ownerType">The type of Home to get the template for</param>
	/// <returns>A URI, for the appropriate home template.</returns>
	/// <remarks>For non-universe templates see <see cref="M:FrooxEngine.Userspace.GetHomeTemplateUri(SkyFrost.Base.OwnerType)" /></remarks>
	public Uri? GetUniverseHomeTemplate(OwnerType ownerType)
	{
		return thisobj.Engine?.Universe.GetRecordAtPath(GetHomePath(ownerType), base.Cloud.Platform);
	}

	/// <summary>
	/// Retrieves a Regular Home Template for this platform. 
	/// </summary>
	/// <param name="ownerType">The type of Home to get the template for</param>
	/// <returns>A URI, for the appropriate home template.</returns>
	/// <remarks>For universe templates see <see cref="M:FrooxEngine.Userspace.GetUniverseHomeTemplate(SkyFrost.Base.OwnerType)" />. Also see <see cref="M:FrooxEngine.Userspace.OpenHomeOrCreateTask(System.String,System.String,System.Boolean)" /> for usage of this. /&gt;</remarks>
	public Uri GetHomeTemplateUri(OwnerType ownerType)
	{
		return base.Cloud.Platform.GetRecordPath(GetHomePath(ownerType));
	}

	private async Task<World> CreateHomeTask(OwnerType ownerType, string ownerId, WorldStartSettings? startInfo = null)
	{
		Uri worldUri = GetHomeTemplateUri(ownerType);
		WorldStartSettings? obj = startInfo ?? new WorldStartSettings();
		obj.SetUri(worldUri);
		obj.Relation = WorldRelation.Independent;
		obj.FetchedWorldName = "Worlds.Home".AsLocaleKey();
		obj.DefaultAccessLevel = GetHomeAccessLevel(ownerType);
		UniLog.Log($"Creating a new {ownerType} Home from the template URL: " + worldUri);
		World world = await OpenWorld(obj).ConfigureAwait(continueOnCapturedContext: false);
		if (world == null)
		{
			UniLog.Log($"Failed to fetch {ownerType} home template from the cloud, falling back to legacy preset.");
			world = ((ownerType != OwnerType.Machine) ? StartUtilityWorld(WorldPresets.GroupHome("An Error has occured, we could not fetch your home template from the Cloud.")) : StartUtilityWorld(WorldPresets.LocalHome()));
		}
		world.Parent = null;
		world.AssignNewRecord(ownerId, "R-Home");
		string homeName = ownerId;
		switch (ownerType)
		{
		case OwnerType.Machine:
			homeName = "Local";
			break;
		case OwnerType.Group:
		{
			CloudResult<Group> result = await base.Cloud.Groups.GetGroup(ownerId);
			if (result.IsOK)
			{
				homeName = result.Entity.Name + " Group Home";
			}
			break;
		}
		case OwnerType.User:
			homeName = base.Cloud.CurrentUser.Username + " Home";
			break;
		}
		world.CorrespondingRecord.Name = homeName;
		world.Name = world.CorrespondingRecord.Name;
		if (startInfo != null)
		{
			startInfo.Record = world.CorrespondingRecord;
		}
		return world;
	}

	private SessionAccessLevel GetHomeAccessLevel(OwnerType type)
	{
		if (type == OwnerType.Machine)
		{
			return SessionAccessLevel.Private;
		}
		if (!AnnounceHomeOnLAN)
		{
			return SessionAccessLevel.Private;
		}
		return SessionAccessLevel.LAN;
	}

	private async Task<HomeWorldResult> LoadHome(OwnerType ownerType, string ownerId)
	{
		Uri uri = ((ownerType != OwnerType.User) ? thisobj.Cloud.Platform.GetRecordUri(ownerId, "R-Home") : (thisobj.Cloud.Profile.GetCurrentFavorite(FavoriteEntity.Home) ?? base.Cloud.Platform.GetRecordUri(ownerId, "R-Home")));
		UniLog.Log($"Fetching home world record: {uri}");
		CloudResult<FrooxEngine.Store.Record> result = await thisobj.Engine.RecordManager.FetchRecord(uri).ConfigureAwait(continueOnCapturedContext: false);
		UniLog.Log("Home world record result: " + result);
		await default(ToWorld);
		if (result.IsOK)
		{
			UniLog.Log("Loading home world from asset: " + result.Entity.AssetURI);
			return new HomeWorldResult(await OpenWorld(new WorldStartSettings(new Uri(result.Entity.AssetURI))
			{
				Record = result.Entity,
				DefaultAccessLevel = GetHomeAccessLevel(ownerType)
			}));
		}
		return new HomeWorldResult(result.State, result.Content);
	}

	private async Task OpenHomeOrCreateTask(string ownerId, string ownerName, bool focus = false)
	{
		OwnerType ownerType = IdUtil.GetOwnerType(ownerId);
		HomeWorldResult? worldResult;
		if (thisobj.Engine.InUniverse && FrooxEngine.Engine.Config.UseUniverseHomes)
		{
			worldResult = await LoadUniverseHome(ownerType);
			if (!worldResult.HasValue || worldResult?.world == null)
			{
				worldResult = await LoadHome(ownerType, ownerId);
			}
		}
		else
		{
			worldResult = await LoadHome(ownerType, ownerId);
		}
		if (worldResult?.world != null)
		{
			UniLog.Log($"Found appropriate {ownerType} home, loading and running it.");
			if (focus)
			{
				base.Engine.WorldManager.FocusWorld(worldResult?.world);
			}
		}
		else if (!worldResult.HasValue || worldResult.GetValueOrDefault().state != HttpStatusCode.NotFound)
		{
			UniLog.Log($"Loading a {ownerType} Home World, failed. State: {worldResult?.state} with error: {worldResult?.error}");
			UniLog.Log($"Halting creation of default {ownerType} Home World");
		}
		else
		{
			UniLog.Log($"{ownerType} Home not found, creating a new one based on the appropriate template.");
			await CreateHomeTask(ownerType, ownerId);
		}
	}

	private async Task<HomeWorldResult> LoadUniverseHome(OwnerType homeType)
	{
		Uri uri = GetUniverseHomeTemplate(homeType);
		if (uri == null)
		{
			return new HomeWorldResult(HttpStatusCode.NotFound, "No Home path");
		}
		World world = await OpenWorld(new WorldStartSettings(uri)
		{
			DefaultAccessLevel = GetHomeAccessLevel(homeType)
		});
		if (world != null)
		{
			world.CorrespondingRecord = null;
		}
		return new HomeWorldResult(world);
	}

	public static World GetParent(World world)
	{
		if (world.CorrespondingRecord?.RecordId == "R-Home")
		{
			return IdUtil.GetOwnerType(world.CorrespondingRecord.OwnerId) switch
			{
				OwnerType.Group => CloudHome ?? LocalHome, 
				OwnerType.User => LocalHome, 
				_ => null, 
			};
		}
		World parent = world.Parent;
		if (parent == null || parent.IsDestroyed || parent == world)
		{
			parent = CloudHome ?? LocalHome;
		}
		return parent;
	}

	public static World GetExistingWorld(WorldStartSettings startInfo)
	{
		if (startInfo.SessionID != null)
		{
			World world = thisobj.WorldManager.Worlds.FirstOrDefault((World w) => w.SessionId?.Equals(startInfo.SessionID, StringComparison.InvariantCultureIgnoreCase) ?? false);
			if (world != null)
			{
				return world;
			}
		}
		if (startInfo.URIs == null)
		{
			return null;
		}
		return thisobj.WorldManager.Worlds.FirstOrDefault(delegate(World w)
		{
			if (startInfo.URIs.Any((Uri u) => w.SourceURLs.Contains(u)))
			{
				return true;
			}
			return startInfo.URIs.Any((Uri u) => w.SessionURLs.Contains(u)) ? true : false;
		});
	}

	public static async Task<World> FocusWorld(WorldStartSettings startInfo)
	{
		await ResolveUris(startInfo);
		if (startInfo.URIs == null)
		{
			return null;
		}
		World existingWorld = GetExistingWorld(startInfo);
		if (existingWorld != null)
		{
			await FocusWhenReady(existingWorld);
		}
		return existingWorld;
	}

	public static async Task WaitForReady(World world)
	{
		while (world.State == FrooxEngine.World.WorldState.Initializing)
		{
			await default(NextUpdate);
		}
		if (world.State != FrooxEngine.World.WorldState.Running)
		{
			return;
		}
		await world.Coroutines.StartTask(async delegate
		{
			await new Updates(1);
			List<IWorldLoadStatus> loaders = world.RootSlot.GetComponentsInChildren<IWorldLoadStatus>();
			if (loaders.Count != 0)
			{
				while (loaders.All((IWorldLoadStatus l) => !l.IsLoaded))
				{
					await default(NextUpdate);
				}
			}
		});
	}

	public static async Task FocusWhenReady(World world)
	{
		if (world.Focus == FrooxEngine.World.WorldFocus.Focused)
		{
			return;
		}
		await WaitForReady(world);
		await new Updates(10);
		if (world == CloudHome)
		{
			if (thisobj.Engine.WorldManager.FocusedWorld != LocalHome && !_cloudHomeFirstOpened)
			{
				_cloudHomeFirstOpened = true;
				return;
			}
			_cloudHomeFirstOpened = true;
		}
		if (world.IsDestroyed)
		{
			return;
		}
		World currentFocused = thisobj.WorldManager.FocusedWorld;
		if (currentFocused != world)
		{
			thisobj.WorldManager.FocusWorld(world);
			while (currentFocused == thisobj.WorldManager.FocusedWorld)
			{
				await default(NextUpdate);
			}
		}
	}

	public static async Task<World> OpenWorld(WorldStartSettings startInfo)
	{
		return await thisobj.StartTask(async () => await thisobj.OpenWorldInternal(startInfo));
	}

	private async Task<World> OpenWorldInternal(WorldStartSettings startInfo)
	{
		World currentWorld = thisobj.WorldManager.FocusedWorld;
		if (startInfo.URIs != null)
		{
			UniLog.Log("Opening world. URI's: " + string.Join(", ", startInfo.URIs));
		}
		else if (startInfo.Link != null)
		{
			UniLog.Log($"Opening world from link: {startInfo.Link}, Actual URI's: {string.Join(", ", string.Join(", ", startInfo.Link.ActiveSessionURLs ?? new List<Uri>()))}");
		}
		await ResolveUris(startInfo);
		if (startInfo.URIs == null && startInfo.InitWorld == null)
		{
			return null;
		}
		World world = null;
		if (startInfo.GetExisting)
		{
			world = GetExistingWorld(startInfo);
		}
		bool loadedWorld = false;
		if (world == null)
		{
			Task<WorldLoadProgress> indicatorTask = null;
			if (startInfo.HostUserId != null && BanManager.IsMutuallyBlocked(new UserFingerprint(null, startInfo.HostUserId, null, null)))
			{
				await WorldLoadProgress.ShowMessage(startInfo.FetchedWorldName, "World.Error.HostBlocked".AsLocaleKey("<color=#f00>{0}</color>"), "World.Error.HostBlockedDetail".AsLocaleKey("<color=#f00>{0}</color>"), ProgressStage.Failed);
				return null;
			}
			if (startInfo.CreateLoadIndicator && startInfo.LoadingIndicator == null)
			{
				indicatorTask = WorldLoadProgress.CreateIndicator(startInfo.FetchedWorldName);
			}
			if (startInfo.URIs == null)
			{
				world = StartSession(startInfo.InitWorld, startInfo.ForcePort, startInfo.ForceSessionId, null, null, startInfo.AutoFocus, startInfo.UnsafeMode);
			}
			else if (startInfo.URIs.Any((Uri u) => thisobj.Engine.NetworkManager.IsSupportedSessionScheme(u.Scheme)))
			{
				world = JoinSession(startInfo.URIs, null, focusWhenReady: false);
			}
			else
			{
				if (startInfo.InitWorld == null && !startInfo.URIs.Any((Uri u) => thisobj.Cloud.Assets.IsValidDBUri(u) || thisobj.Engine.PlatformProfile.IsValidRecordUri(u) || u.Scheme == "local"))
				{
					UniLog.Warning("Cannot open world, none of the URL's are supported!");
					if (indicatorTask != null)
					{
						(await indicatorTask).DestroyIndicator();
					}
					return null;
				}
				world = await LoadWorld(startInfo);
				loadedWorld = true;
			}
			if (indicatorTask != null)
			{
				startInfo.LoadingIndicator = await indicatorTask;
			}
		}
		if (startInfo.LoadingIndicator != null)
		{
			if (world == null)
			{
				startInfo.LoadingIndicator.DestroyIndicator();
			}
			else
			{
				startInfo.LoadingIndicator.TargetWorld = world;
			}
		}
		if (world != null)
		{
			if (startInfo.Record != null)
			{
				world.CorrespondingRecord = startInfo.Record;
				world.SourceURL = startInfo.Record.GetUrl(base.Cloud.Platform);
				world.Name = startInfo.Record.Name;
				world.Description = startInfo.Record.Description;
				world.Tags = startInfo.Record.Tags;
			}
			if (startInfo.Link != null)
			{
				world.SourceLink = startInfo.Link;
				world.SourceURLs = startInfo.URIs;
				if (startInfo.Relation == WorldRelation.Nest)
				{
					world.Parent = startInfo.Link.World;
				}
				else if (startInfo.Relation == WorldRelation.Replace)
				{
					world.Parent = startInfo.Link.World.Parent;
				}
				if (world.IsAuthority)
				{
					AssignSessionURLs(startInfo.Link, world);
				}
			}
			if (loadedWorld)
			{
				if (startInfo.UniverseID != null)
				{
					world.UniverseId = startInfo.UniverseID;
				}
				if (startInfo.DefaultAccessLevel.HasValue)
				{
					world.AccessLevel = startInfo.DefaultAccessLevel.Value;
				}
				else if (ForceLANOnly)
				{
					world.AccessLevel = SessionAccessLevel.LAN;
				}
				else if (base.Cloud.Status.OnlineStatus.DefaultPrivate())
				{
					world.AccessLevel = SessionAccessLevel.Private;
				}
				else if (world.CorrespondingRecord?.IsPublic ?? false)
				{
					world.AccessLevel = SessionAccessLevel.Anyone;
				}
				if (startInfo.HideFromListing.HasValue)
				{
					world.HideFromListing = startInfo.HideFromListing.Value;
				}
				if (startInfo.MobileFriendly.HasValue)
				{
					world.MobileFriendly = startInfo.MobileFriendly.Value;
				}
			}
			if (startInfo.Relation == WorldRelation.Replace)
			{
				World sourceWorld = startInfo.Link?.World ?? currentWorld;
				StartTask(async delegate
				{
					await ReplaceWorld(sourceWorld, world);
				});
			}
			if (startInfo.AutoFocus)
			{
				await FocusWhenReady(world);
			}
		}
		return world;
	}

	public static async Task ResolveUris(WorldStartSettings startInfo)
	{
		if (startInfo.URIs == null)
		{
			if (startInfo.Link == null)
			{
				return;
			}
			if (startInfo.Link.HasAnyActiveSessionURLs)
			{
				startInfo.URIs = startInfo.Link.ActiveSessionURLs;
			}
			else
			{
				List<Uri> list = new List<Uri>();
				list.Add(startInfo.Link.URL);
				startInfo.URIs = list;
			}
		}
		Uri sessionUri = startInfo.URIs.FirstOrDefault((Uri u) => u.Scheme == thisobj.Cloud.Platform.SessionScheme);
		string sessionId = null;
		if (sessionUri != null)
		{
			sessionId = (startInfo.SessionID = ((sessionUri.Segments.Length < 2) ? sessionUri.Host : sessionUri.Segments[1]));
		}
		if (sessionId != null)
		{
			SessionInfo sessionInfo = thisobj.Engine.Cloud.Sessions.TryGetInfo(sessionId);
			if (sessionInfo != null)
			{
				startInfo.URIs = sessionInfo.GetSessionURLs();
				startInfo.HostUserId = sessionInfo.HostUserId;
				return;
			}
			CloudResult<List<string>> result = await thisobj.Engine.Cloud.Sessions.GetSessionURLs(sessionId).ConfigureAwait(continueOnCapturedContext: false);
			if (result.IsOK)
			{
				startInfo.URIs = (from u in result.Entity.Select(delegate(string u)
					{
						Uri.TryCreate(u, UriKind.Absolute, out Uri result2);
						return result2;
					})
					where u != null
					select u).ToList();
			}
			else
			{
				startInfo.URIs = null;
			}
			return;
		}
		Uri userUri = startInfo.URIs.FirstOrDefault((Uri u) => u.Scheme == thisobj.Cloud.Platform.UserSessionScheme);
		if (!(userUri != null))
		{
			return;
		}
		if (userUri.Segments.Length != 2)
		{
			startInfo.URIs = null;
			return;
		}
		string name = userUri.Segments[1];
		if (!name.StartsWith("U-"))
		{
			CloudResult<List<SkyFrost.Base.User>> users = await thisobj.Cloud.Users.GetUsers(name);
			if (users.Entity == null || users.Entity.Count == 0)
			{
				startInfo.URIs = null;
				return;
			}
			_ = users.Entity[0].Id;
		}
		throw new NotImplementedException();
	}

	public static Task SaveWorldAuto(World world, SaveType saveType, bool exitOnSave)
	{
		return thisobj.StartTask(async delegate
		{
			await thisobj.SaveWorldAutoTask(world, saveType, exitOnSave);
		});
	}

	private async Task SaveWorldAutoTask(World world, SaveType saveType, bool exitOnSave)
	{
		if (saveType == SaveType.Overwrite && !CanSave(world))
		{
			return;
		}
		if (!world.IsAllowedToSaveWorld())
		{
			UniLog.Warning($"Not allowed to save world: {world}");
			return;
		}
		Uri thumbUri = await CreateWorldThumbnail(world, excludeUsers: true);
		if (saveType != SaveType.Overwrite)
		{
			Slot slot = base.World.AddSlot("Saver");
			slot.PositionInFrontOfUser();
			WorldOrbSaver worldOrbSaver = slot.AttachComponent<WorldOrbSaver>();
			worldOrbSaver.TargetWorld = world;
			worldOrbSaver.ThumbnailUri = thumbUri;
			worldOrbSaver.SaveType = saveType;
			worldOrbSaver.CloseOnSave = exitOnSave;
			worldOrbSaver.Orb.Target.ThumbnailTexURL = thumbUri;
		}
		else
		{
			if (thumbUri != null)
			{
				world.CorrespondingRecord.ThumbnailURI = thumbUri.ToString();
			}
			await SaveWorld(world);
			if (exitOnSave)
			{
				ExitWorld(world);
			}
		}
	}

	public static async Task<FrooxEngine.Store.Record> SaveWorld(World world, FrooxEngine.Store.Record record = null, RecordOwnerTransferer transferer = null)
	{
		if (record == null)
		{
			record = world.CorrespondingRecord;
		}
		return await thisobj.StartTask(async () => await thisobj.SaveWorldTask(world, record, transferer));
	}

	public static string GetNewWorldName()
	{
		return (thisobj.Engine.Cloud.CurrentUser?.Username ?? Environment.MachineName) + " World " + worldIndex++;
	}

	public static World StartLocal(WorldAction init = null, DataTreeDictionary load = null, bool focusWhenReady = true)
	{
		World world = thisobj.Engine.WorldManager.StartLocal(init, load);
		if (world.Name == null)
		{
			world.Name = GetNewWorldName();
		}
		world.Parent = thisobj.Engine.WorldManager.FocusedWorld;
		if (focusWhenReady)
		{
			thisobj.StartTask(async delegate
			{
				await FocusWhenReady(world);
			});
		}
		return world;
	}

	public static World StartSession(WorldAction init = null, ushort port = 0, string forceSessionId = null, DataTreeDictionary load = null, FrooxEngine.Store.Record record = null, bool focusWhenReady = true, bool unsafeMode = false)
	{
		World world = thisobj.Engine.WorldManager.StartSession(init, port, forceSessionId, load, record, unsafeMode);
		if (world.Name == null)
		{
			world.Name = GetNewWorldName();
		}
		world.Parent = thisobj.Engine.WorldManager.FocusedWorld;
		if (focusWhenReady)
		{
			thisobj.StartTask(async delegate
			{
				await FocusWhenReady(world);
			});
		}
		return world;
	}

	public static void JoinSession(IPAddress ip, ushort port, IWorldLink sourceLink = null, bool focusWhenReady = true)
	{
		JoinSession(new Uri("lnl://" + ip.ToString() + ":" + port), sourceLink, focusWhenReady);
	}

	public static World JoinSession(Uri address, IWorldLink sourceLink = null, bool focusWhenReady = true)
	{
		return JoinSession(new List<Uri> { address }, sourceLink, focusWhenReady);
	}

	public static World JoinSession(IEnumerable<Uri> addresses, IWorldLink sourceLink = null, bool focusWhenReady = true)
	{
		World localSession = thisobj.Engine.WorldManager.Worlds.FirstOrDefault((World w) => w?.SessionURLs.Any((Uri u) => addresses.Contains(u)) ?? false);
		if (localSession != null)
		{
			thisobj.Engine.WorldManager.FocusWorld(localSession);
			return localSession;
		}
		World world = thisobj.Engine.WorldManager.JoinSession(addresses);
		world.Parent = thisobj.Engine.WorldManager.FocusedWorld;
		world.DisconnectRequestedHook = delegate
		{
			LeaveSession(world);
		};
		world.HostConnectionClosedHook = delegate
		{
			LeaveSession(world);
		};
		world.SourceLink = sourceLink;
		world.SourceURLs = addresses;
		if (focusWhenReady)
		{
			thisobj.StartTask(async delegate
			{
				await FocusWhenReady(world);
			});
		}
		return world;
	}

	public static Task EndSession(World world)
	{
		if (!world.IsAuthority)
		{
			throw new Exception("Cannot end session that's not hosted locally, use LeaveSession instead");
		}
		return thisobj.World.Coroutines.StartTask(async delegate
		{
			await thisobj.EndWorld(world);
		});
	}

	public static Task LeaveSession(World world)
	{
		if (world.IsAuthority)
		{
			throw new Exception("Cannot leave locally hosted world, use EndSession instead");
		}
		return thisobj.World.Coroutines.StartTask(async delegate
		{
			await thisobj.LeaveWorld(world);
		});
	}

	private async Task EndWorld(World world)
	{
		if (world.SourceLink != null && !world.SourceLink.IsRemoved)
		{
			world.SourceLink.World?.RunSynchronously(delegate
			{
				if (!world.SourceLink.IsDestroyed)
				{
					world.SourceLink.ActiveSessionURLs = null;
				}
			});
		}
		if (world.UserCount > 1)
		{
			world.RunSynchronously(delegate
			{
				foreach (FrooxEngine.User current in world.AllUsers)
				{
					if (!current.IsLocalUser)
					{
						current.Disconnect();
					}
				}
			});
			DateTime start = DateTime.UtcNow;
			while (world.UserCount > 1 && (DateTime.UtcNow - start).TotalSeconds < 5.0)
			{
				await default(NextUpdate);
			}
		}
		await LeaveWorld(world);
	}

	private async Task LeaveWorld(World world)
	{
		if (world == WorldManager.FocusedWorld)
		{
			World focus = GetParent(world);
			if (focus != null)
			{
				WorldManager.FocusWorld(focus);
			}
			while (WorldManager.FocusedWorld == world)
			{
				await default(NextUpdate);
			}
		}
		world.Destroy();
		await default(NextUpdate);
	}

	private async Task ReplaceWorld(World sourceWorld, World targetWorld)
	{
		while (base.Engine.WorldManager.FocusedWorld == sourceWorld)
		{
			await default(NextUpdate);
		}
		await EndSession(sourceWorld);
	}

	private async Task<World> LoadWorld(WorldStartSettings startInfo)
	{
		await default(ToBackground);
		Uri linkUrl = startInfo.URIs.First();
		Uri assetUrl;
		if (linkUrl.Scheme == base.Cloud.Platform.RecordScheme)
		{
			CloudResult<FrooxEngine.Store.Record> result;
			if (base.Cloud.Records.ExtractRecordID(linkUrl, out var ownerId, out var recordId))
			{
				result = await base.Engine.RecordManager.FetchRecord(ownerId, recordId);
			}
			else
			{
				if (!base.Cloud.Records.ExtractRecordPath(linkUrl, out ownerId, out var _))
				{
					return null;
				}
				result = await base.Engine.RecordManager.FetchRecord(linkUrl);
			}
			if (result.IsError)
			{
				if (result.State != HttpStatusCode.NotFound)
				{
					return null;
				}
				if (startInfo.Link?.CreateIfNotExists != null)
				{
					World world = startInfo.Link.CreateIfNotExists(startInfo.Link);
					world.AssignNewRecord(ownerId, recordId);
					return world;
				}
				if (recordId == "R-Home")
				{
					return await CreateHome(IdUtil.GetOwnerType(ownerId), ownerId, startInfo);
				}
			}
			FrooxEngine.Store.Record record = result.Entity;
			if (record.RecordType != "world")
			{
				UniLog.Log($"We expected a {"world"} for loading {linkUrl} but got a {record.RecordType} instead.");
				if (!TryGetWorldUrlFromWorldOrb(record, out Uri worldOrbRecordUrl) || worldOrbRecordUrl == null)
				{
					UniLog.Warning($"Unable to find a valid World to load from: {linkUrl}");
					return null;
				}
				result = await base.Engine.RecordManager.FetchRecord(worldOrbRecordUrl);
				if (result.IsError)
				{
					UniLog.Log("Invalid Record URL found in world orb.");
					return null;
				}
				record = result.Entity;
				if (record.RecordType != "world")
				{
					UniLog.Log($"World Orb contained: {record.RecordType} instead of {"world"}, cannot continue.");
					return null;
				}
				UniLog.Log($"Extracted actual world URL from World ORB. Old: {linkUrl}, NEW: {worldOrbRecordUrl}");
			}
			startInfo.Record = record;
			assetUrl = new Uri(startInfo.Record.AssetURI);
		}
		else
		{
			assetUrl = linkUrl;
		}
		UniLog.Log("Requesting gather for: " + assetUrl);
		string assetPath = await base.Engine.AssetManager.GatherAssetFile(assetUrl, 100f);
		if (!File.Exists(assetPath))
		{
			UniLog.Log($"Failed the retrieve file for {assetUrl}, returned path: {assetPath ?? "null"}");
			return null;
		}
		UniLog.Log("Got asset at path: " + assetPath + ", loading world");
		DataTreeDictionary node = DataTreeConverter.Load(assetPath, assetUrl);
		await default(ToWorld);
		return StartSession(startInfo.InitWorld, startInfo.ForcePort, startInfo.ForceSessionId, node, startInfo.Record, focusWhenReady: false, startInfo.UnsafeMode);
	}

	/// <summary>
	/// Given a Record, try to figure out if the record is a world orb. If it is return the URI from "inside" the world orb.
	/// </summary>
	/// <param name="record">Input record</param>
	/// <param name="worldOrbRecordUrl">A valid Record URL if successful</param>
	/// <returns>true for success, false otherwise</returns>
	/// <remarks>World Orb records, usually get two tags 'world_orb' and 'world_uri:resrec//etc' see <see cref="M:SkyFrost.Base.RecordTags.GetCorrespondingWorldUrl(System.Collections.Generic.HashSet{System.String})" /> for more info.</remarks>
	private bool TryGetWorldUrlFromWorldOrb(FrooxEngine.Store.Record record, out Uri? worldOrbRecordUrl)
	{
		worldOrbRecordUrl = null;
		if (!record.Tags.Contains(RecordTags.WorldOrb))
		{
			return false;
		}
		if (!Uri.TryCreate(RecordTags.GetCorrespondingWorldUrl(record.Tags), UriKind.Absolute, out Uri uri) || uri.Scheme != base.Cloud.Platform.RecordScheme)
		{
			UniLog.Log("World Orb does not contain a valid URL");
			return false;
		}
		worldOrbRecordUrl = uri;
		return true;
	}

	private static void AssignSessionURLs(IWorldLink link, World openedWorld)
	{
		((Component)link).StartTask(async delegate
		{
			await AssignSessionURLsAsync(link, openedWorld);
		});
	}

	private static async Task AssignSessionURLsAsync(IWorldLink link, World world)
	{
		while (!world.SessionURLs.Any())
		{
			await default(NextUpdate);
			if (world.IsDestroyed)
			{
				return;
			}
		}
		link.ActiveSessionURLs = world.SessionURLs;
	}

	private async Task<FrooxEngine.Store.Record> SaveWorldTask(World world, FrooxEngine.Store.Record record, RecordOwnerTransferer transferer)
	{
		try
		{
			Interlocked.Increment(ref _activeSaveTaskCount);
			UniLog.Log("Saving world: " + world.Name + ", currently being saved: " + _activeSaveTaskCount);
			return await SaveWorldTaskIntern(world, record, transferer);
		}
		catch (Exception value)
		{
			UniLog.Error($"Exception in saving world {world}:\n{value}");
			throw;
		}
		finally
		{
			Interlocked.Decrement(ref _activeSaveTaskCount);
			UniLog.Log("Finished save world: " + world.Name + ", currently being saved: " + _activeSaveTaskCount);
		}
	}

	private async Task<FrooxEngine.Store.Record> SaveWorldTaskIntern(World world, FrooxEngine.Store.Record record, RecordOwnerTransferer transferer)
	{
		if (record == null)
		{
			throw new Exception("World record is null, cannot perform save");
		}
		if (IdUtil.GetOwnerType(record.OwnerId) == OwnerType.INVALID)
		{
			throw new Exception("Invalid record ownerID: " + record.OwnerId);
		}
		TaskCompletionSource<SavedGraph> completionSource = new TaskCompletionSource<SavedGraph>();
		string _name = null;
		string _description = null;
		HashSet<string> _tags = null;
		world.RunSynchronously(delegate
		{
			try
			{
				int value = MaterialOptimizer.DeduplicateMaterials(world);
				int value2 = WorldOptimizer.DeduplicateStaticProviders(world);
				int value3 = WorldOptimizer.CleanupAssets(world);
				UniLog.Log($"World Optimized! Deduplicated Materials: {value}, Deduplicated Static Providers: {value2}, Cleaned Up Assets: {value3}");
				completionSource.SetResult(world.SaveWorld());
				_name = world.Name;
				_description = world.Description;
				_tags = new HashSet<string>();
				foreach (string current in world.Tags)
				{
					if (!string.IsNullOrWhiteSpace(current))
					{
						_tags.Add(current);
					}
				}
			}
			catch (Exception exception)
			{
				completionSource.SetException(exception);
			}
		});
		SavedGraph graph = await completionSource.Task.ConfigureAwait(continueOnCapturedContext: false);
		await default(ToBackground);
		try
		{
			if (transferer == null)
			{
				transferer = new RecordOwnerTransferer(base.Engine, record.OwnerId);
			}
			await transferer.EnsureOwnerId(graph).ConfigureAwait(continueOnCapturedContext: false);
			Uri worldUri = await new DataTreeSaver(base.Engine).SaveLocally(graph, world.SourceLink?.URL).ConfigureAwait(continueOnCapturedContext: false);
			if (!base.Engine.Cloud.HasPotentialAccess(record.OwnerId))
			{
				return null;
			}
			if (!record.IsPublic)
			{
				record.IsPublic = world.Parent?.CorrespondingRecord?.IsPublic == true;
			}
			record.Name = _name;
			record.Description = _description;
			record.Tags = _tags;
			record.AssetURI = worldUri.ToString();
			record.RecordType = "world";
			Uri recordUri = base.Cloud.Platform.GetRecordUri(record.OwnerId, record.RecordId);
			if (world.CorrespondingRecord == record)
			{
				world.SourceURL = recordUri;
			}
			string worldInfo = $"Name: {record.Name}. RecordId: {record.OwnerId}:{record.RecordId}. Local: {record.Version.LocalVersion}, Global: {record.Version.GlobalVersion}";
			if (!(await base.Engine.RecordManager.SaveRecord(record, graph)).saved)
			{
				UniLog.Error("Error saving the record! " + worldInfo);
			}
			else
			{
				UniLog.Log("World Saved! " + worldInfo);
			}
			return record;
		}
		catch (Exception ex)
		{
			string file = base.Engine.LocalDB.GetTempFilePath(".lz4bson");
			DataTreeConverter.Save(graph.Root, file, DataTreeConverter.Compression.LZ4);
			UniLog.Error("Exception in the save process for " + _name + "!\nDumping the raw save data to: " + file + "\n" + ex);
			return null;
		}
	}

	public static async Task RunSaveTask(Task task)
	{
		Interlocked.Increment(ref _activeSaveTaskCount);
		try
		{
			await task;
		}
		finally
		{
			Interlocked.Decrement(ref _activeSaveTaskCount);
		}
	}

	public static async Task<Uri> CreateWorldThumbnail(World world, bool excludeUsers)
	{
		Bitmap2D texture = await world.CaptureThumbnail<IWorldThumbnailSource>(new int2(2048, 1024), excludeUsers: true);
		if (texture == null)
		{
			return null;
		}
		await default(ToBackground);
		return await thisobj.Engine.LocalDB.SaveAssetAsync(texture, "webp", 75, preserveColorInAlpha: false).ConfigureAwait(continueOnCapturedContext: false);
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static Userspace __New()
	{
		return new Userspace();
	}
}
