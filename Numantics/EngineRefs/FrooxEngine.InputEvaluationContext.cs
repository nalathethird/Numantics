// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.InputEvaluationContext
using FrooxEngine;

public readonly struct InputEvaluationContext
{
	public readonly InputGroup group;

	public readonly InputBinding binding;

	public readonly InputAction action;

	public readonly bool registerBlocks;

	public readonly bool registerGroupBlocks;

	public bool ShouldRegisterBlocks
	{
		get
		{
			if (registerBlocks)
			{
				return action.RegisterBlocks;
			}
			return false;
		}
	}

	public bool ShouldRegisterGroupBlocks
	{
		get
		{
			if (registerGroupBlocks)
			{
				return ShouldRegisterBlocks;
			}
			return false;
		}
	}

	public InputEvaluationContext NoBlocks()
	{
		return new InputEvaluationContext(group, binding, action, registerBlocks: false, registerGroupBlocks);
	}

	public InputEvaluationContext NoGroupBlocks()
	{
		return new InputEvaluationContext(group, binding, action, registerBlocks, registerGroupBlocks: false);
	}

	public InputEvaluationContext(InputGroup group, InputBinding binding, InputAction action, bool registerBlocks = true, bool registerGroupBlocks = true)
	{
		this.group = group;
		this.binding = binding;
		this.action = action;
		this.registerBlocks = registerBlocks;
		this.registerGroupBlocks = registerGroupBlocks;
	}
}
