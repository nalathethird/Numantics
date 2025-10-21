// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.UserRoot
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using Renderite.Shared;

[Category(new string[] { "Users" })]
public class UserRoot : Component
{
	public enum UserNode
	{
		None,
		Root,
		GroundProjectedHead,
		Head,
		Hips,
		Feet,
		View
	}

	public const float MAX_DISTANCE = 100000f;

	private ILinkRef _lastLink;

	private User _lastUser;

	private Slot _localNameplate;

	private AvatarManager _avatarManager;

	private int _avatarManagerVersion;

	private Slot _rootSpaceHint;

	public readonly SyncRef<IRenderSettingsSource> RenderSettings;

	public readonly SyncRef<ScreenController> ScreenController;

	public readonly SyncRef<Slot> OverrideRoot;

	public readonly SyncRef<Slot> OverrideView;

	public readonly SyncRef<AudioListener> PrimaryListener;

	private BoundingBox _cachedPlayerBounds;

	private int _cachedPlayerBoundsFrame = -1;

	private bool _settingRegistered;

	private DesktopRenderSettings _renderSettings;

	private Slot _cachedHeadSlot;

	private Slot _cachedLeftHandSlot;

	private Slot _cachedRightHandSlot;

	private Slot _cachedLeftControllerSlot;

	private Slot _cachedRightControllerSlot;

	private Slot _cachedLeftFootSlot;

	private Slot _cachedRightFootSlot;

	private TrackedDevicePositioner _hipsPositioner;

	private TrackedDevicePositioner _leftFootPositioner;

	private TrackedDevicePositioner _rightFootPositioner;

	private HashSet<Component> _registeredComponents = new HashSet<Component>();

	private Dictionary<Type, IList> _perTypeComponents = new Dictionary<Type, IList>();

	private CancellationTokenSource _scaleAnimCancel;

	public static float3 DEFAULT_NECK_OFFSET => new float3(0f, -0.12f, -0.1f);

	public Slot LocalNameplate => _localNameplate;

	public User ActiveUser
	{
		get
		{
			if (base.DirectLink != _lastLink)
			{
				_lastLink = base.DirectLink;
				_lastUser = _lastLink.FindNearestParent<User>();
			}
			return _lastUser;
		}
	}

	public Slot RootSpaceHint
	{
		get
		{
			if (_rootSpaceHint != null && (_rootSpaceHint.IsRemoved || _rootSpaceHint.IsChildOf(base.Slot, includeSelf: true)))
			{
				_rootSpaceHint = null;
			}
			return _rootSpaceHint;
		}
		set
		{
			_rootSpaceHint = value;
		}
	}

	public bool IsPrimaryListenerActive => PrimaryListener.Target?.EnabledAndActive ?? false;

	public BoundingBox PlayerBounds
	{
		get
		{
			if (_cachedPlayerBoundsFrame != base.Time.LocalUpdateIndex)
			{
				BoundingBox bounds = BoundingBox.Empty();
				float radius = base.Slot.LocalScaleToGlobal(0.1f);
				bounds.Encapsulate(FeetPosition, radius);
				bounds.Encapsulate(HeadPosition, radius);
				bounds.Encapsulate(LeftControllerPosition, radius);
				bounds.Encapsulate(RightControllerPosition, radius);
				_cachedPlayerBounds = bounds;
				_cachedPlayerBoundsFrame = base.Time.LocalUpdateIndex;
			}
			return _cachedPlayerBounds;
		}
	}

	public float DesktopFOV
	{
		get
		{
			float fov = _renderSettings?.FieldOfView.Value ?? 75f;
			Sync<bool> sync = _renderSettings?.SprintFieldOfViewZoom;
			if (sync == null || (bool)sync)
			{
				ForeachRegisteredComponent(delegate(IFieldOfViewModifier m)
				{
					fov = m.ProcessFOV(fov);
				});
			}
			fov = MathX.FilterInvalid(fov);
			fov = MathX.Clamp(fov, 1f, 179f);
			return fov;
		}
	}

	public bool ReceivedFirstPositionalData
	{
		get
		{
			if (HeadSlot == null || (!(HeadSlot.LocalPosition != float3.Zero) && !(HeadSlot.LocalRotation != floatQ.Identity)))
			{
				if (ActiveUser != null)
				{
					return ActiveUser.HeadDevice.IsCamera();
				}
				return false;
			}
			return true;
		}
	}

	public float GlobalScale
	{
		get
		{
			return base.Slot.GlobalScale.x;
		}
		set
		{
			base.Slot.GlobalScale = float3.One * value;
		}
	}

	public float LocalScale
	{
		get
		{
			return base.Slot.LocalScale.x;
		}
		set
		{
			base.Slot.LocalScale = float3.One * value;
		}
	}

	public Slot HeadSlot
	{
		get
		{
			if ((_cachedHeadSlot == null || _cachedHeadSlot.IsDestroyed || _cachedHeadSlot.IsDisposed) && !IsRemoved)
			{
				_cachedHeadSlot = base.Slot.FindChild("Head") ?? base.Slot.FindChild("Body Nodes")?.FindChild("Head");
			}
			return _cachedHeadSlot;
		}
	}

	public float3 LeftHandPosition => LeftHandSlot?.GlobalPosition ?? float3.Zero;

	public float3 RightHandPosition => RightHandSlot?.GlobalPosition ?? float3.Zero;

	public floatQ LeftHandRotation => LeftHandSlot?.GlobalRotation ?? floatQ.Identity;

	public floatQ RightHandRotation => RightHandSlot?.GlobalRotation ?? floatQ.Identity;

	public float3 LeftControllerPosition => LeftControllerSlot?.GlobalPosition ?? float3.Zero;

	public float3 RightControllerPosition => RightControllerSlot?.GlobalPosition ?? float3.Zero;

	public float3 LeftHipPosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.leftThigh?.Target)?.GlobalPosition ?? (HipsPosition + HipsRotation * float3.Left * 0.2f * GlobalScale);

	public float3 RightHipPosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.rightThigh?.Target)?.GlobalPosition ?? (HipsPosition + HipsRotation * float3.Right * 0.2f * GlobalScale);

	public float3 LeftShoulderPosition
	{
		get
		{
			VRIKAvatar ik = GetRegisteredComponent<VRIKAvatar>();
			return (ik?.IK?.Target?.Solver?.BoneReferences?.leftShoulder?.Target ?? ik?.IK?.Target?.Solver?.BoneReferences?.leftUpperArm?.Target)?.GlobalPosition ?? (NeckPosition + HeadFacingRotation * float3.Left * 0.25f * GlobalScale);
		}
	}

	public float3 RightShoulderPosition
	{
		get
		{
			VRIKAvatar ik = GetRegisteredComponent<VRIKAvatar>();
			return (ik?.IK?.Target?.Solver?.BoneReferences?.rightShoulder?.Target ?? ik?.IK?.Target?.Solver?.BoneReferences?.rightUpperArm?.Target)?.GlobalPosition ?? (NeckPosition + HeadFacingRotation * float3.Right * 0.25f * GlobalScale);
		}
	}

	public float3 LeftUpperArmPosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.leftUpperArm?.Target)?.GlobalPosition ?? (NeckPosition + HeadFacingRotation * float3.Left * 0.3f * GlobalScale);

	public float3 RightUpperArmPosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.rightUpperArm?.Target)?.GlobalPosition ?? (NeckPosition + HeadFacingRotation * float3.Right * 0.3f * GlobalScale);

	public float3 LeftFootPosition => LeftFootSlot?.GlobalPosition ?? (GetRegisteredComponent<VRIKAvatar>()?.LeftFootNode.Slot)?.GlobalPosition ?? (FeetPosition + HeadFacingRotation * float3.Left * 0.2f * GlobalScale);

	public float3 RightFootPosition => RightFootSlot?.GlobalPosition ?? (GetRegisteredComponent<VRIKAvatar>()?.RightFootNode.Slot)?.GlobalPosition ?? (FeetPosition + HeadFacingRotation * float3.Right * 0.2f * GlobalScale);

	public floatQ LeftFootRotation => LeftFootSlot?.GlobalRotation ?? floatQ.Identity;

	public floatQ RightFootRotation => RightFootSlot?.GlobalRotation ?? floatQ.Identity;

	public float3 LeftKneePosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.leftCalf?.Target)?.GlobalPosition ?? MathX.Lerp(LeftHipPosition, LeftFootPosition, 0.5f);

	public float3 RightKneePosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.rightCalf?.Target)?.GlobalPosition ?? MathX.Lerp(RightHipPosition, RightFootPosition, 0.5f);

	public float3 LeftElbowPosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.leftForearm?.Target)?.GlobalPosition ?? MathX.Lerp(LeftShoulderPosition, LeftHandPosition, 0.5f);

	public float3 RightElbowPosition => (GetRegisteredComponent<VRIKAvatar>()?.IK?.Target?.Solver?.BoneReferences?.rightForearm?.Target)?.GlobalPosition ?? MathX.Lerp(RightShoulderPosition, RightHandPosition, 0.5f);

	public float3 LocalLeftHandPosition
	{
		get
		{
			Slot _hand = LeftHandSlot;
			if (_hand == null)
			{
				return float3.Zero;
			}
			return base.Slot.SpacePointToLocal(_hand.LocalPosition, _hand.Parent);
		}
	}

	public float3 LocalRightHandPosition
	{
		get
		{
			Slot _hand = RightHandSlot;
			if (_hand == null)
			{
				return float3.Zero;
			}
			return base.Slot.SpacePointToLocal(_hand.LocalPosition, _hand.Parent);
		}
	}

	public floatQ LocalLeftHandRotation
	{
		get
		{
			Slot _hand = LeftHandSlot;
			if (_hand == null)
			{
				return floatQ.Identity;
			}
			return base.Slot.SpaceRotationToLocal(_hand.LocalRotation, _hand.Parent);
		}
	}

	public floatQ LocalRightHandRotation
	{
		get
		{
			Slot _hand = RightHandSlot;
			if (_hand == null)
			{
				return floatQ.Identity;
			}
			return base.Slot.SpaceRotationToLocal(_hand.LocalRotation, _hand.Parent);
		}
	}

	private TrackedDevicePositioner HipsDevice
	{
		get
		{
			if ((_hipsPositioner == null || _hipsPositioner.CorrespondingBodyNode.Value != BodyNode.Hips) && !IsRemoved)
			{
				_hipsPositioner = GetRegisteredComponent((TrackedDevicePositioner p) => (BodyNode)p.CorrespondingBodyNode == BodyNode.Hips);
			}
			return _hipsPositioner;
		}
	}

	private TrackedDevicePositioner LeftFootDevice
	{
		get
		{
			if ((_leftFootPositioner == null || _leftFootPositioner.CorrespondingBodyNode.Value != BodyNode.LeftFoot) && !IsRemoved)
			{
				_leftFootPositioner = GetRegisteredComponent((TrackedDevicePositioner p) => (BodyNode)p.CorrespondingBodyNode == BodyNode.LeftFoot);
			}
			return _leftFootPositioner;
		}
	}

	private TrackedDevicePositioner RightFootDevice
	{
		get
		{
			if ((_rightFootPositioner == null || _rightFootPositioner.CorrespondingBodyNode.Value != BodyNode.RightFoot) && !IsRemoved)
			{
				_rightFootPositioner = GetRegisteredComponent((TrackedDevicePositioner p) => (BodyNode)p.CorrespondingBodyNode == BodyNode.RightFoot);
			}
			return _rightFootPositioner;
		}
	}

	public float3 LocalNeckPosition => base.Slot.GlobalPointToLocal(NeckPosition);

	public float3 NeckPosition
	{
		get
		{
			Slot neck = GetRegisteredComponent((VRIKAvatar avatar) => avatar.IsEquipped)?.IK.Target?.Solver.BoneReferences.neck.Target;
			if (neck != null)
			{
				return neck.GlobalPosition;
			}
			float3 neckOffset = GetNeckOffset();
			neckOffset = LocalHeadRotation * neckOffset;
			return base.Slot.LocalPointToGlobal(LocalHeadPosition + neckOffset);
		}
		set
		{
			SetProxyPosition(NeckPosition, in value);
		}
	}

	public float3 HipsPosition
	{
		get
		{
			TrackedDevicePositioner _hipsDevice = HipsDevice;
			if (_hipsDevice != null && _hipsDevice.IsTracking.Value && _hipsDevice.IsActive.Value && _hipsDevice.BodyNodeRoot.Target != null)
			{
				return _hipsDevice.BodyNodeRoot.Target.GlobalPosition;
			}
			VRIKAvatar vrIkAvatar = GetRegisteredComponent((VRIKAvatar avatar) => avatar.IsEquipped);
			Slot ikPelvis = vrIkAvatar?.IK.Target?.Solver.BoneReferences.pelvis.Target;
			if (ikPelvis != null && vrIkAvatar.PelvisCalibrated.Value)
			{
				_ = vrIkAvatar.Slot;
				Slot proxy = vrIkAvatar.PelvisProxy;
				return vrIkAvatar.PelvisNode.Slot.GetTransformedByAnother(proxy, ikPelvis.GlobalPosition, ikPelvis.GlobalRotation, vrIkAvatar.Slot.GlobalScale).DecomposedPosition;
			}
			return MathX.Lerp(FeetPosition, HeadPosition, 0.6f) + base.Slot.LocalVectorToGlobal(LocalHeadFacingDirection * -0.1f);
		}
		set
		{
			SetProxyPosition(HipsPosition, in value);
		}
	}

	public float3 LocalHipsPosition => base.Slot.GlobalPointToLocal(HipsPosition);

	public floatQ HipsRotation
	{
		get
		{
			TrackedDevicePositioner _hipsDevice = HipsDevice;
			if (_hipsDevice?.BodyNodeRoot.Target != null && _hipsDevice.IsTracking.Value)
			{
				return _hipsDevice.BodyNodeRoot.Target.GlobalRotation;
			}
			return HeadFacingRotation;
		}
		set
		{
			SetProxyRotation(HipsRotation, in value);
		}
	}

	public Slot LeftHandSlot
	{
		get
		{
			if ((_cachedLeftHandSlot == null || _cachedLeftHandSlot.IsDestroyed || _cachedLeftHandSlot.IsDisposed) && !IsRemoved)
			{
				TrackedDevicePositioner positioner = GetRegisteredComponent((TrackedDevicePositioner p) => p.AutoBodyNode.Value == BodyNode.LeftHand);
				_cachedLeftHandSlot = positioner?.BodyNodeRoot.Target ?? positioner?.Slot;
			}
			return _cachedLeftHandSlot;
		}
	}

	public Slot RightHandSlot
	{
		get
		{
			if ((_cachedRightHandSlot == null || _cachedRightHandSlot.IsDestroyed || _cachedRightHandSlot.IsDisposed) && !IsRemoved)
			{
				TrackedDevicePositioner positioner = GetRegisteredComponent((TrackedDevicePositioner p) => p.AutoBodyNode.Value == BodyNode.RightHand);
				_cachedRightHandSlot = positioner?.BodyNodeRoot.Target ?? positioner?.Slot;
			}
			return _cachedRightHandSlot;
		}
	}

	public Slot LeftControllerSlot
	{
		get
		{
			if ((_cachedLeftControllerSlot == null || _cachedLeftControllerSlot.IsDestroyed || _cachedLeftControllerSlot.IsDisposed) && !IsRemoved)
			{
				TrackedDevicePositioner positioner = GetRegisteredComponent((TrackedDevicePositioner p) => p.AutoBodyNode.Value == BodyNode.LeftController);
				_cachedLeftControllerSlot = positioner?.BodyNodeRoot.Target ?? positioner?.Slot;
			}
			return _cachedLeftControllerSlot;
		}
	}

	public Slot RightControllerSlot
	{
		get
		{
			if ((_cachedRightControllerSlot == null || _cachedRightControllerSlot.IsDestroyed || _cachedRightControllerSlot.IsDisposed) && !IsRemoved)
			{
				TrackedDevicePositioner positioner = GetRegisteredComponent((TrackedDevicePositioner p) => p.AutoBodyNode.Value == BodyNode.RightController);
				_cachedRightControllerSlot = positioner?.BodyNodeRoot.Target ?? positioner?.Slot;
			}
			return _cachedRightControllerSlot;
		}
	}

	public Slot LeftFootSlot
	{
		get
		{
			if ((_cachedLeftFootSlot == null || _cachedLeftFootSlot.IsDestroyed || _cachedLeftFootSlot.IsDisposed) && !IsRemoved)
			{
				TrackedDevicePositioner positioner = GetRegisteredComponent((TrackedDevicePositioner p) => p.AutoBodyNode.Value == BodyNode.LeftFoot);
				_cachedLeftFootSlot = positioner?.BodyNodeRoot.Target ?? positioner?.Slot;
			}
			return _cachedLeftFootSlot;
		}
	}

	public Slot RightFootSlot
	{
		get
		{
			if ((_cachedRightFootSlot == null || _cachedRightFootSlot.IsDestroyed || _cachedRightFootSlot.IsDisposed) && !IsRemoved)
			{
				TrackedDevicePositioner positioner = GetRegisteredComponent((TrackedDevicePositioner p) => p.AutoBodyNode.Value == BodyNode.RightFoot);
				_cachedRightFootSlot = positioner?.BodyNodeRoot.Target ?? positioner?.Slot;
			}
			return _cachedRightFootSlot;
		}
	}

	public float3 HeadPosition
	{
		get
		{
			return HeadSlot?.GlobalPosition ?? base.Slot.GlobalPosition;
		}
		set
		{
			SetProxyPosition(HeadPosition, in value);
		}
	}

	public float3 GroundProjectedHeadPosition
	{
		get
		{
			return base.Slot.LocalPointToGlobal(LocalHeadPosition.x_z);
		}
		set
		{
			SetProxyPosition(GroundProjectedHeadPosition, in value);
		}
	}

	public float3 ViewPosition
	{
		get
		{
			Slot overridenView = OverrideView.Target;
			if (overridenView != null)
			{
				return overridenView.GlobalPosition;
			}
			if (ActiveUser == base.LocalUser)
			{
				return base.World.LocalUserViewPosition;
			}
			ViewReferenceController viewReference = GetRegisteredComponent<ViewReferenceController>();
			if (viewReference != null && viewReference.AreStreamsActive)
			{
				return viewReference.Slot.GlobalPosition;
			}
			return HeadPosition;
		}
		set
		{
			SetProxyPosition(ViewPosition, in value);
		}
	}

	public floatQ ViewRotation
	{
		get
		{
			Slot overridenView = OverrideView.Target;
			if (overridenView != null)
			{
				return overridenView.GlobalRotation;
			}
			if (ActiveUser == base.LocalUser)
			{
				return base.World.LocalUserViewRotation;
			}
			ViewReferenceController viewReference = GetRegisteredComponent<ViewReferenceController>();
			if (viewReference != null && viewReference.AreStreamsActive)
			{
				return viewReference.Slot.GlobalRotation;
			}
			return HeadRotation;
		}
		set
		{
			SetProxyRotation(ViewRotation, in value);
		}
	}

	public float3 LocalHeadPosition
	{
		get
		{
			Slot _head = HeadSlot;
			if (_head == null)
			{
				return float3.Zero;
			}
			return base.Slot.SpacePointToLocal(_head.LocalPosition, _head.Parent);
		}
	}

	public floatQ LocalHeadRotation
	{
		get
		{
			Slot _head = HeadSlot;
			if (_head == null)
			{
				return floatQ.Identity;
			}
			return base.Slot.SpaceRotationToLocal(_head.LocalRotation, _head.Parent);
		}
	}

	public float3 LocalHeadFacingDirection => ((HeadSlot?.LocalRotation ?? floatQ.Identity) * float3.Forward).x_z.Normalized;

	public float3 HeadFacingDirection
	{
		get
		{
			return base.Slot.LocalDirectionToGlobal(LocalHeadFacingDirection);
		}
		set
		{
			float3 localTargetDirection = base.Slot.GlobalDirectionToLocal(in value);
			float3 globalTargetDirection = base.Slot.LocalDirectionToGlobal(localTargetDirection.x_z);
			float3 prevHeadPos = HeadPosition;
			base.Slot.GlobalRotation = base.Slot.GlobalRotation * floatQ.FromToRotation(HeadFacingDirection, in globalTargetDirection);
			HeadPosition = prevHeadPos;
		}
	}

	public floatQ HeadFacingRotation
	{
		get
		{
			return floatQ.LookRotation(HeadFacingDirection, base.Slot.Up);
		}
		set
		{
			SetProxyRotation(HeadFacingRotation, in value);
		}
	}

	public floatQ HeadRotation
	{
		get
		{
			return HeadSlot?.GlobalRotation ?? base.Slot.GlobalRotation;
		}
		set
		{
			float3 prevHeadPosition = HeadPosition;
			base.Slot.GlobalRotation = base.Slot.GlobalRotation * floatQ.FromToRotation(HeadRotation, value);
			HeadPosition = prevHeadPosition;
		}
	}

	public float3 FeetPosition
	{
		get
		{
			TrackedDevicePositioner leftFoot = LeftFootDevice;
			TrackedDevicePositioner rightFoot = RightFootDevice;
			if (leftFoot?.BodyNodeRoot.Target != null && rightFoot?.BodyNodeRoot.Target != null && leftFoot.IsTracking.Value && rightFoot.IsTracking.Value)
			{
				return (leftFoot.BodyNodeRoot.Target.GlobalPosition + rightFoot.BodyNodeRoot.Target.GlobalPosition) * 0.5f;
			}
			VRIKAvatar ik;
			if ((ik = GetRegisteredComponent<VRIKAvatar>()) != null && ik.LeftFootNode != null && ik.RightFootNode != null)
			{
				return (ik.LeftFootNode.Slot.GlobalPosition + ik.RightFootNode.Slot.GlobalPosition) * 0.5f;
			}
			Slot _headSlot = HeadSlot;
			return _headSlot?.Parent.LocalPointToGlobal(_headSlot.LocalPosition.x_z + LocalHeadFacingDirection * -0.1f) ?? base.Slot.GlobalPosition;
		}
		set
		{
			SetProxyPosition(FeetPosition, in value);
		}
	}

	public floatQ FeetRotation
	{
		get
		{
			TrackedDevicePositioner leftFoot = LeftFootDevice;
			TrackedDevicePositioner rightFoot = RightFootDevice;
			if (leftFoot?.BodyNodeRoot.Target != null && rightFoot?.BodyNodeRoot.Target != null && leftFoot.IsTracking.Value && rightFoot.IsTracking.Value)
			{
				float3 feetForward = leftFoot.BodyNodeRoot.Target.Forward + rightFoot.BodyNodeRoot.Target.Forward;
				feetForward = base.Slot.GlobalDirectionToLocal(in feetForward).x_z.Normalized;
				if (MathX.Dot(LocalHeadFacingDirection, in feetForward) < 0f)
				{
					feetForward *= -1;
				}
				floatQ rotation = floatQ.LookRotation(in feetForward, float3.Up);
				return base.Slot.LocalRotationToGlobal(in rotation);
			}
			return HeadFacingRotation;
		}
		set
		{
			SetProxyRotation(FeetRotation, in value);
		}
	}

	public float3 GetGlobalPosition(UserNode node)
	{
		return node switch
		{
			UserNode.None => float3.Zero, 
			UserNode.Root => base.Slot.GlobalPosition, 
			UserNode.GroundProjectedHead => GroundProjectedHeadPosition, 
			UserNode.Head => HeadSlot?.GlobalPosition ?? base.Slot.GlobalPosition, 
			UserNode.View => ViewPosition, 
			UserNode.Hips => HipsPosition, 
			UserNode.Feet => FeetPosition, 
			_ => throw new Exception("Invalid UserNode: " + node), 
		};
	}

	public floatQ GetGlobalRotation(UserNode node)
	{
		return node switch
		{
			UserNode.None => floatQ.Identity, 
			UserNode.Root => base.Slot.GlobalRotation, 
			UserNode.GroundProjectedHead => HeadFacingRotation, 
			UserNode.Head => HeadSlot?.GlobalRotation ?? base.Slot.GlobalRotation, 
			UserNode.View => ViewRotation, 
			UserNode.Hips => HipsRotation, 
			UserNode.Feet => FeetRotation, 
			_ => throw new Exception("Invalid UserNode: " + node), 
		};
	}

	public void SetGlobalPosition(UserNode node, in float3 position)
	{
		switch (node)
		{
		case UserNode.Root:
			base.Slot.GlobalPosition = position;
			break;
		case UserNode.GroundProjectedHead:
			GroundProjectedHeadPosition = position;
			break;
		case UserNode.Head:
			HeadPosition = position;
			break;
		case UserNode.View:
			ViewPosition = position;
			break;
		case UserNode.Hips:
			HipsPosition = position;
			break;
		case UserNode.Feet:
			FeetPosition = position;
			break;
		default:
			throw new Exception("Invalid UserNode: " + node);
		case UserNode.None:
			break;
		}
	}

	public void SetGlobalRotation(UserNode node, in floatQ rotation)
	{
		switch (node)
		{
		case UserNode.Root:
			base.Slot.GlobalRotation = rotation;
			break;
		case UserNode.GroundProjectedHead:
			HeadFacingRotation = rotation;
			break;
		case UserNode.Head:
			HeadRotation = rotation;
			break;
		case UserNode.View:
			ViewRotation = rotation;
			break;
		case UserNode.Hips:
			HipsRotation = rotation;
			break;
		case UserNode.Feet:
			FeetRotation = rotation;
			break;
		default:
			throw new Exception("Invalid UserNode: " + node);
		case UserNode.None:
			break;
		}
	}

	public void JumpToPoint(float3 targetPoint, float distance = 1.5f)
	{
		float3 dir = (targetPoint - HeadPosition).Normalized;
		HeadPosition = targetPoint - dir * 1.5f;
		HeadFacingDirection = dir;
	}

	public Slot GetControllerSlot(Chirality node, bool throwOnInvalid = true)
	{
		switch (node)
		{
		case Chirality.Left:
			return LeftControllerSlot;
		case Chirality.Right:
			return RightControllerSlot;
		default:
			if (throwOnInvalid)
			{
				throw new ArgumentException("Invalid node: " + node);
			}
			return null;
		}
	}

	public Slot GetHandSlot(Chirality chirality, bool throwOnInvalid = true)
	{
		switch (chirality)
		{
		case Chirality.Left:
			return LeftHandSlot;
		case Chirality.Right:
			return RightHandSlot;
		default:
			if (throwOnInvalid)
			{
				throw new ArgumentException("Invalid chirality: " + chirality);
			}
			return null;
		}
	}

	private void SetProxyPosition(in float3 currentPosition, in float3 newPosition)
	{
		_ = base.Slot.GlobalPosition;
		float3 globalOffset = newPosition - currentPosition;
		Slot slot = base.Slot;
		slot.GlobalPosition += globalOffset;
	}

	private void SetProxyRotation(in floatQ currentRotation, in floatQ newRotation)
	{
		floatQ globalOffset = floatQ.FromToRotation(currentRotation, newRotation);
		base.Slot.GlobalRotation = globalOffset * base.Slot.GlobalRotation;
	}

	public void PositionReliably(Action positioner)
	{
		StartTask(async delegate
		{
			await RunPositionReliably(positioner);
		});
	}

	private async Task RunPositionReliably(Action positioner)
	{
		bool isFirst = true;
		int positionTimes = 2;
		do
		{
			if (!isFirst)
			{
				await default(NextUpdate);
			}
			else
			{
				isFirst = false;
			}
			if (!IsRemoved)
			{
				try
				{
					positioner();
				}
				catch (Exception)
				{
					UniLog.Error("Exception running reliable positioner:\n{ex}", stackTrace: false);
					break;
				}
				continue;
			}
			break;
		}
		while (!ReceivedFirstPositionalData || positionTimes-- > 0);
	}

	public float3 GetNeckOffset()
	{
		float3 neckOffset = DEFAULT_NECK_OFFSET;
		List<INeckOffsetSource> neckOffsets = Pool.BorrowList<INeckOffsetSource>();
		GetRegisteredComponents(neckOffsets);
		if (neckOffsets.Count > 0)
		{
			int maxPriority = int.MinValue;
			foreach (INeckOffsetSource offset in neckOffsets)
			{
				if (offset.NeckOffset.HasValue && offset.NeckOffsetPriority > maxPriority)
				{
					maxPriority = offset.NeckOffsetPriority;
					neckOffset = offset.NeckOffset.Value;
				}
			}
		}
		Pool.Return(ref neckOffsets);
		return neckOffset;
	}

	public bool IsAtScale(float scale)
	{
		scale = base.Slot.Parent.GlobalScaleToLocal(scale);
		return MathX.Approximately(MathX.AvgComponent(base.Slot.LocalScale), scale);
	}

	public Task SetUserScale(float scale, float time)
	{
		_scaleAnimCancel?.Cancel();
		_scaleAnimCancel = new CancellationTokenSource();
		return StartTask(async delegate
		{
			await SetUserScaleAnim(scale, time, _scaleAnimCancel.Token).ConfigureAwait(continueOnCapturedContext: false);
		});
	}

	private async ValueTask SetUserScaleAnim(float scale, float time, CancellationToken cancellationToken)
	{
		List<LocomotionPermissions> validators = Pool.BorrowList<LocomotionPermissions>();
		base.World.Permissions.GetValidators(typeof(LocomotionController), validators, ActiveUser);
		foreach (LocomotionPermissions validator in validators)
		{
			scale = validator.ClampScale(scale, ActiveUser);
		}
		Pool.Return(ref validators);
		float3 from = base.Slot.LocalScale;
		float3 to = base.Slot.Parent.GlobalScaleToLocal(scale) * float3.One;
		float3 prevPos;
		if (time > 0f)
		{
			for (float f = 0f; f < 1f; f += base.Time.Delta / time)
			{
				prevPos = FeetPosition;
				base.Slot.LocalScale = MathX.Lerp(in from, in to, f);
				FeetPosition = prevPos;
				await default(NextUpdate);
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}
			}
		}
		prevPos = FeetPosition;
		base.Slot.LocalScale = to;
		FeetPosition = prevPos;
		_scaleAnimCancel = null;
	}

	protected override void OnAwake()
	{
		base.OnAwake();
		base.Slot.RegisterUserRoot(this);
		base.Slot.OnPrepareDestroy += Slot_OnPrepareDestroy;
	}

	protected override void OnStart()
	{
		base.OnStart();
		_localNameplate = base.Slot.AddLocalSlot("Local Name Badge");
		NameplateHelper.SetupDefaultNameBadge(_localNameplate, ActiveUser);
		NameplateHelper.SetupDefaultIconBadge(_localNameplate, ActiveUser);
		NameplateHelper.SetupDefaultLiveIndicator(_localNameplate, ActiveUser);
		_avatarManager = GetRegisteredComponent<AvatarManager>();
		UpdateLocalNameplate();
	}

	private void UpdateLocalNameplate()
	{
		if (_avatarManager != null && _localNameplate != null)
		{
			_localNameplate.ForeachComponentInChildren(delegate(AvatarNameTagAssigner a)
			{
				a.UpdateTags(_avatarManager);
			});
			_localNameplate.ForeachComponentInChildren(delegate(AvatarBadgeManager a)
			{
				a.UpdateBadges(_avatarManager);
			});
			_avatarManagerVersion = _avatarManager.UpdateVersion;
		}
	}

	private void Slot_OnPrepareDestroy(Slot slot)
	{
		UniLog.Log($"Destroying User: {ActiveUser}\nCurrently updating user: {base.World.UpdateManager.CurrentlyUpdatingUser}", stackTrace: true);
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		base.Slot.UnregisterUserRoot(this);
	}

	private void OnRenderSettingChanged(DesktopRenderSettings settings)
	{
		_renderSettings = settings;
	}

	private void UnregisterSettings()
	{
		if (_settingRegistered)
		{
			Settings.UnregisterComponentChanges<DesktopRenderSettings>(OnRenderSettingChanged);
			_settingRegistered = false;
		}
	}

	protected override void OnCommonUpdate()
	{
		if (_avatarManager == null || _avatarManager.IsRemoved)
		{
			_avatarManager = GetRegisteredComponent<AvatarManager>();
		}
		if (_avatarManager != null && _avatarManagerVersion != _avatarManager.UpdateVersion)
		{
			UpdateLocalNameplate();
		}
		if (base.LocalUser != ActiveUser)
		{
			UnregisterSettings();
			return;
		}
		if (!_settingRegistered)
		{
			Settings.RegisterComponentChanges<DesktopRenderSettings>(OnRenderSettingChanged);
			_settingRegistered = true;
		}
		float3 globalPos = base.Slot.GlobalPosition;
		floatQ globalRot = base.Slot.GlobalRotation;
		float3 globalScl = base.Slot.GlobalScale;
		if (MathX.MinComponent(MathX.Abs(in globalScl)) <= 0f || globalScl.IsInfinity || globalScl.IsNaN)
		{
			UniLog.Warning("UserRoot Global Scale was invalid (zero, NaN or infinity), reseting user transform.\n" + base.Slot.ParentHierarchyToString());
			base.Slot.Parent = base.World.RootSlot;
			base.Slot.SetIdentityTransform();
			base.Slot.LocalScale = this.GetDefaultScale() * float3.One;
		}
		globalScl = base.Slot.GlobalScale;
		if (globalPos.IsInfinity || globalPos.IsNaN || globalRot.IsInfinity || globalRot.IsNaN || globalScl.IsInfinity || globalScl.IsNaN || MathX.MaxComponent(MathX.Abs(in globalPos)) > 100000f)
		{
			base.Slot.Destroy();
			return;
		}
		float3 localScale = base.Slot.LocalScale;
		if (MathX.Abs(localScale.x - localScale.y) + MathX.Abs(localScale.y - localScale.z) > 1E-05f)
		{
			base.Slot.LocalScale = MathX.AvgComponent(base.Slot.LocalScale) * float3.One;
		}
	}

	public T GetRegisteredComponent<T>(Predicate<T> filter = null) where T : class, IComponent
	{
		foreach (T t in GetComponentsOfType<T>())
		{
			if (!t.IsRemoved && (filter == null || filter(t)))
			{
				return t;
			}
		}
		return null;
	}

	public void GetRegisteredComponents<T>(List<T> list, Predicate<T> filter = null) where T : class, IComponent
	{
		foreach (T t in GetComponentsOfType<T>())
		{
			if (!t.IsRemoved && (filter == null || filter(t)))
			{
				list.Add(t);
			}
		}
	}

	public List<T> GetRegisteredComponents<T>(Predicate<T> filter = null) where T : class, IComponent
	{
		List<T> list = new List<T>();
		GetRegisteredComponents(list, filter);
		return list;
	}

	public void ForeachRegisteredComponent<T>(Action<T> action) where T : class, IComponent
	{
		foreach (T t in GetComponentsOfType<T>())
		{
			action(t);
		}
	}

	private List<T> GetComponentsOfType<T>() where T : class, IComponent
	{
		if (_perTypeComponents.TryGetValue(typeof(T), out IList list))
		{
			return (List<T>)list;
		}
		List<T> typedList = new List<T>();
		foreach (Component registeredComponent in _registeredComponents)
		{
			if (registeredComponent is T t)
			{
				typedList.Add(t);
			}
		}
		_perTypeComponents.Add(typeof(T), typedList);
		return typedList;
	}

	internal void RegisterComponent(Component component)
	{
		if (!_registeredComponents.Add(component))
		{
			throw new Exception("Component already registered: " + component);
		}
		Type type = component.GetType();
		foreach (KeyValuePair<Type, IList> group in _perTypeComponents)
		{
			if (group.Key.IsAssignableFrom(type))
			{
				group.Value.Add(component);
			}
		}
	}

	internal void UnregisterComponent(Component component)
	{
		if (_registeredComponents == null)
		{
			return;
		}
		if (!_registeredComponents.Remove(component))
		{
			throw new Exception("Component is not registered: " + component);
		}
		Type type = component.GetType();
		foreach (KeyValuePair<Type, IList> group in _perTypeComponents)
		{
			if (group.Key.IsAssignableFrom(type))
			{
				group.Value.Remove(component);
			}
		}
	}

	protected override void OnDispose()
	{
		base.Slot.OnPrepareDestroy -= Slot_OnPrepareDestroy;
		RootSpaceHint = null;
		_lastLink = null;
		_lastUser = null;
		_cachedHeadSlot = null;
		_cachedLeftHandSlot = null;
		_cachedRightHandSlot = null;
		_cachedLeftControllerSlot = null;
		_cachedRightControllerSlot = null;
		_hipsPositioner = null;
		_leftFootPositioner = null;
		_rightFootPositioner = null;
		_registeredComponents.Clear();
		_registeredComponents = null;
		foreach (KeyValuePair<Type, IList> perTypeComponent in _perTypeComponents)
		{
			perTypeComponent.Value.Clear();
		}
		_perTypeComponents.Clear();
		_perTypeComponents = null;
		base.OnDispose();
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		RenderSettings = new SyncRef<IRenderSettingsSource>();
		ScreenController = new SyncRef<ScreenController>();
		OverrideRoot = new SyncRef<Slot>();
		OverrideView = new SyncRef<Slot>();
		PrimaryListener = new SyncRef<AudioListener>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => RenderSettings, 
			4 => ScreenController, 
			5 => OverrideRoot, 
			6 => OverrideView, 
			7 => PrimaryListener, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static UserRoot __New()
	{
		return new UserRoot();
	}
}
