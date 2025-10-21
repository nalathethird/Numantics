// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.StaticAudioClip
using System;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using Elements.Data;
using FrooxEngine;
using FrooxEngine.UIX;
using FrooxEngine.Undo;
using SharpPipe;

[Category(new string[] { "Assets" })]
[OldTypeName("FrooxEngine.StaticAudioClipProvider", null)]
public class StaticAudioClip : StaticAssetProvider<AudioClip, DummyMetadata, AudioClipVariantDescriptor>
{
	public readonly Sync<AudioLoadMode> LoadMode;

	public readonly Sync<SampleRateMode> SampleRateMode;

	public override EngineAssetClass AssetClass => EngineAssetClass.Audio;

	protected override void OnAwake()
	{
		base.OnAwake();
		LoadMode.Value = AudioLoadMode.Automatic;
		SampleRateMode.Value = FrooxEngine.SampleRateMode.Conform;
	}

	public override void BuildInspectorUI(UIBuilder ui)
	{
		base.BuildInspectorUI(ui);
		AudioClipAssetMetadata metadata = ui.Root.AttachComponent<AudioClipAssetMetadata>();
		metadata.AudioClip.Target = this;
		ui.Text("Inspector.Audio.FormatInfo".AsLocaleKey(("rate", metadata.SampleRate), ("channels", metadata.Channels), ("channel_count", metadata.ChannelCount)));
		ui.Text("Inspector.Audio.Duration".AsLocaleKey(("duration", metadata.Duration), ("samples", metadata.SampleCount)));
		ui.Text("Inspector.Audio.EncodingInfo".AsLocaleKey(("info", metadata.CodecInfo), ("decoded", metadata.FullyDecoded)));
		ui.Button("Inspector.Audio.Normalize".AsLocaleKey(), Normalize);
		ui.Button("Inspector.Audio.ExtractSides".AsLocaleKey(), ExtractSides);
		ui.Button("Inspector.Audio.DenoiseRNNoise".AsLocaleKey(), Denoise);
		ui.HorizontalLayout(4f);
		ui.Text("Inspector.Audio.AmplitudeThreshold".AsLocaleKey());
		FloatTextEditorParser amplitudeField = ui.FloatField(0f, 1f, 4);
		amplitudeField.ParsedValue.Value = 0.002f;
		ui.NestOut();
		ui.HorizontalLayout(4f);
		ui.ButtonRef("Inspector.Audio.TrimSilence".AsLocaleKey(), (colorX?)null, TrimSilence, amplitudeField);
		ui.ButtonRef("Inspector.Audio.TrimStartSilence".AsLocaleKey(), (colorX?)null, TrimStartSilence, amplitudeField);
		ui.ButtonRef("Inspector.Audio.TrimEndSilence".AsLocaleKey(), (colorX?)null, TrimEndSilence, amplitudeField);
		ui.NestOut();
		ui.HorizontalLayout(4f);
		ui.Text("Inspector.Audio.PositionDuration".AsLocaleKey());
		float prevWidth = ui.Style.MinWidth;
		ui.Style.MinWidth = 64f;
		FloatTextEditorParser positionField = ui.FloatField(0f, float.MaxValue, 4);
		positionField.ParsedValue.Value = 0.1f;
		ui.Style.MinWidth = prevWidth;
		ui.NestOut();
		ui.HorizontalLayout(4f);
		ui.ButtonRef("Inspector.Audio.TrimStart".AsLocaleKey(), (colorX?)null, TrimStart, positionField);
		ui.ButtonRef("Inspector.Audio.TrimEnd".AsLocaleKey(), (colorX?)null, TrimEnd, positionField);
		ui.NestOut();
		ui.HorizontalLayout(4f);
		ui.ButtonRef("Inspector.Audio.FadeIn".AsLocaleKey(), (colorX?)null, FadeIn, positionField);
		ui.ButtonRef("Inspector.Audio.FadeOut".AsLocaleKey(), (colorX?)null, FadeOut, positionField);
		ui.NestOut();
		ui.ButtonRef("Inspector.Audio.MakeLoopable".AsLocaleKey(), (colorX?)null, MakeLoopable, positionField);
		ui.HorizontalLayout(4f);
		ui.ButtonRef("Inspector.Audio.ToWAV".AsLocaleKey(), (colorX?)null, ConvertToWAV, positionField);
		ui.ButtonRef("Inspector.Audio.ToVorbis".AsLocaleKey(), (colorX?)null, ConvertToVorbis, positionField);
		ui.ButtonRef("Inspector.Audio.ToFLAC".AsLocaleKey(), (colorX?)null, ConvertToFLAC, positionField);
		ui.NestOut();
	}

	protected override async ValueTask<AudioClipVariantDescriptor> UpdateVariantDescriptor(DummyMetadata metadata, AudioClipVariantDescriptor currentVariant)
	{
		if (currentVariant == null || currentVariant.LoadMode != LoadMode.Value || currentVariant.SampleRateMode != SampleRateMode.Value)
		{
			return new AudioClipVariantDescriptor(LoadMode, SampleRateMode);
		}
		return null;
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> Normalize()
	{
		return Process(delegate(AudioX a)
		{
			a.Normalize();
		}, null);
	}

	[SyncMethod(typeof(Func<float, Task<bool>>), new string[] { })]
	public Task<bool> AdjustVolume(float ratio)
	{
		return Process(delegate(AudioX a)
		{
			a.AdjustVolume(ratio);
		}, null);
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> ExtractSides()
	{
		return Process(delegate(AudioX a)
		{
			a.ExtractSides();
		}, null);
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> Denoise()
	{
		return Process(delegate(AudioX a)
		{
			a.Denoise();
		}, null);
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> TrimSilence()
	{
		return Process(delegate(AudioX a)
		{
			a.TrimSilence();
		}, null);
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> TrimStartSilence()
	{
		return Process(delegate(AudioX a)
		{
			a.TrimStartSilence();
		}, null);
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> TrimEndSilence()
	{
		return Process(delegate(AudioX a)
		{
			a.TrimEndSilence();
		}, null);
	}

	[SyncMethod(typeof(Func<float, Task<bool>>), new string[] { })]
	public Task<bool> TrimStart(float duration)
	{
		return Process(delegate(AudioX a)
		{
			a.TrimStart(duration);
		}, null);
	}

	[SyncMethod(typeof(Func<float, Task<bool>>), new string[] { })]
	public Task<bool> TrimEnd(float duration)
	{
		return Process(delegate(AudioX a)
		{
			a.TrimEnd(duration);
		}, null);
	}

	[SyncMethod(typeof(Func<float, Task<bool>>), new string[] { })]
	public Task<bool> FadeIn(float duration)
	{
		return Process(delegate(AudioX a)
		{
			a.FadeIn(duration);
		}, null);
	}

	[SyncMethod(typeof(Func<float, Task<bool>>), new string[] { })]
	public Task<bool> FadeOut(float duration)
	{
		return Process(delegate(AudioX a)
		{
			a.FadeOut(duration);
		}, null);
	}

	[SyncMethod(typeof(Func<float, Task<bool>>), new string[] { })]
	public Task<bool> MakeFadeLoop(float duration)
	{
		return Process(delegate(AudioX a)
		{
			a.MakeFadeLoop(duration);
		}, null);
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> ConvertToWAV()
	{
		return Process((AudioX a) => a, null, new WavEncodeSettings());
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> ConvertToVorbis()
	{
		return Process((AudioX a) => a, null, new VorbisEncodeSettings());
	}

	[SyncMethod(typeof(Func<Task<bool>>), new string[] { })]
	public Task<bool> ConvertToFLAC()
	{
		return Process((AudioX a) => a, null, new FlacEncodeSettings());
	}

	[SyncMethod(typeof(Func<ZitaParameters, Task<bool>>), new string[] { })]
	public Task<bool> ApplyZitaReverb(ZitaParameters filter)
	{
		return Process(delegate(AudioX a)
		{
			a.ApplyZitaFilter(filter);
		}, null);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void Normalize(IButton button, ButtonEventData eventData)
	{
		Process(delegate(AudioX a)
		{
			a.Normalize();
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ExtractSides(IButton button, ButtonEventData eventData)
	{
		Process(delegate(AudioX a)
		{
			a.ExtractSides();
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void Denoise(IButton button, ButtonEventData eventData)
	{
		Process(delegate(AudioX a)
		{
			a.Denoise();
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void TrimSilence(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.TrimSilence(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void TrimStartSilence(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.TrimStartSilence(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void TrimEndSilence(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.TrimEndSilence(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void TrimStart(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.TrimStart(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void TrimEnd(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.TrimEnd(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void FadeIn(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.FadeIn(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void FadeOut(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.FadeOut(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void MakeLoopable(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process(delegate(AudioX a)
		{
			a.MakeFadeLoop(textField.ParsedValue);
		}, button);
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ConvertToWAV(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process((AudioX a) => a, button, new WavEncodeSettings());
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ConvertToVorbis(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process((AudioX a) => a, button, new VorbisEncodeSettings());
	}

	[SyncMethod(typeof(Delegate), null)]
	private void ConvertToFLAC(IButton button, ButtonEventData eventData, FloatTextEditorParser textField)
	{
		Process((AudioX a) => a, button, new FlacEncodeSettings());
	}

	private Task<bool> Process(Action<AudioX> processFunc, IButton button)
	{
		return Process(delegate(AudioX a)
		{
			processFunc(a);
			return a;
		}, button);
	}

	private Task<bool> Process(Func<AudioX, AudioX> processFunc, IButton button, AudioEncodeSettings encodeSettings = null)
	{
		return StartGlobalTask(async () => await ProcessAsync(processFunc, button, encodeSettings));
	}

	private async Task<bool> ProcessAsync(Func<AudioX, AudioX> processFunc, IButton button, AudioEncodeSettings encodeSettings = null)
	{
		if (URL.Value == null)
		{
			return false;
		}
		while (Asset == null)
		{
			await default(NextUpdate);
		}
		string _description = button?.LabelText;
		if (button != null)
		{
			button.LabelText = this.GetLocalized("General.Processing");
			button.Enabled = false;
		}
		Uri uri;
		try
		{
			AudioX audiox = processFunc(await Asset.GetOriginalAudioData().ConfigureAwait(continueOnCapturedContext: false));
			uri = await base.Engine.LocalDB.SaveAssetAsync(audiox, encodeSettings).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			UniLog.Error($"Exception processing audio clip {URL.Value}:\n" + ex);
			await default(ToWorld);
			if (button != null && !button.IsDestroyed)
			{
				button.LabelText = "<color=#f00>Error! Check log.</color>";
			}
			throw;
		}
		await default(ToWorld);
		if (button != null && !button.IsDestroyed)
		{
			button.LabelText = _description;
			button.Enabled = true;
		}
		if (uri == null)
		{
			return false;
		}
		if (button != null)
		{
			base.World.BeginUndoBatch(_description);
			URL.UndoableSet(uri, forceNew: true);
			base.World.EndUndoBatch();
		}
		else
		{
			URL.Value = uri;
		}
		return true;
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		LoadMode = new Sync<AudioLoadMode>();
		SampleRateMode = new Sync<SampleRateMode>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => URL, 
			4 => LoadMode, 
			5 => SampleRateMode, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static StaticAudioClip __New()
	{
		return new StaticAudioClip();
	}
}
