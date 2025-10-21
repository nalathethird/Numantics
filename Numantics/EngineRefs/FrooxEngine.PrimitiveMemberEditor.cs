// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.PrimitiveMemberEditor
using System;
using System.Globalization;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

[InspectorHeader("Inspector.PrimitiveMemberEditor.Warning", 200)]
public class PrimitiveMemberEditor : MemberEditor
{
	public readonly Sync<string> Format;

	protected readonly SyncRef<TextEditor> _textEditor;

	protected readonly FieldDrive<string> _textDrive;

	protected readonly SyncRef<Button> _button;

	protected readonly SyncRef<Button> _resetButton;

	protected override void OnAwake()
	{
		base.OnAwake();
		_textDrive.SetupValueSetHook(TextChanged);
	}

	protected override void BuildUI(UIBuilder ui)
	{
		TextField textField = ui.TextField("", undo: false, null, parseRTF: false);
		textField.Text.NullContent.Value = MemberEditor.NULL_STRING;
		_textEditor.Target = textField.Editor.Target;
		_textDrive.Target = textField.Text.Content;
		_button.Target = textField.Slot.GetComponentInChildren<Button>();
		ui.Style.FlexibleWidth = -1f;
		ui.Style.MinWidth = 24f;
		Button resetButton = ui.Button((LocaleString)"∅");
		resetButton.Pressed.Target = OnReset;
		_resetButton.Target = resetButton;
		ui.Style.MinWidth = -1f;
		textField.Editor.Target.EditingStarted.Target = EditingStarted;
		textField.Editor.Target.EditingChanged.Target = EditingChanged;
		textField.Editor.Target.EditingFinished.Target = EditingFinished;
	}

	protected override void OnChanges()
	{
		base.OnChanges();
		if (base.Accessor == null)
		{
			return;
		}
		try
		{
			if (_textDrive.IsLinkValid)
			{
				_textDrive.Target.Value = PrimitiveToString(base.Accessor.TargetType, GetMemberValue());
			}
			_button.Target?.SetColors(base.FieldColor);
			if (_resetButton.Target != null)
			{
				_resetButton.Target.Slot.ActiveSelf = base.Accessor.TargetType == typeof(string);
			}
		}
		catch (Exception)
		{
			UniLog.Error("Exception in OnChanges on PrimitiveMemberEditor. Format: " + Format.Value + ", Path: " + _path.Value + ", Target: " + (_target.Target?.GetType())?.ToString() + " - " + _target.Target);
		}
	}

	[SyncMethod(typeof(Delegate), null)]
	private void EditingStarted(TextEditor editor)
	{
		if (base.Accessor != null)
		{
			if (PrimitiveToString(base.Accessor.TargetType, GetMemberValue()) == null)
			{
				_textEditor.Target.TargetString = "";
			}
			_textDrive.Target = null;
		}
	}

	[SyncMethod(typeof(Delegate), null)]
	private void EditingChanged(TextEditor editor)
	{
		if (Continuous.Value)
		{
			ParseAndAssign();
		}
	}

	private void TextChanged(IField<string> field, string value)
	{
		if (ParseAndAssign(value))
		{
			_textEditor.Target.Text.Target.Text = value;
		}
	}

	[SyncMethod(typeof(Delegate), null)]
	private void EditingFinished(TextEditor editor)
	{
		ParseAndAssign();
		_textDrive.Target = ((Text)_textEditor.Target.Text.Target).Content;
	}

	[SyncMethod(typeof(Delegate), null)]
	private void OnReset(IButton button, ButtonEventData eventData)
	{
		StructMemberAccessor accessor = base.Accessor;
		if (accessor != null)
		{
			SetMemberValue((accessor.TargetType ?? accessor.RootType).GetDefaultValue());
		}
	}

	private bool ParseAndAssign()
	{
		return ParseAndAssign(_textEditor.Target.Text.Target.Text);
	}

	private bool ParseAndAssign(string str)
	{
		try
		{
			StructMemberAccessor accessor = base.Accessor;
			if (accessor == null)
			{
				return false;
			}
			object result;
			bool parsed;
			if (accessor.TargetType == typeof(Type))
			{
				if (string.IsNullOrWhiteSpace(str))
				{
					result = null;
					parsed = true;
				}
				else
				{
					result = base.World.Types.ParseNiceType(str, allowAmbigious: true);
					parsed = result != null;
					if (result is Type type && !type.IsValidGenericType(validForInstantiation: false))
					{
						parsed = false;
						result = null;
					}
				}
			}
			else
			{
				parsed = PrimitiveTryParsers.GetParser(accessor.TargetType)(str, out result);
			}
			if (parsed)
			{
				SetMemberValue(result);
			}
			return parsed;
		}
		catch (Exception value)
		{
			UniLog.Error($"Exception assigning value to {_textDrive.Target}:\n{value}");
		}
		return false;
	}

	private string PrimitiveToString(Type primitiveType, object primitive)
	{
		if (primitive == null)
		{
			return null;
		}
		return primitiveType.Name switch
		{
			"Boolean" => ((bool)primitive).ToString(CultureInfo.InvariantCulture), 
			"SByte" => ((sbyte)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Int16" => ((short)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Int32" => ((int)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Int64" => ((long)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Byte" => ((byte)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"UInt16" => ((ushort)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"UInt32" => ((uint)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"UInt64" => ((ulong)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Single" => ((float)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Double" => ((double)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Decimal" => ((decimal)primitive).ToString(Format.Value, CultureInfo.InvariantCulture), 
			"Type" => (primitive as Type)?.GetNiceName(), 
			_ => primitive.ToString(), 
		};
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		Format = new Sync<string>();
		_textEditor = new SyncRef<TextEditor>();
		_textDrive = new FieldDrive<string>();
		_button = new SyncRef<Button>();
		_resetButton = new SyncRef<Button>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => Continuous, 
			4 => _path, 
			5 => _target, 
			6 => Format, 
			7 => _textEditor, 
			8 => _textDrive, 
			9 => _button, 
			10 => _resetButton, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static PrimitiveMemberEditor __New()
	{
		return new PrimitiveMemberEditor();
	}
}
