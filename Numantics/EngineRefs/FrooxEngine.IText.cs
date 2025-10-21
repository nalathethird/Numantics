// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.IText
using Elements.Core;
using FrooxEngine;

public interface IText : IComponent, IComponentBase, IDestroyable, IWorker, IWorldElement, IUpdatable, IChangeable, IAudioUpdatable, IInitializable, ILinkable
{
	string Text { get; set; }

	colorX Color { get; set; }

	string MaskPattern { get; set; }

	IAssetProvider<FontSet> Font { get; set; }

	int CaretPosition { get; set; }

	int SelectionStart { get; set; }

	colorX CaretColor { get; set; }

	colorX SelectionColor { get; set; }

	void UndoableSet(string text, string description);
}
