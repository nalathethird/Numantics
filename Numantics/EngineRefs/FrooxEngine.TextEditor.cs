// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.TextEditor
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

[Category(new string[] { "Common UI/Editors" })]
public class TextEditor : Component, IFocusable, IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	public enum FinishAction
	{
		LeaveAsIs,
		NullOnEmpty,
		NullOnWhitespace
	}

	public readonly SyncRef<IText> Text;

	public readonly Sync<bool> Undo;

	public readonly Sync<string> UndoDescription;

	public readonly Sync<FinishAction> FinishHandling;

	[DefaultValue(true)]
	public readonly Sync<bool> AutoCaretColorField;

	public readonly Sync<colorX> CaretColorField;

	public readonly Sync<colorX> SelectionColorField;

	public readonly SyncDelegate<Action<TextEditor>> EditingStarted;

	public readonly SyncDelegate<Action<TextEditor>> EditingChanged;

	public readonly SyncDelegate<Action<TextEditor>> EditingFinished;

	public readonly SyncDelegate<Action<TextEditor>> SubmitPressed;

	private bool _editingActive;

	private Coroutine _editingCoroutine;

	public bool AutoCaretColor
	{
		get
		{
			return AutoCaretColorField;
		}
		set
		{
			AutoCaretColorField.Value = value;
		}
	}

	public colorX CaretColor
	{
		get
		{
			return CaretColorField;
		}
		set
		{
			CaretColorField.Value = value;
		}
	}

	public colorX SelectionColor
	{
		get
		{
			return SelectionColorField;
		}
		set
		{
			SelectionColorField.Value = value;
		}
	}

	public colorX ActualCaretColor
	{
		get
		{
			if (!AutoCaretColor)
			{
				return CaretColor;
			}
			return Text.Target?.Color ?? colorX.Clear;
		}
	}

	public string TargetString
	{
		get
		{
			return Text.Target?.Text ?? "";
		}
		set
		{
			if (Undo.Value)
			{
				Text.Target.UndoableSet(value, UndoDescription);
			}
			else
			{
				Text.Target.Text = value;
			}
		}
	}

	public User EditingUser => this.GetFocusingUser();

	public bool IsEditing => EditingUser != null;

	public bool EditEventRunning { get; private set; }

	private int CaretPosition
	{
		get
		{
			return MathX.Clamp(Text.Target?.CaretPosition ?? (-1), -1, TargetString.Length + 1);
		}
		set
		{
			value = MathX.Clamp(value, 0, TargetString.Length + 1);
			if (value > 0 && value < TargetString.Length && char.GetUnicodeCategory(TargetString, value) == UnicodeCategory.Surrogate)
			{
				value += MathX.Sign(value - Text.Target.CaretPosition);
			}
			Text.Target.CaretPosition = value;
		}
	}

	private int SelectionStart
	{
		get
		{
			return MathX.Clamp(Text.Target.SelectionStart, -1, TargetString.Length + 1);
		}
		set
		{
			Text.Target.SelectionStart = MathX.Clamp(value, 0, TargetString.Length + 1);
		}
	}

	private int SelectionLength
	{
		get
		{
			if (!HasSelection)
			{
				return 0;
			}
			return MathX.Abs(CaretPosition - SelectionStart);
		}
	}

	private bool HasSelection
	{
		get
		{
			return SelectionStart != -1;
		}
		set
		{
			Text.Target.SelectionStart = (value ? CaretPosition : (-1));
		}
	}

	public event Action<TextEditor> LocalEditingStarted;

	public event Action<TextEditor> LocalEditingChanged;

	public event Action<TextEditor> LocalEditingFinished;

	public event Action<TextEditor> LocalSubmitPressed;

	public void Focus(User user)
	{
		if (user.IsLocalUser && Text.Target != null)
		{
			EnsureKeyboard();
			if (!_editingCoroutine.IsNull)
			{
				_editingCoroutine.Stop();
			}
			_editingActive = true;
			_editingCoroutine = StartCoroutine(EditCoroutine());
		}
	}

	public void Defocus(User user)
	{
		if (!user.IsLocalUser || !_editingActive)
		{
			return;
		}
		_editingActive = false;
		if (!_editingCoroutine.IsNull)
		{
			_editingCoroutine.Stop();
		}
		Text.Target.CaretPosition = -1;
		Text.Target.SelectionStart = -1;
		base.InputInterface.HideKeyboard(this);
		try
		{
			EditEventRunning = true;
			OnFinished();
			base.Slot.ForeachComponent(delegate(ITextEditorEventReceiver r)
			{
				r.EditingFinished(this);
			});
			EditingFinished.Target?.Invoke(this);
			this.LocalEditingFinished?.Invoke(this);
		}
		finally
		{
			EditEventRunning = false;
		}
	}

	public void EnsureKeyboard()
	{
		base.LocalUser.GetPointInFrontOfUser(out var point, out var rotation, float3.Backward, float3.Down * 0.2f);
		point = WorldManager.TransferPoint(point, base.World, Userspace.UserspaceWorld);
		rotation = WorldManager.TransferRotation(rotation, base.World, Userspace.UserspaceWorld);
		base.InputInterface.ShowKeyboard(Text.Target, TargetString, KeyboardType.Default, autocorrection: true, multiline: false, !string.IsNullOrEmpty(Text.Target.MaskPattern), "", 0, this, point, rotation);
	}

	private void DeleteSelection()
	{
		int start = MathX.Min(SelectionStart, CaretPosition);
		if (SelectionLength > 0)
		{
			string str = TargetString;
			TargetString = str.Remove(start, SelectionLength);
		}
		HasSelection = false;
		CaretPosition = start;
	}

	private string GetSelection()
	{
		string targetString = TargetString;
		int start = MathX.Min(SelectionStart, CaretPosition);
		return targetString.Substring(start, SelectionLength);
	}

	private void Delete()
	{
		if (HasSelection)
		{
			DeleteSelection();
		}
		else if (CaretPosition < TargetString.Length && TargetString.Length > 0)
		{
			TargetString = TargetString.Remove(CaretPosition, (!char.IsHighSurrogate(TargetString, CaretPosition)) ? 1 : 2);
		}
	}

	private void Backspace()
	{
		if (HasSelection)
		{
			DeleteSelection();
		}
		else
		{
			if (CaretPosition <= 0 || TargetString.Length <= 0)
			{
				return;
			}
			int caretPosition = CaretPosition;
			if (base.InputInterface.GetKey(Key.Control))
			{
				if (base.InputInterface.GetKey(Key.Shift))
				{
					CaretPosition = GetPreviousLineStart(CaretPosition);
				}
				else
				{
					CaretPosition = GetWordStart(CaretPosition);
				}
			}
			else
			{
				CaretPosition--;
			}
			int count = caretPosition - CaretPosition;
			for (int i = 0; i < count; i++)
			{
				Delete();
			}
		}
	}

	private void Insert(string str)
	{
		if (HasSelection)
		{
			DeleteSelection();
		}
		TargetString = TargetString.Substring(0, CaretPosition) + str + TargetString.Substring(CaretPosition, TargetString.Length - CaretPosition);
		CaretPosition += str.Length;
	}

	public void SelectAll()
	{
		if (!_editingActive)
		{
			throw new Exception("TextEditor isn't currently in editing state for local user.");
		}
		SelectionStart = 0;
		CaretPosition = TargetString.Length;
	}

	public int GetPositionOnNextLine()
	{
		int currentLineStart = GetLineStart(CaretPosition);
		int currentLinePos = CaretPosition - currentLineStart;
		int nextLineStart = GetNextLineStart(CaretPosition);
		int nextLineLength = GetLineLength(nextLineStart);
		return nextLineStart + MathX.Min(nextLineLength, currentLinePos);
	}

	public int GetPositionOnPreviousLine()
	{
		int currentLineStart = GetLineStart(CaretPosition);
		int currentLinePos = CaretPosition - currentLineStart;
		int prevLineStart = GetPreviousLineStart(CaretPosition);
		int prevLineLength = GetLineLength(prevLineStart);
		return prevLineStart + MathX.Min(prevLineLength, currentLinePos);
	}

	public int GetLineStart(int index)
	{
		return TargetString.GetLineStart(index);
	}

	public int GetNextLineStart(int index)
	{
		return TargetString.GetNextLineStart(index);
	}

	public int GetWordStart(int index)
	{
		return TargetString.GetPreviousWordBoundary(index);
	}

	public int GetNextWord(int index)
	{
		return TargetString.GetNextWordBoundary(index);
	}

	public int GetLineEnd(int index)
	{
		return MathX.Max(0, GetNextLineStart(index) - 1);
	}

	public int GetLineLength(int index)
	{
		return MathX.Max(0, GetNextLineStart(index) - GetLineStart(index) - 1);
	}

	public int GetPreviousLineStart(int index)
	{
		int lineStart = GetLineStart(index);
		if (lineStart == 0)
		{
			return 0;
		}
		return GetLineStart(lineStart - 1);
	}

	public void MoveCaret(int newPosition, bool selectModifier)
	{
		int prevPosition = CaretPosition;
		CaretPosition = newPosition;
		if (newPosition != prevPosition)
		{
			if (selectModifier && SelectionStart == -1)
			{
				SelectionStart = prevPosition;
			}
			if (!selectModifier)
			{
				HasSelection = false;
			}
		}
		else if (HasSelection != selectModifier)
		{
			HasSelection = selectModifier;
		}
	}

	private IEnumerator<Context> EditCoroutine()
	{
		try
		{
			EditEventRunning = true;
			base.Slot.ForeachComponent(delegate(ITextEditorEventReceiver r)
			{
				r.EditingStarted(this);
			});
			EditingStarted.Target?.Invoke(this);
			this.LocalEditingStarted?.Invoke(this);
		}
		finally
		{
			EditEventRunning = false;
		}
		Text.Target.CaretColor = ActualCaretColor;
		Text.Target.CaretPosition = TargetString.Length;
		Text.Target.SelectionColor = SelectionColor;
		bool caretVisible = true;
		float timeElapsed = 0f;
		while (_editingActive)
		{
			yield return Context.WaitForNextUpdate();
			if (base.InputInterface.GetKeyUp(Key.Escape))
			{
				this.Defocus();
				break;
			}
			int originalCaretPosition = CaretPosition;
			bool stringChanged = false;
			bool keyProcessed = false;
			if (base.InputInterface.GetKey(Key.Control))
			{
				if (base.InputInterface.GetKeyRepeat(Key.A))
				{
					SelectAll();
					keyProcessed = true;
				}
				if (base.InputInterface.IsClipboardSupported)
				{
					if (base.InputInterface.GetKeyRepeat(Key.V))
					{
						if (base.InputInterface.Clipboard.ContainsText)
						{
							Insert(base.InputInterface.Clipboard.GetText().Result);
							stringChanged = true;
						}
						keyProcessed = true;
					}
					if (base.InputInterface.GetKeyRepeat(Key.C) || base.InputInterface.GetKeyDown(Key.X))
					{
						if (SelectionLength > 0)
						{
							string text = GetSelection();
							bool num = base.InputInterface.IsKeyVirtualPressed(Key.Control) || base.InputInterface.IsKeyVirtualPressed(Key.C) || base.InputInterface.IsKeyVirtualPressed(Key.X);
							bool process = true;
							if (num && IsPath(text) && (File.Exists(text) || Directory.Exists(text)))
							{
								process = false;
							}
							if (process)
							{
								base.InputInterface.Clipboard.SetText(text);
								if (base.InputInterface.GetKeyDown(Key.X))
								{
									DeleteSelection();
									stringChanged = true;
								}
							}
						}
						keyProcessed = true;
					}
				}
			}
			if (!keyProcessed)
			{
				bool selectModifier = base.InputInterface.GetKey(Key.Shift);
				bool wordModifier = base.InputInterface.GetKey(Key.Control);
				if (base.InputInterface.GetKeyRepeat(Key.UpArrow))
				{
					MoveCaret(GetPositionOnPreviousLine(), selectModifier);
				}
				if (base.InputInterface.GetKeyRepeat(Key.DownArrow))
				{
					MoveCaret(GetPositionOnNextLine(), selectModifier);
				}
				if (base.InputInterface.GetKeyRepeat(Key.LeftArrow))
				{
					if (HasSelection && !selectModifier)
					{
						MoveCaret(MathX.Min(CaretPosition, SelectionStart), selectModifier);
					}
					else if (wordModifier)
					{
						MoveCaret(GetWordStart(CaretPosition), selectModifier);
					}
					else
					{
						MoveCaret(CaretPosition - 1, selectModifier);
					}
				}
				if (base.InputInterface.GetKeyRepeat(Key.RightArrow))
				{
					if (HasSelection && !selectModifier)
					{
						MoveCaret(MathX.Max(CaretPosition, SelectionStart), selectModifier);
					}
					else if (wordModifier)
					{
						MoveCaret(GetNextWord(CaretPosition), selectModifier);
					}
					else
					{
						MoveCaret(CaretPosition + 1, selectModifier);
					}
				}
				if (base.InputInterface.GetKeyRepeat(Key.Home))
				{
					MoveCaret(GetLineStart(CaretPosition), selectModifier);
				}
				if (base.InputInterface.GetKeyRepeat(Key.End))
				{
					MoveCaret(GetLineEnd(CaretPosition), selectModifier);
				}
				if (base.InputInterface.GetKeyRepeat(Key.Delete))
				{
					int count = 1;
					if (base.InputInterface.GetKey(Key.Control))
					{
						count = ((!base.InputInterface.GetKey(Key.Shift)) ? (GetNextWord(CaretPosition) - CaretPosition) : (GetNextLineStart(CaretPosition) - CaretPosition));
					}
					for (int i = 0; i < count; i++)
					{
						Delete();
					}
					stringChanged = true;
				}
				if (base.InputInterface.TypeDelta.Length > 0)
				{
					stringChanged = true;
					string typeDelta = base.InputInterface.TypeDelta;
					for (int num2 = 0; num2 < typeDelta.Length; num2++)
					{
						char ch = typeDelta[num2];
						if (ch == '\n' || ch == '\r')
						{
							if (!base.InputInterface.GetKey(Key.Shift))
							{
								this.Defocus();
								SubmitPressed.Target?.Invoke(this);
								this.LocalSubmitPressed?.Invoke(this);
								break;
							}
							Insert("\n");
						}
						else if (ch == '\b' || base.InputInterface.GetKeyRepeat(Key.Backspace))
						{
							Backspace();
						}
						else if (!char.IsControl(ch))
						{
							Insert(ch.ToString());
						}
					}
				}
			}
			if (stringChanged)
			{
				try
				{
					EditEventRunning = true;
					EditingChanged.Target?.Invoke(this);
					base.Slot.ForeachComponent(delegate(ITextEditorEventReceiver r)
					{
						r.EditingChanged(this);
					});
					this.LocalEditingChanged?.Invoke(this);
				}
				finally
				{
					EditEventRunning = false;
				}
			}
			if (originalCaretPosition != CaretPosition)
			{
				caretVisible = true;
				Text.Target.CaretColor = ActualCaretColor;
				timeElapsed = 0f;
				continue;
			}
			timeElapsed += base.Time.Delta;
			if (timeElapsed >= 0.5f)
			{
				timeElapsed -= 0.5f;
				caretVisible = !caretVisible;
				Text.Target.CaretColor = new colorX(ActualCaretColor.rgb, caretVisible ? ActualCaretColor.a : 0f);
			}
		}
		static bool IsPath(string path)
		{
			try
			{
				return Path.IsPathRooted(path);
			}
			catch (ArgumentException)
			{
				return false;
			}
		}
	}

	public void ForceEditingChangedEvent()
	{
		base.Slot.ForeachComponent(delegate(ITextEditorEventReceiver r)
		{
			r.EditingChanged(this);
		});
		EditingChanged.Target?.Invoke(this);
		this.LocalEditingChanged?.Invoke(this);
		if (!IsEditing)
		{
			OnFinished();
			base.Slot.ForeachComponent(delegate(ITextEditorEventReceiver r)
			{
				r.EditingFinished(this);
			});
			EditingFinished.Target?.Invoke(this);
			this.LocalEditingFinished?.Invoke(this);
		}
	}

	private void OnFinished()
	{
		switch (FinishHandling.Value)
		{
		case FinishAction.NullOnEmpty:
			if (string.IsNullOrEmpty(TargetString))
			{
				TargetString = null;
			}
			break;
		case FinishAction.NullOnWhitespace:
			if (string.IsNullOrWhiteSpace(TargetString))
			{
				TargetString = null;
			}
			break;
		}
	}

	protected override void OnAwake()
	{
		CaretColor = colorX.Black;
		SelectionColor = new colorX(0f, 0.5f, 0.2f, 0.5f);
	}

	protected override void InitializeSyncMembers()
	{
		base.InitializeSyncMembers();
		Text = new SyncRef<IText>();
		Undo = new Sync<bool>();
		UndoDescription = new Sync<string>();
		FinishHandling = new Sync<FinishAction>();
		AutoCaretColorField = new Sync<bool>();
		CaretColorField = new Sync<colorX>();
		SelectionColorField = new Sync<colorX>();
		EditingStarted = new SyncDelegate<Action<TextEditor>>();
		EditingChanged = new SyncDelegate<Action<TextEditor>>();
		EditingFinished = new SyncDelegate<Action<TextEditor>>();
		SubmitPressed = new SyncDelegate<Action<TextEditor>>();
	}

	public override ISyncMember GetSyncMember(int index)
	{
		return index switch
		{
			0 => persistent, 
			1 => updateOrder, 
			2 => EnabledField, 
			3 => Text, 
			4 => Undo, 
			5 => UndoDescription, 
			6 => FinishHandling, 
			7 => AutoCaretColorField, 
			8 => CaretColorField, 
			9 => SelectionColorField, 
			10 => EditingStarted, 
			11 => EditingChanged, 
			12 => EditingFinished, 
			13 => SubmitPressed, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	public static TextEditor __New()
	{
		return new TextEditor();
	}
}
