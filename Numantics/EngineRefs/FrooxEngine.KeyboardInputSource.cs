// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.KeyboardInputSource
using FrooxEngine;
using Renderite.Shared;

public class KeyboardInputSource : IInputNode<bool>, IInputNode
{
	private RepeatDigital _cachedKey;

	public Key Key { get; private set; }

	public bool RegisterBlock { get; private set; }

	public bool RepeatPress { get; private set; }

	public KeyboardInputSource(Key key)
		: this(key, !key.IsModifier())
	{
	}

	public KeyboardInputSource(Key key, bool registerBlock, bool repeatPress = false)
	{
		Key = key;
		RegisterBlock = registerBlock;
		RepeatPress = repeatPress;
	}

	public bool? Evaluate(in InputEvaluationContext context)
	{
		if (_cachedKey == null)
		{
			_cachedKey = context.group.InputInterface.GetKeyState(Key);
		}
		if (_cachedKey == null)
		{
			return null;
		}
		if (context.group.Block.IsBlocked((KeyboardBlock b) => b.IsBlocked(Key)))
		{
			return null;
		}
		if (context.group.Block.IsPropertyBlocked(_cachedKey))
		{
			return null;
		}
		if (RegisterBlock && context.ShouldRegisterBlocks)
		{
			context.group.Block.RegisterProperty(_cachedKey, context.ShouldRegisterGroupBlocks);
		}
		return RepeatPress ? _cachedKey.RepeatPressed : _cachedKey.Held;
	}

	public void ResetNode()
	{
	}

	bool? IInputNode<bool>.Evaluate(in InputEvaluationContext context)
	{
		return Evaluate(in context);
	}
}
