namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Windows.Media;
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