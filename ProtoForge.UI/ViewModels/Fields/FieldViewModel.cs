namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>What a field editor needs to read and write the selected prototypes.</summary>
	public sealed class EditorContext
	{
		public EditorContext(GameWorkspace workspace, ArtImageService art)
		{
			Workspace = workspace;
			Art = art;
		}

		public GameWorkspace Workspace { get; }

		public ArtImageService Art { get; }

		public IReadOnlyList<ProtoEntry> Entries { get; set; } = [];
	}

	/// <summary>
	/// Base class of every editable field. It shows the common value of the selected prototypes (or a "mixed" state) and
	/// writes changes through the workspace, so every edit is undoable and applies to the whole selection.
	/// </summary>
	public abstract partial class FieldViewModel : ObservableObject
	{
		private Boolean _suppress;

		[ObservableProperty]
		private Boolean _isMixed;

		[ObservableProperty]
		private Boolean _isEnabled = true;

		[ObservableProperty]
		private Boolean _isVisible = true;

		protected FieldViewModel(ProtoField field, EditorContext context)
		{
			Field = field;
			Context = context;
		}

		public ProtoField Field { get; }

		public EditorContext Context { get; }

		public String Key
		{
			get { return Field.Key; }
		}

		public String Label
		{
			get { return Loc.Field(Field.Key); }
		}

		public String Tooltip
		{
			get
			{
				String hint = Loc.Has("tip." + Field.Key) ? Loc.Get("tip." + Field.Key) + Environment.NewLine : String.Empty;
				return $"{hint}{Field.Key} - offset 0x{Field.Offset:X2}" + (Field.IsVirtual ? $" (bits 0x{Field.Mask:X})" : String.Empty);
			}
		}

		public Boolean IsReadOnly
		{
			get { return Field.IsReadOnly; }
		}

		public Boolean IsAdvanced
		{
			get { return Field.IsAdvanced; }
		}

		protected IReadOnlyList<ProtoEntry> Entries
		{
			get { return Context.Entries; }
		}

		protected Boolean IsRefreshing
		{
			get { return _suppress; }
		}

		/// <summary>Reloads the displayed value from the selected prototypes.</summary>
		public void Refresh()
		{
			_suppress = true;
			try
			{
				List<Int32> values = ReadValues();
				IsEnabled = values.Count > 0 && !IsReadOnly;
				IsMixed = values.Distinct().Count() > 1;
				Display(values);
			}
			finally
			{
				_suppress = false;
			}
		}

		/// <summary>Called by the editor once the field is no longer displayed.</summary>
		public virtual void Detach()
		{
		}

		protected abstract void Display(IReadOnlyList<Int32> values);

		protected void Commit(Int32 value)
		{
			if (_suppress || Entries.Count == 0)
			{
				return;
			}

			Context.Workspace.SetField(Entries, Field.Key, value);
		}

		protected void Modify(Func<Int32, Int32> transform)
		{
			if (_suppress || Entries.Count == 0)
			{
				return;
			}

			Context.Workspace.ModifyField(Entries, Field.Key, transform);
		}

		private List<Int32> ReadValues()
		{
			List<Int32> values = [];
			foreach (ProtoEntry entry in Entries)
			{
				ProtoField? field = entry.File?.Schema.Find(Field.Key);
				Int32? value = field is null ? null : entry.GetValue(field);
				if (value is not null)
				{
					values.Add(value.Value);
				}
			}

			return values;
		}
	}
}