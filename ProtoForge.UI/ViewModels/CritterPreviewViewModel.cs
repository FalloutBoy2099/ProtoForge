namespace ProtoForge.UI.ViewModels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows.Media;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Frm;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Infrastructure.Workspace;
	using Services;

	/// <summary>A choice of the preview (a weapon, an animation): the letter the art files use and its name.</summary>
	public sealed record PreviewChoice(Char Letter, String Label);

	/// <summary>One of the six directions of the animation.</summary>
	public sealed record DirectionPreview(String Title, IReadOnlyList<ImageSource>? Frames, Int32 FramesPerSecond);

	/// <summary>The six directions of a critter animated side by side, for any weapon and animation its art files have.</summary>
	public sealed partial class CritterPreviewViewModel : ObservableObject
	{
		/// <summary>The weapon letters of the art files (<c>name</c> + weapon + animation); 'b' is used for the death animations.</summary>
		private static readonly Dictionary<Char, String> WeaponNames = new()
		{
			['a'] = "preview.weapon.a",
			['d'] = "preview.weapon.d",
			['e'] = "preview.weapon.e",
			['f'] = "preview.weapon.f",
			['g'] = "preview.weapon.g",
			['h'] = "preview.weapon.h",
			['i'] = "preview.weapon.i",
			['j'] = "preview.weapon.j",
			['k'] = "preview.weapon.k",
			['l'] = "preview.weapon.l",
			['m'] = "preview.weapon.m",
		};

		private readonly GameWorkspace _workspace;
		private readonly ArtImageService _art;
		private Int32 _index = -1;
		private Dictionary<Char, SortedSet<Char>> _available = [];
		private Boolean _loading;
		private Int32 _version;

		[ObservableProperty]
		private Boolean _isActive;

		[ObservableProperty]
		private IReadOnlyList<PreviewChoice> _weapons = [];

		[ObservableProperty]
		private PreviewChoice? _selectedWeapon;

		[ObservableProperty]
		private IReadOnlyList<PreviewChoice> _animations = [];

		[ObservableProperty]
		private PreviewChoice? _selectedAnimation;

		[ObservableProperty]
		private IReadOnlyList<DirectionPreview> _directions = [];

		[ObservableProperty]
		private String _message = String.Empty;

		public CritterPreviewViewModel(GameWorkspace workspace, ArtImageService art)
		{
			_workspace = workspace;
			_art = art;
		}

		/// <summary>The critter to show (null for none, or for something that is not a critter).</summary>
		public void Load(ProtoEntry? entry)
		{
			_index = -1;
			_available = [];
			if (entry is { Kind: ProtoKind.Critter, File: { } file } && file.TryGet(FieldKeys.FrmId) is { } value && value != -1 && new FrameId(value).Type == ArtType.Critter)
			{
				_index = new FrameId(value).Index;
			}

			if (IsActive)
			{
				Rebuild();
			}
			else
			{
				Directions = [];
			}
		}

		partial void OnIsActiveChanged(Boolean value)
		{
			if (value)
			{
				Rebuild();
			}
		}

		partial void OnSelectedWeaponChanged(PreviewChoice? value)
		{
			if (_loading || value is null)
			{
				return;
			}

			_loading = true;
			try
			{
				FillAnimations(value.Letter);
			}
			finally
			{
				_loading = false;
			}

			_ = ShowAsync();
		}

		partial void OnSelectedAnimationChanged(PreviewChoice? value)
		{
			if (!_loading)
			{
				_ = ShowAsync();
			}
		}

		/// <summary>Reads which weapons and animations the art files of the critter have, then shows the first.</summary>
		private void Rebuild()
		{
			_available = [];
			Message = String.Empty;
			if (_index < 0)
			{
				Weapons = Animations = [];
				SelectedWeapon = null;
				SelectedAnimation = null;
				Directions = [];
				Message = Loc.Get("preview.none");
				return;
			}

			String? name = _workspace.Art.GetList(ArtType.Critter)?.TryGetName(_index);
			if (String.IsNullOrEmpty(name))
			{
				Weapons = Animations = [];
				Directions = [];
				Message = Loc.Get("preview.none");
				return;
			}

			String prefix = $"art/critters/{name.ToLowerInvariant()}";
			foreach (String path in _workspace.Files.Enumerate("art/critters"))
			{
				String file = path.ToLowerInvariant();
				if (file.StartsWith(prefix, StringComparison.Ordinal) && file.EndsWith(".frm", StringComparison.Ordinal) && file.Length == prefix.Length + 6)
				{
					Char weapon = file[prefix.Length];
					Char animation = file[prefix.Length + 1];
					if (weapon != 'b')
					{
						(_available.TryGetValue(weapon, out SortedSet<Char>? set) ? set : _available[weapon] = []).Add(animation);
					}
				}
			}

			_loading = true;
			try
			{
				Weapons = [.. _available.Keys.Order().Select(l => new PreviewChoice(l, WeaponLabel(l)))];
				SelectedWeapon = Weapons.FirstOrDefault(w => w.Letter == 'a') ?? Weapons.FirstOrDefault();
				FillAnimations(SelectedWeapon?.Letter);
			}
			finally
			{
				_loading = false;
			}

			_ = ShowAsync();
		}

		private void FillAnimations(Char? weapon)
		{
			Animations = weapon is { } letter && _available.TryGetValue(letter, out SortedSet<Char>? set)
				? [.. set.Select(a => new PreviewChoice(a, AnimationLabel(a)))]
				: [];
			SelectedAnimation = Animations.FirstOrDefault(a => a.Letter == 'a') ?? Animations.FirstOrDefault();
		}

		private static String WeaponLabel(Char letter)
		{
			return WeaponNames.TryGetValue(letter, out String? key) ? Loc.Get(key) : Loc.Format("preview.weapon.other", Char.ToUpperInvariant(letter));
		}

		private static String AnimationLabel(Char letter)
		{
			return letter switch
			{
				'a' => Loc.Get("preview.anim.a"),
				'b' => Loc.Get("preview.anim.b"),
				_ => Loc.Format("preview.anim.other", Char.ToUpperInvariant(letter)),
			};
		}

		private async Task ShowAsync()
		{
			Int32 version = ++_version;
			if (_index < 0 || SelectedWeapon is not { } weapon || SelectedAnimation is not { } animation)
			{
				Directions = [];
				return;
			}

			FrameId fid = FrameId.Create(ArtType.Critter, _index, animation.Letter - 'a', weapon.Letter - 'a');
			ArtAnimation?[] loaded = await Task.WhenAll(Enumerable.Range(0, 6).Select(async direction =>
			{
				try
				{
					return await _art.GetAnimationAsync(fid, direction);
				}
				catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or System.IO.InvalidDataException)
				{
					return null;
				}
			}));
			if (version != _version)
			{
				return;
			}

			Directions =
			[
				.. loaded.Select((a, i) => new DirectionPreview(Loc.Get("preview.dir" + i), a?.Frames.Cast<ImageSource>().ToList(), a?.FramesPerSecond ?? 10)),
			];
		}
	}
}