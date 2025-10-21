// ProtoFluxBindings, Version=2025.9.23.1238, Culture=neutral, PublicKeyToken=null
// FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot
using System;
using Awwdio;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Core;
using FrooxEngine.ProtoFlux.Runtimes.Execution;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio;

[Category(new string[] { "ProtoFlux/Runtimes/Execution/Nodes/Audio" })]
public class PlayOneShot : ActionNode<FrooxEngineContext>, FrooxEngine.FrooxEngine.ProtoFlux.IMappableNode, IProtoFluxNode, IWorker, IWorldElement, IProtoFluxNode<FrooxEngine.ProtoFlux.IMappableNode>, FrooxEngine.ProtoFlux.Core.INode, IProtoFluxNode<ProtoFlux.Core.INode>
{
	public readonly SyncRef<INodeObjectOutput<IAssetProvider<AudioClip>>> Clip;

	public readonly SyncRef<INodeValueOutput<float>> Volume;

	public readonly SyncRef<INodeValueOutput<float>> Speed;

	public readonly SyncRef<INodeValueOutput<bool>> Spatialize;

	public readonly SyncRef<INodeValueOutput<float>> SpatialBlend;

	public readonly SyncRef<INodeObjectOutput<bool?>> Global;

	public readonly SyncRef<INodeValueOutput<float3>> Point;

	public readonly SyncRef<INodeObjectOutput<Slot>> Root;

	public readonly SyncRef<INodeValueOutput<bool>> ParentUnderRoot;

	public readonly SyncRef<INodeValueOutput<int>> Priority;

	public readonly SyncRef<INodeValueOutput<float>> Doppler;

	public readonly SyncRef<INodeValueOutput<float>> MinDistance;

	public readonly SyncRef<INodeValueOutput<float>> MaxDistance;

	public readonly SyncRef<INodeValueOutput<AudioRolloffCurve>> Rolloff;

	public readonly SyncRef<INodeValueOutput<AudioDistanceSpace>> DistanceSpace;

	public readonly SyncRef<INodeValueOutput<float>> MinScale;

	public readonly SyncRef<INodeValueOutput<float>> MaxScale;

	public new readonly SyncRef<INodeValueOutput<AudioTypeGroup>> Group;

	public readonly SyncRef<INodeValueOutput<bool>> IgnoreAudioEffects;

	public readonly SyncRef<INodeValueOutput<bool>> LocalOnly;

	public new readonly NodeObjectOutput<FrooxEngine.AudioOutput> Audio;

	public readonly SyncRef<INodeOperation> OnStartedPlaying;

	public override Type NodeType => typeof(ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot);

	public ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot TypedNodeInstance { get; private set; }

	public override ProtoFlux.Core.INode NodeInstance => TypedNodeInstance;

	public override int NodeInputCount => base.NodeInputCount + 20;

	public override int NodeOutputCount => base.NodeOutputCount + 1;

	public override int NodeImpulseCount => base.NodeImpulseCount + 1;

	public override N Instantiate<N>()
	{
		if (TypedNodeInstance != null)
		{
			throw new InvalidOperationException("Node has already been instantiated");
		}
		ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot node = (TypedNodeInstance = new ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot());
		return node as N;
	}

	protected override void AssociateInstanceInternal(ProtoFlux.Core.INode node)
	{
		if (node is ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot typedNode)
		{
			TypedNodeInstance = typedNode;
			return;
		}
		throw new ArgumentException("Node instance is not of type " + typeof(ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot));
	}

	public override void ClearInstance()
	{
		TypedNodeInstance = null;
	}

	protected override ISyncRef GetInputInternal(ref int index)
	{
		ISyncRef @base = base.GetInputInternal(ref index);
		if (@base != null)
		{
			return @base;
		}
		switch (index)
		{
		case 0:
			return Clip;
		case 1:
			return Volume;
		case 2:
			return Speed;
		case 3:
			return Spatialize;
		case 4:
			return SpatialBlend;
		case 5:
			return Global;
		case 6:
			return Point;
		case 7:
			return Root;
		case 8:
			return ParentUnderRoot;
		case 9:
			return Priority;
		case 10:
			return Doppler;
		case 11:
			return MinDistance;
		case 12:
			return MaxDistance;
		case 13:
			return Rolloff;
		case 14:
			return DistanceSpace;
		case 15:
			return MinScale;
		case 16:
			return MaxScale;
		case 17:
			return Group;
		case 18:
			return IgnoreAudioEffects;
		case 19:
			return LocalOnly;
		default:
			index -= 20;
			return null;
		}
	}

	protected override INodeOutput GetOutputInternal(ref int index)
	{
		INodeOutput @base = base.GetOutputInternal(ref index);
		if (@base != null)
		{
			return @base;
		}
		if (index == 0)
		{
			return Audio;
		}
		index--;
		return null;
	}

	protected override ISyncRef GetImpulseInternal(ref int index)
	{
		ISyncRef @base = base.GetImpulseInternal(ref index);
		if (@base != null)
		{
			return @base;
		}
		if (index == 0)
		{
			return OnStartedPlaying;
		}
		index--;
		return null;
	}

	protected override void OnLoading(DataTreeNode node, LoadControl control)
	{
		base.OnLoading(node, control);
		if (control.GetFeatureFlag("Awwdio").HasValue)
		{
			return;
		}
		control.OnLoaded(this, delegate
		{
			LegacyFeatureSettings? activeSetting = Settings.GetActiveSetting<LegacyFeatureSettings>();
			if (activeSetting != null && activeSetting.PreserveLegacyReverbZoneHandling.Value)
			{
				if (Spatialize.Target == null)
				{
					IgnoreAudioEffects.InjectInputValue(value: true);
				}
				else
				{
					IgnoreAudioEffects.Target = Spatialize.Target;
				}
			}
			if (Rolloff.Target == null)
			{
				Rolloff.InjectInputValue(AudioRolloffCurve.LogarithmicClamped);
			}
		});
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		Clip = new SyncRef<INodeObjectOutput<IAssetProvider<AudioClip>>>();
		Volume = new SyncRef<INodeValueOutput<float>>();
		Speed = new SyncRef<INodeValueOutput<float>>();
		Spatialize = new SyncRef<INodeValueOutput<bool>>();
		SpatialBlend = new SyncRef<INodeValueOutput<float>>();
		Global = new SyncRef<INodeObjectOutput<bool?>>();
		Point = new SyncRef<INodeValueOutput<float3>>();
		Root = new SyncRef<INodeObjectOutput<Slot>>();
		ParentUnderRoot = new SyncRef<INodeValueOutput<bool>>();
		Priority = new SyncRef<INodeValueOutput<int>>();
		Doppler = new SyncRef<INodeValueOutput<float>>();
		MinDistance = new SyncRef<INodeValueOutput<float>>();
		MaxDistance = new SyncRef<INodeValueOutput<float>>();
		Rolloff = new SyncRef<INodeValueOutput<AudioRolloffCurve>>();
		DistanceSpace = new SyncRef<INodeValueOutput<AudioDistanceSpace>>();
		MinScale = new SyncRef<INodeValueOutput<float>>();
		MaxScale = new SyncRef<INodeValueOutput<float>>();
		Group = new SyncRef<INodeValueOutput<AudioTypeGroup>>();
		IgnoreAudioEffects = new SyncRef<INodeValueOutput<bool>>();
		LocalOnly = new SyncRef<INodeValueOutput<bool>>();
		Audio = new NodeObjectOutput<FrooxEngine.AudioOutput>();
		OnStartedPlaying = new SyncRef<INodeOperation>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => Clip, 
			4 => Volume, 
			5 => Speed, 
			6 => Spatialize, 
			7 => SpatialBlend, 
			8 => Global, 
			9 => Point, 
			10 => Root, 
			11 => ParentUnderRoot, 
			12 => Priority, 
			13 => Doppler, 
			14 => MinDistance, 
			15 => MaxDistance, 
			16 => Rolloff, 
			17 => DistanceSpace, 
			18 => MinScale, 
			19 => MaxScale, 
			20 => Group, 
			21 => IgnoreAudioEffects, 
			22 => LocalOnly, 
			23 => Audio, 
			24 => OnStartedPlaying, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot __New()
	{
		return new FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot();
	}
}
