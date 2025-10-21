// ProtoFlux.Nodes.FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Audio.PlayOneShot
using Awwdio;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

[NodeCategory("Audio")]
public class PlayOneShot : ActionNode<FrooxEngineContext>, IMappableNode, INode
{
	public ObjectInput<IAssetProvider<AudioClip>> Clip;

	[@DefaultValue(1f)]
	public ValueInput<float> Volume;

	[@DefaultValue(1f)]
	public ValueInput<float> Speed;

	[@DefaultValue(true)]
	public ValueInput<bool> Spatialize;

	[@DefaultValue(1f)]
	public ValueInput<float> SpatialBlend;

	public ObjectInput<bool?> Global;

	public ValueInput<float3> Point;

	public ObjectInput<Slot> Root;

	[@DefaultValue(true)]
	public ValueInput<bool> ParentUnderRoot;

	[@DefaultValue(128)]
	public ValueInput<int> Priority;

	[@DefaultValue(1f)]
	public ValueInput<float> Doppler;

	[@DefaultValue(1f)]
	public ValueInput<float> MinDistance;

	[@DefaultValue(500f)]
	public ValueInput<float> MaxDistance;

	[@DefaultValue(AudioRolloffCurve.LogarithmicFadeOff)]
	public ValueInput<AudioRolloffCurve> Rolloff;

	[@DefaultValue(AudioDistanceSpace.Global)]
	public ValueInput<AudioDistanceSpace> DistanceSpace;

	[@DefaultValue(0f)]
	public ValueInput<float> MinScale;

	[@DefaultValue(float.PositiveInfinity)]
	public ValueInput<float> MaxScale;

	[@DefaultValue(AudioTypeGroup.SoundEffect)]
	public ValueInput<AudioTypeGroup> Group;

	public ValueInput<bool> IgnoreAudioEffects;

	[@DefaultValue(false)]
	public ValueInput<bool> LocalOnly;

	public readonly ObjectOutput<FrooxEngine.AudioOutput> Audio;

	public Continuation OnStartedPlaying;

	protected override IOperation Run(FrooxEngineContext context)
	{
		IAssetProvider<AudioClip> clip = Clip.Evaluate(context);
		if (clip == null)
		{
			return null;
		}
		Slot root = ((Root.Source != null) ? Root.Evaluate(context) : context.GetRootSlotContainer(this));
		if (root == null)
		{
			return null;
		}
		bool parent = !root.IsRootSlot && ParentUnderRoot.Evaluate(context, defaultValue: true);
		FrooxEngine.AudioOutput player = context.World.PlayOneShot(root.LocalPointToGlobal(Point.Evaluate(context)), clip, Volume.Evaluate(context, 1f), Spatialize.Evaluate(context, defaultValue: true), Global.Evaluate(context), Speed.Evaluate(context, 1f), parent ? root.FilterWorldElement() : null, DistanceSpace.Evaluate(context, AudioDistanceSpace.Global), LocalOnly.Evaluate(context, defaultValue: false));
		player.SpatialBlend.Value = SpatialBlend.Evaluate(context, 1f);
		player.DopplerLevel.Value = Doppler.Evaluate(context, 1f);
		player.MinDistance.Value = MinDistance.Evaluate(context, 1f);
		player.MaxDistance.Value = MaxDistance.Evaluate(context, 500f);
		player.RolloffMode.Value = Rolloff.Evaluate(context, AudioRolloffCurve.LogarithmicFadeOff);
		player.DistanceSpace.Value = DistanceSpace.Evaluate(context, AudioDistanceSpace.Global);
		player.MinScale.Value = MinScale.Evaluate(context, 0f);
		player.MaxScale.Value = MaxScale.Evaluate(context, float.PositiveInfinity);
		player.Priority.Value = Priority.Evaluate(context, 128);
		player.IgnoreAudioEffects.Value = IgnoreAudioEffects.Evaluate(context, defaultValue: false);
		player.AudioTypeGroup.Value = Group.Evaluate(context, AudioTypeGroup.SoundEffect);
		Audio.Write(player, context);
		return OnStartedPlaying.Target;
	}

	public PlayOneShot()
	{
		Audio = new ObjectOutput<FrooxEngine.AudioOutput>(this);
	}
}
