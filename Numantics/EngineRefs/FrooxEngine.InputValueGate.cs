// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.InputValueGate<T>
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;

public class InputValueGate<T> : IInputNode<T>, IInputNode where T : struct
{
	public IInputNode<T> Value;

	public readonly List<IInputNode<bool>> Gates = new List<IInputNode<bool>>();

	public bool EvaluationGate;

	public bool LastKeepsOpen;

	private List<bool> _prevGateStates = new List<bool>();

	private int _nextGateToActivate;

	public InputValueGate(IInputNode<T> value, IInputNode<bool> gate, bool evaluationGate = false, bool lastKeepsOpen = false)
	{
		Value = value;
		Gates.Add(gate);
		EvaluationGate = evaluationGate;
		LastKeepsOpen = lastKeepsOpen;
	}

	public InputValueGate(IInputNode<T> value, bool evaluationGate = false, bool lastKeepsOpen = false, params IInputNode<bool>[] gates)
	{
		Value = value;
		Gates.AddRange(gates);
		EvaluationGate = evaluationGate;
		LastKeepsOpen = lastKeepsOpen;
	}

	public T? Evaluate(in InputEvaluationContext context)
	{
		_prevGateStates.EnsureExactCount(Gates.Count);
		bool anyNull = false;
		bool anyTrue = false;
		bool lockOpen = _nextGateToActivate == Gates.Count && LastKeepsOpen;
		for (int i = 0; i < Gates.Count; i++)
		{
			InputEvaluationContext subcontext = context.NoGroupBlocks();
			if (i > _nextGateToActivate || (i == _nextGateToActivate && _prevGateStates[i]))
			{
				subcontext = subcontext.NoBlocks();
			}
			bool? stateNullable = Gates[i].Evaluate(in subcontext);
			if (!stateNullable.HasValue)
			{
				anyNull = true;
			}
			bool state = stateNullable == true;
			if (state)
			{
				anyTrue = true;
			}
			if (state && !_prevGateStates[i] && _nextGateToActivate == i)
			{
				_nextGateToActivate = i + 1;
			}
			else if (state && _nextGateToActivate < i)
			{
				_nextGateToActivate = -1;
			}
			else if (!lockOpen && !state && _nextGateToActivate == i + 1)
			{
				_nextGateToActivate = i;
			}
			_prevGateStates[i] = state;
		}
		if (!anyTrue)
		{
			_nextGateToActivate = 0;
		}
		bool gate = _nextGateToActivate == Gates.Count;
		T? value = null;
		if (!EvaluationGate || gate)
		{
			value = Value?.Evaluate(in context);
		}
		if (!value.HasValue || anyNull)
		{
			return null;
		}
		if (gate)
		{
			return value;
		}
		return default(T);
	}

	public void ResetNode()
	{
		Value?.ResetNode();
		foreach (IInputNode<bool> gate in Gates)
		{
			gate.ResetNode();
		}
		for (int i = 0; i < _prevGateStates.Count; i++)
		{
			_prevGateStates[i] = false;
		}
		_nextGateToActivate = 0;
	}

	T? IInputNode<T>.Evaluate(in InputEvaluationContext context)
	{
		return Evaluate(in context);
	}
}
