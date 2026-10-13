namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Windows.Media;
	using Microsoft.Extensions.DependencyInjection;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using Services;

	public sealed record ArtOption(Int32 Index, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>A frame id: pick the art file by name, see it, and keep the other bits of the id untouched.</summary>
	public sealed partial class ArtFieldViewModel : FieldViewModel
	{
		private const Int32 None = -1;

		private Int32 _previewVersion;

		[ObservableProperty]
		private IReadOnlyList<ArtOption> _options = [];

		[ObservableProperty]
		private ArtOption? _selected;

		[ObservableProperty]
		private ImageSource? _preview;

		[ObservableProperty]
		private String _fidText = String.Empty;

		public ArtFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
			context.Workspace.ArtChanged += OnArtChanged;
		}

		public override void Detach()
		{
			Context.Workspace.ArtChanged -= OnArtChanged;
		}

		private void OnArtChanged(Object? sender, EventArgs e)
		{
			// An image was added: the list of names must be read again.
			Options = [];
			Refresh();
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			ArtType type = Field.ArtType ?? ArtType.Item;
			if (Options.Count == 0)
			{
				List<ArtOption> options = [new ArtOption(None, Loc.Get("field.none"))];
				IReadOnlyList<String> names = Context.Workspace.Art.GetNames(type);
				for (Int32 i = 0; i < names.Count; i++)
				{
					options.Add(new ArtOption(i, $"{i}: {names[i]}"));
				}

				Options = options;
			}

			if (values.Count == 0 || IsMixed)
			{
				Selected = null;
				FidText = IsMixed ? Loc.Get("field.mixed") : String.Empty;
				Preview = null;
				_previewVersion++;
				return;
			}

			Int32 value = values[0];
			FidText = "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
			if (value == None)
			{
				Selected = Options[0];
				Preview = null;
				_previewVersion++;
				return;
			}

			FrameId fid = new(value);
			Selected = fid.Index < Options.Count - 1 ? Options[fid.Index + 1] : null;
			LoadPreview(fid);
		}

		partial void OnSelectedChanged(ArtOption? value)
		{
			if (IsRefreshing || value is null)
			{
				return;
			}

			ArtType type = Field.ArtType ?? ArtType.Item;
			if (value.Index == None)
			{
				Commit(None);
				return;
			}

			Modify(current => current == None ? FrameId.Create(type, value.Index).Value : (current & ~0xFFF) | value.Index);
		}

		/// <summary>The art folders a dropped image can be added to (critters and heads use naming rules of their own).</summary>
		private static readonly HashSet<ArtType> DroppableTypes = [ArtType.Item, ArtType.Inventory, ArtType.Scenery, ArtType.Wall, ArtType.Tile, ArtType.Misc];

		/// <summary>True when an image dropped on the field can be added to the game and assigned.</summary>
		public Boolean CanImport
		{
			get { return !IsReadOnly && IsEnabled && Entries.Count > 0 && DroppableTypes.Contains(Field.ArtType ?? ArtType.Item); }
		}

		/// <summary>
		/// Adds a dropped FRM (or BMP, converted with the palette of the game) to the art of the game and assigns it to the selection.
		/// An image whose name already exists is not replaced: the existing one is used.
		/// </summary>
		public void Import(String path)
		{
			INotifier notifier = App.Services.GetRequiredService<INotifier>();
			ArtType type = Field.ArtType ?? ArtType.Item;
			if (!CanImport)
			{
				notifier.Warning(Loc.Get("art.drop"), Loc.Get("art.dropUnsupported"));
				return;
			}

			if (!ArtImport.IsSupported(path))
			{
				notifier.Warning(Loc.Get("art.drop"), Loc.Get("art.dropInvalid"));
				return;
			}

			Byte[] frm;
			try
			{
				frm = ArtImport.ToFrm(path, System.IO.File.ReadAllBytes(path), Context.Workspace.Palette);
			}
			catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
			{
				notifier.Error(Loc.Get("art.drop"), ProtoForge.Core.UserTexts.Describe(ex));
				return;
			}

			String name = ArtImport.GetGameName(path);
			Int32 index = Context.Workspace.Art.GetList(type)?.IndexOf(name) ?? -1;
			try
			{
				if (index >= 0)
				{
					notifier.Info(Loc.Get("art.drop"), Loc.Format("art.dropReused", name));
				}
				else
				{
					index = Context.Workspace.AddArt(type, name, frm);
					notifier.Success(Loc.Get("art.drop"), Loc.Format("art.dropDone", name, index));
				}
			}
			catch (InvalidOperationException ex)
			{
				notifier.Error(Loc.Get("art.drop"), ProtoForge.Core.UserTexts.Describe(ex));
				return;
			}

			Int32 chosen = index;
			Modify(current => current == None ? FrameId.Create(type, chosen).Value : (current & ~0xFFF) | chosen);
		}

		private async void LoadPreview(FrameId fid)
		{
			Int32 version = ++_previewVersion;
			try
			{
				ImageSource? image = await Context.Art.GetAsync(fid);
				if (version == _previewVersion)
				{
					Preview = image;
				}
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
			{
				if (version == _previewVersion)
				{
					Preview = null;
				}
			}
		}
	}
}