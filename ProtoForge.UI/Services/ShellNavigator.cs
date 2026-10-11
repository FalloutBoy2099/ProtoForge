namespace ProtoForge.UI.Services
{
	using System;
	using System.Collections.Generic;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;

	/// <summary>
	/// Lets view models ask the shell to change page without referencing views: show a prototype in the library,
	/// open the compare page, jump to the messages editor...
	/// </summary>
	public sealed class ShellNavigator
	{
		public Action<Type>? NavigateHandler { get; set; }

		/// <summary>Lets other pages read what is currently selected in the library.</summary>
		public Func<IReadOnlyList<ProtoEntry>>? SelectionProvider { get; set; }

		public event EventHandler<ProtoId>? ProtoRequested;

		public event EventHandler<ProtoEntry>? VanillaCompareRequested;

		public event EventHandler<(ProtoEntry A, ProtoEntry B)>? PairCompareRequested;

		public event EventHandler<(String File, Int32 Id)>? MessageRequested;

		/// <summary>Raised when the keyboard focus must go to the prototype list (from the search box).</summary>
		public event EventHandler? FocusListRequested;

		public void Navigate<TPage>()
		{
			NavigateHandler?.Invoke(typeof(TPage));
		}

		public void ShowProto(ProtoId pid)
		{
			ProtoRequested?.Invoke(this, pid);
		}

		public void CompareWithVanilla(ProtoEntry entry)
		{
			VanillaCompareRequested?.Invoke(this, entry);
		}

		public void ComparePair(ProtoEntry a, ProtoEntry b)
		{
			PairCompareRequested?.Invoke(this, (a, b));
		}

		/// <summary>Raised when a user template was saved or deleted (the "New" menu must list it).</summary>
		public event EventHandler? TemplatesChanged;

		public void NotifyTemplatesChanged()
		{
			TemplatesChanged?.Invoke(this, EventArgs.Empty);
		}

		public void FocusList()
		{
			FocusListRequested?.Invoke(this, EventArgs.Empty);
		}

		public void ShowMessage(String file, Int32 id)
		{
			MessageRequested?.Invoke(this, (file, id));
		}
	}
}