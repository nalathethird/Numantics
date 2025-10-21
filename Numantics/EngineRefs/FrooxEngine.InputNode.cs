// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.InputNode
using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

public static class InputNode
{
	public static NoBlocksInput<T> NoBlocks<T>(this IInputNode<T> node) where T : struct
	{
		return new NoBlocksInput<T>(node);
	}

	public static ConstantInput<T> Constant<T>(this T value) where T : struct
	{
		return new ConstantInput<T>(value);
	}

	public static MultiplyDeltaTime<T> dT<T>(this IInputNode<T> node) where T : struct
	{
		return new MultiplyDeltaTime<T>(node);
	}

	public static DivideDeltaTime<T> dT_inv<T>(this IInputNode<T> node) where T : struct
	{
		return new DivideDeltaTime<T>(node);
	}

	public static Analog2DMagnitude Magnitude(this IInputNode<float2> node)
	{
		return new Analog2DMagnitude(node);
	}

	public static Analog3DMagnitude Magnitude(this IInputNode<float3> node)
	{
		return new Analog3DMagnitude(node);
	}

	public static Analog2DDeadzone Deadzone(this IInputNode<float2> node, float deadzone)
	{
		return new Analog2DDeadzone(node, deadzone);
	}

	public static Analog3DDeadzone Deadzone(this IInputNode<float3> node, float deadzone)
	{
		return new Analog3DDeadzone(node, deadzone);
	}

	public static GetAnalog2D_Axis X(this IInputNode<float2> node)
	{
		return new GetAnalog2D_Axis(node, 0);
	}

	public static GetAnalog2D_Axis Y(this IInputNode<float2> node)
	{
		return new GetAnalog2D_Axis(node, 1);
	}

	public static GetAnalog3D_Axis X(this IInputNode<float3> node)
	{
		return new GetAnalog3D_Axis(node, 0);
	}

	public static GetAnalog3D_Axis Y(this IInputNode<float3> node)
	{
		return new GetAnalog3D_Axis(node, 1);
	}

	public static GetAnalog3D_Axis Z(this IInputNode<float3> node)
	{
		return new GetAnalog3D_Axis(node, 2);
	}

	public static ComposeAnalog2D_Axis XY(IInputNode<float> x, IInputNode<float> y, bool requireAll = true)
	{
		return new ComposeAnalog2D_Axis(x, y, requireAll);
	}

	public static ComposeAnalog3D_Axis XYZ(IInputNode<float> x, IInputNode<float> y, IInputNode<float> z, bool requireAll = true)
	{
		return new ComposeAnalog3D_Axis(x, y, z, requireAll);
	}

	public static ComposeAnalog3D_Axis_XY_Z XYZ(IInputNode<float2> xy, IInputNode<float> z, bool requireAll = true)
	{
		return new ComposeAnalog3D_Axis_XY_Z(xy, z, requireAll);
	}

	public static ComposeAnalog3D_Axis_X_YZ XYZ(IInputNode<float> x, IInputNode<float2> yz, bool requireAll = true)
	{
		return new ComposeAnalog3D_Axis_X_YZ(x, yz, requireAll);
	}

	public static Swizzle_YX YX(this IInputNode<float2> xy)
	{
		return new Swizzle_YX(xy);
	}

	public static Swizzle_XY0 XY_(this IInputNode<float2> xy)
	{
		return new Swizzle_XY0(xy);
	}

	public static Swizzle_X0Y X_Y(this IInputNode<float2> xy)
	{
		return new Swizzle_X0Y(xy);
	}

	public static Swizzle_0XY _XY(this IInputNode<float2> xy)
	{
		return new Swizzle_0XY(xy);
	}

	public static Swizzle_XZY XZY(this IInputNode<float3> xyz)
	{
		return new Swizzle_XZY(xyz);
	}

	public static Swizzle_YXZ YXZ(this IInputNode<float3> xyz)
	{
		return new Swizzle_YXZ(xyz);
	}

	public static Swizzle_ZYX ZYX(this IInputNode<float3> xyz)
	{
		return new Swizzle_ZYX(xyz);
	}

	public static Swizzle_ZXY ZXY(this IInputNode<float3> xyz)
	{
		return new Swizzle_ZXY(xyz);
	}

	public static InputValueGate<T> Gate<T>(this IInputNode<T> node, IInputNode<bool> gate, bool evaluationGate = false, bool lastKeepsOpen = false) where T : struct
	{
		return new InputValueGate<T>(node, gate, evaluationGate, lastKeepsOpen);
	}

	public static InputValueGate<T> Gate<T>(this IInputNode<T> node, bool evaluationGate = false, bool lastKeepsOpen = false, params IInputNode<bool>[] gates) where T : struct
	{
		return new InputValueGate<T>(node, evaluationGate, lastKeepsOpen, gates);
	}

	public static ScaleInput<T> Gate<T>(this IInputNode<T> node, IInputNode<float> amplitude) where T : struct
	{
		return new ScaleInput<T>(node, amplitude);
	}

	public static RemapInput<T> Remap<T>(this IInputNode<T> node, T outMin, T outMax, T? inMin = null, T? inMax = null) where T : struct
	{
		RemapInput<T> remap = new RemapInput<T>();
		remap.Value = node;
		remap.OutputMin = outMin;
		remap.OutputMax = outMax;
		if (inMin.HasValue)
		{
			remap.InputMin = inMin.Value;
		}
		if (inMax.HasValue)
		{
			remap.InputMin = inMax.Value;
		}
		return remap;
	}

	public static InvertDigitalInput Invert(this IInputNode<bool> node)
	{
		return new InvertDigitalInput(node);
	}

	public static NegateAnalogInput<T> Negate<T>(this IInputNode<T> node) where T : struct
	{
		return new NegateAnalogInput<T>(node);
	}

	public static ScaleInput<T> Multiply<T>(this IInputNode<T> node, IInputNode<float> amplitude) where T : struct
	{
		return new ScaleInput<T>(node, amplitude);
	}

	public static MultiplyInput<T> Multiply<T>(this IInputNode<T> a, IInputNode<T> b) where T : struct
	{
		return new MultiplyInput<T>(a, b);
	}

	public static MultiplyInputByConstant<T> Multiply<T>(this IInputNode<T> node, T constant) where T : struct
	{
		return new MultiplyInputByConstant<T>(node, constant);
	}

	public static PowerInput<T> Pow<T>(this IInputNode<T> node, IInputNode<float> power) where T : struct
	{
		return new PowerInput<T>(node, power);
	}

	public static AnalogToDigital ToDigital(this IInputNode<float> node, float activation = 0.6f, float deactivation = 0.4f)
	{
		return new AnalogToDigital(node, activation, deactivation);
	}

	public static DigitalToAnalog ToAnalog(this IInputNode<bool> node, float transitionTime = 4f, CurvePreset curve = CurvePreset.Smooth)
	{
		return new DigitalToAnalog(node, transitionTime, transitionTime, curve);
	}

	public static TapToggle TapToggle(this IInputNode<bool> node, float interval = 0.2f)
	{
		return new TapToggle(node, interval);
	}

	public static MultiTapInput MultiTap(this IInputNode<bool> node, int tapCount = 2, float interval = 0.25f, bool ignoreNull = false)
	{
		return new MultiTapInput(node, toggle: false, tapCount, interval, ignoreNull);
	}

	public static MultiTapInput MultiTapToggle(this IInputNode<bool> node, int tapCount = 2, float interval = 0.25f)
	{
		return new MultiTapInput(node, toggle: true, tapCount, interval);
	}

	public static MultiTapInput MultiTap(IEnumerable<IInputNode<bool>> nodes, int tapCount = 2, float interval = 0.25f)
	{
		return new MultiTapInput(nodes, toggle: false, tapCount, interval);
	}

	public static MultiTapInput MultiTapToggle(IEnumerable<IInputNode<bool>> nodes, int tapCount = 2, float interval = 0.25f)
	{
		return new MultiTapInput(nodes, toggle: true, tapCount, interval);
	}

	public static ToggleInput Toggle(this IInputNode<bool> node, IInputNode<bool> reset = null)
	{
		return new ToggleInput(node, reset);
	}

	public static KeyboardInputSource Key(Key key)
	{
		return new KeyboardInputSource(key);
	}

	public static KeyboardInputSource KeyRepeat(Key key)
	{
		return new KeyboardInputSource(key, !key.IsModifier(), repeatPress: true);
	}

	public static MouseButtonInputSource MouseButton(MouseButton button)
	{
		return new MouseButtonInputSource(button);
	}

	public static ImplicitDeviceDigitalSource Digital(string name)
	{
		return new ImplicitDeviceDigitalSource(name);
	}

	public static ImplicitDeviceAnalogSource Analog(string name)
	{
		return new ImplicitDeviceAnalogSource(name);
	}

	public static ImplicitDeviceAnalog2DSource Analog2D(string name)
	{
		return new ImplicitDeviceAnalog2DSource(name);
	}

	public static ImplicitDeviceAnalog3DSource Analog3D(string name)
	{
		return new ImplicitDeviceAnalog3DSource(name);
	}

	public static ImplicitDeviceDigitalSource Digital(Digital property)
	{
		return new ImplicitDeviceDigitalSource(property.Name);
	}

	public static ImplicitDeviceAnalogSource Analog(Analog property)
	{
		return new ImplicitDeviceAnalogSource(property.Name);
	}

	public static ImplicitDeviceAnalog2DSource Analog2D(Analog2D property)
	{
		return new ImplicitDeviceAnalog2DSource(property.Name);
	}

	public static ImplicitDeviceAnalog3DSource Analog3D(Analog3D property)
	{
		return new ImplicitDeviceAnalog3DSource(property.Name);
	}

	public static ControllerDigitalSource Digital(Chirality side, string name, bool ignoreBlocks = false)
	{
		return new ControllerDigitalSource(side, name, ignoreBlocks);
	}

	public static ControllerAnalogSource Analog(Chirality side, string name, bool ignoreBlocks = false)
	{
		return new ControllerAnalogSource(side, name, ignoreBlocks);
	}

	public static ControllerAnalog2DSource Analog2D(Chirality side, string name, bool ignoreBlocks = false)
	{
		return new ControllerAnalog2DSource(side, name, ignoreBlocks);
	}

	public static ControllerAnalog3DSource Analog3D(Chirality side, string name, bool ignoreBlocks = false)
	{
		return new ControllerAnalog3DSource(side, name, ignoreBlocks);
	}

	public static VR_SingleLocomotionTurn LocomotionTurn(this IInputNode<float> node)
	{
		return new VR_SingleLocomotionTurn(node);
	}

	public static LeftRightSelector<T> Primary<T>(IInputNode<T> left, IInputNode<T> right) where T : struct
	{
		return new LeftRightSelector<T>(primary: true, left, right);
	}

	public static LeftRightSelector<T> Secondary<T>(IInputNode<T> left, IInputNode<T> right) where T : struct
	{
		return new LeftRightSelector<T>(primary: false, left, right);
	}

	public static PrimarySecondarySelector<T> PrimarySecondary<T>(IInputNode<T> primary, IInputNode<T> secondary) where T : struct
	{
		return new PrimarySecondarySelector<T>(primary, secondary);
	}

	public static MouseMovementSource MouseMovement(bool normalized)
	{
		return new MouseMovementSource(normalized);
	}

	public static MouseScrollSource MouseScroll(bool normalized)
	{
		return new MouseScrollSource(normalized);
	}

	public static CursorLockSource CursorLocked()
	{
		return new CursorLockSource();
	}

	public static SettingSource<T, S> Setting<T, S>(Func<S, T> getter, T defaultValue = default(T)) where T : struct where S : SettingComponent<S>, new()
	{
		return new SettingSource<T, S>(getter, defaultValue);
	}

	public static AllInputs All(params IInputNode<bool>[] inputs)
	{
		return new AllInputs(inputs);
	}

	public static AnyInput Any(params IInputNode<bool>[] inputs)
	{
		return new AnyInput(inputs);
	}

	public static DigitalToAxis Axis(IInputNode<bool> up, IInputNode<bool> left, IInputNode<bool> down, IInputNode<bool> right)
	{
		return new DigitalToAxis
		{
			Left = left,
			Right = right,
			Up = up,
			Down = down
		};
	}

	public static GroupActionInput<T> GroupAction<T>(string name) where T : struct
	{
		return new GroupActionInput<T>(name);
	}

	public static Conditional<T> Conditional<T>(IInputNode<T> onTrue, IInputNode<T> onFalse, IInputNode<bool> condition) where T : struct
	{
		return new Conditional<T>(onTrue, onFalse, condition);
	}
}
