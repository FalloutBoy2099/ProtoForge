namespace ProtoForge.Core.Templates
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Formats.Pro;

	/// <summary>
	/// Changes the layout of an item (weapon, armor, drug...) or of a scenery object (door, stairs...). The common header is
	/// kept; the fields that belong to the old kind are dropped and the ones of the new kind get the engine defaults.
	/// </summary>
	public static class ProtoConversion
	{
		private static readonly String[] SharedKeys =
		[
			FieldKeys.MessageId,
			FieldKeys.FrmId,
			FieldKeys.LightRadius,
			FieldKeys.LightIntensity,
			FieldKeys.Flags,
			FieldKeys.ScriptId,
			FieldKeys.Material,
			FieldKeys.Size,
			FieldKeys.Weight,
			FieldKeys.Cost,
			FieldKeys.InventoryFrmId,
			FieldKeys.SoundId,
		];

		/// <summary>The kinds a prototype can be turned into (the other kinds of the same family), empty when the kind has no family.</summary>
		public static IReadOnlyList<ProtoKind> GetTargets(ProtoKind kind)
		{
			if (!kind.IsItem() && !kind.IsScenery())
			{
				return [];
			}

			return [.. ProtoKindExtensions.All.Where(k => k.GetObjectType() == kind.GetObjectType())];
		}

		public static Boolean CanConvert(ProtoKind from, ProtoKind to)
		{
			return from != to && from.GetObjectType() == to.GetObjectType() && GetTargets(from).Count > 0;
		}

		/// <summary>Builds the converted file. The prototype id stored in the file is kept.</summary>
		public static ProtoFile Convert(ProtoFile source, ProtoKind target)
		{
			if (!CanConvert(source.Kind, target))
			{
				throw new InvalidOperationException($"A {source.Kind} cannot be converted to {target}.");
			}

			ProtoFile result = ProtoFactory.CreateDefault(target, new ProtoId(source.StoredProtoId));
			foreach (String key in SharedKeys)
			{
				ProtoField? from = source.Schema.Find(key);
				ProtoField? to = result.Schema.Find(key);
				if (from is not null && to is not null && source.TryGet(from) is { } value)
				{
					result.TrySet(to, value);
				}
			}

			// Extended flags: the low byte and the big gun / two-hand / energy bits only mean something for weapons.
			Int32 ext = source.Get(FieldKeys.FlagsExt);
			Int32 weaponOnly = unchecked((Int32)(0xFFu | 0x700u));
			Int32 keep = ext & ~weaponOnly;
			result.TrySet(FieldKeys.FlagsExt, keep | (result.Get(FieldKeys.FlagsExt) & weaponOnly));
			return result;
		}
	}
}