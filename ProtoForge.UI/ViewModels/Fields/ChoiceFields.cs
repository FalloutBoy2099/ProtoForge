namespace ProtoForge.UI.ViewModels.Fields
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using CommunityToolkit.Mvvm.ComponentModel;
	using ProtoForge.Core.Domain;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Reference;
	using Services;

	/// <summary>A value picked from a named list (damage type, perk, caliber...).</summary>
	public sealed partial class ChoiceFieldViewModel : FieldViewModel
	{
		[ObservableProperty]
		private IReadOnlyList<ChoiceOption> _options = [];

		[ObservableProperty]
		private ChoiceOption? _selected;

		public ChoiceFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			IReadOnlyList<ChoiceOption> baseOptions = Localize(Field.ChoiceSet!, Context.Workspace.Choices.Get(Field.ChoiceSet!));
			if (values.Count == 0 || IsMixed)
			{
				Options = baseOptions;
				Selected = null;
				return;
			}

			Int32 value = values[0];
			ChoiceOption? match = baseOptions.Cast<ChoiceOption?>().FirstOrDefault(o => o!.Value.Value == value);
			if (match is null)
			{
				// Keep unknown values visible instead of silently showing another entry.
				ChoiceOption unknown = new(value, value.ToString(CultureInfo.InvariantCulture) + " (" + Loc.Get("field.unknown") + ")");
				Options = [.. baseOptions, unknown];
				Selected = unknown;
			}
			else
			{
				Options = baseOptions;
				Selected = match;
			}
		}

		partial void OnSelectedChanged(ChoiceOption? value)
		{
			if (!IsRefreshing && value is { } choice)
			{
				Commit(choice.Value);
			}
		}

		/// <summary>Built-in lists have English labels; a translation <c>choice.&lt;set&gt;.&lt;value&gt;</c> replaces them when it exists.</summary>
		private static IReadOnlyList<ChoiceOption> Localize(String set, IReadOnlyList<ChoiceOption> options)
		{
			if (!options.Any(o => Loc.Has($"choice.{set}.{o.Value}")))
			{
				return options;
			}

			return [.. options.Select(o => Loc.Has($"choice.{set}.{o.Value}") ? o with { Label = Loc.Get($"choice.{set}.{o.Value}") } : o)];
		}
	}

	public sealed record ReferenceOption(Int32 Value, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>A reference to another prototype (default ammo, projectile, power source...).</summary>
	public sealed partial class ProtoReferenceFieldViewModel : FieldViewModel
	{
		[ObservableProperty]
		private IReadOnlyList<ReferenceOption> _options = [];

		[ObservableProperty]
		private ReferenceOption? _selected;

		public ProtoReferenceFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			List<ReferenceOption> options = BuildOptions();
			if (values.Count == 0 || IsMixed)
			{
				Options = options;
				Selected = null;
				return;
			}

			Int32 value = values[0];
			ReferenceOption? match = options.FirstOrDefault(o => o.Value == value);
			if (match is null)
			{
				match = new ReferenceOption(value, $"{new ProtoId(value)} ({Loc.Get("field.unknown")})");
				options.Add(match);
			}

			Options = options;
			Selected = match;
		}

		partial void OnSelectedChanged(ReferenceOption? value)
		{
			if (!IsRefreshing && value is not null)
			{
				Commit(value.Value);
			}
		}

		private List<ReferenceOption> BuildOptions()
		{
			List<ReferenceOption> options = [new ReferenceOption(-1, Loc.Get("field.none"))];
			ProtoObjectType type = Field.ReferenceType ?? ProtoObjectType.Item;
			foreach (ProtoEntry entry in Context.Workspace.Catalog.GetEntries(type))
			{
				if (entry.File is null || (Field.ReferenceKind is { } kind && entry.Kind != kind))
				{
					continue;
				}

				String name = entry.Name.Length > 0 ? entry.Name : entry.FileName;
				options.Add(new ReferenceOption(entry.Pid.Value, $"{name}  [{entry.Pid}]"));
			}

			return options;
		}
	}

	public sealed record ScriptOption(Int32 Index, String Label)
	{
		public override String ToString()
		{
			return Label;
		}
	}

	/// <summary>
	/// A script id: the high byte is the script type (3 = item/scenery, 4 = critter) and the low 24 bits the number of the line of
	/// <c>scripts.lst</c>, counted from 0. Changing the script keeps the existing type byte.
	/// </summary>
	public sealed partial class ScriptFieldViewModel : FieldViewModel
	{
		private const Int32 NoScript = -1;

		[ObservableProperty]
		private IReadOnlyList<ScriptOption> _options = [];

		[ObservableProperty]
		private ScriptOption? _selected;

		public ScriptFieldViewModel(ProtoField field, EditorContext context)
			: base(field, context)
		{
		}

		protected override void Display(IReadOnlyList<Int32> values)
		{
			List<ScriptOption> options = [new ScriptOption(NoScript, Loc.Get("field.none"))];
			IReadOnlyList<String> scripts = Context.Workspace.ScriptNames;
			for (Int32 i = 0; i < scripts.Count; i++)
			{
				String description = Context.Workspace.GetScriptDescription(i);
				options.Add(new ScriptOption(i, description.Length > 0 ? $"{i}: {scripts[i]} — {description}" : $"{i}: {scripts[i]}"));
			}

			if (values.Count == 0 || IsMixed)
			{
				Options = options;
				Selected = null;
				return;
			}

			Int32 index = values[0] == NoScript ? NoScript : values[0] & 0xFFFFFF;
			ScriptOption? match = options.FirstOrDefault(o => o.Index == index);
			if (match is null)
			{
				match = new ScriptOption(index, $"{index} ({Loc.Get("field.unknown")})");
				options.Add(match);
			}

			Options = options;
			Selected = match;
		}

		partial void OnSelectedChanged(ScriptOption? value)
		{
			if (IsRefreshing || value is null)
			{
				return;
			}

			if (value.Index == NoScript)
			{
				Commit(NoScript);
				return;
			}

			Modify(current =>
			{
				Int32 type = current == NoScript ? DefaultType() : (current >> 24) & 0xFF;
				return (type << 24) | value.Index;
			});
		}

		private Int32 DefaultType()
		{
			return Entries.Count > 0 && Entries[0].Type == ProtoObjectType.Critter ? 4 : 3;
		}
	}
}