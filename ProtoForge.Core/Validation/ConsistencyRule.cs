namespace ProtoForge.Core.Validation
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Domain;
	using Formats.Frm;
	using Formats.Pro;

	/// <summary>Things that are legal for the engine but almost certainly a mistake: dangling references, weapons that cannot work, art that is missing.</summary>
	public sealed class ConsistencyRule : ValidationRuleBase
	{
		/// <summary>Fields the weapon checks of <see cref="ValueRule"/> already cover.</summary>
		private static readonly HashSet<String> CheckedElsewhere = new(StringComparer.Ordinal) { FieldKeys.AmmoPid, FieldKeys.ProjectilePid };

		public override String Id
		{
			get { return "consistency"; }
		}

		public override void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
			ProtoFile? file = entry.File;
			if (file is null)
			{
				return;
			}

			CheckReferences(context, entry, file, sink);
			CheckInventoryArt(entry, file, sink);
			if (file.Kind == ProtoKind.Weapon)
			{
				CheckWeapon(entry, file, sink);
			}
		}

		public override void ValidateAll(ValidationContext context, IssueSink sink)
		{
			if (!context.Catalog.IsLoaded(ProtoObjectType.Item))
			{
				return;
			}

			IReadOnlyList<ProtoEntry> items = context.Catalog.GetEntries(ProtoObjectType.Item);
			HashSet<Int32> weaponCalibers = [.. items.Where(e => e.File?.Kind == ProtoKind.Weapon).Select(e => e.File!.Get(FieldKeys.Caliber))];
			foreach (ProtoEntry ammo in items.Where(e => e.File?.Kind == ProtoKind.Ammo))
			{
				if (!weaponCalibers.Contains(ammo.File!.Get(FieldKeys.Caliber)))
				{
					sink.Report(Severity.Info, "AmmoWithoutWeapon", ammo, FieldKeys.Caliber, ammo.File.Get(FieldKeys.Caliber));
				}
			}
		}

		/// <summary>A prototype pointer (power cell...) must lead to a prototype that exists and has the kind the field asks for.</summary>
		private static void CheckReferences(ValidationContext context, ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			foreach (ProtoField field in file.Schema.Fields.Where(f => !f.IsVirtual && f.Editor == FieldEditor.Proto && !CheckedElsewhere.Contains(f.Key)))
			{
				if (file.TryGet(field) is not { } value || value == -1 || field.ReferenceType is not { } type || !context.Catalog.IsLoaded(type))
				{
					continue;
				}

				ProtoId id = new(value);
				ProtoEntry? target = id.Type == type ? context.Catalog.Find(value) : null;
				if (target is null)
				{
					sink.Report(Severity.Warning, "ReferenceMissing", entry, field.Key, field.Key, id);
				}
				else if (field.ReferenceKind is { } kind && target.Kind != kind)
				{
					sink.Report(Severity.Warning, "ReferenceWrongKind", entry, field.Key, field.Key, id, kind);
				}
			}
		}

		/// <summary>An item without an inventory image shows nothing in the inventory.</summary>
		private static void CheckInventoryArt(ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			if (entry.Type == ProtoObjectType.Item && file.TryGet(FieldKeys.InventoryFrmId) is -1)
			{
				sink.Report(Severity.Info, "NoInventoryArt", entry, FieldKeys.InventoryFrmId);
			}
		}

		private static void CheckWeapon(ProtoEntry entry, ProtoFile file, IssueSink sink)
		{
			Int32 magazine = file.Get("MaxAmmo");
			Int32 burst = file.Get("BurstRounds");
			Int32 maxDamage = file.Get(FieldKeys.MaxDamage);

			if (burst > 0 && magazine > 0 && burst > magazine)
			{
				sink.Report(Severity.Warning, "BurstExceedsMagazine", entry, "BurstRounds", burst, magazine);
			}

			if (magazine > 0 && file.Get(FieldKeys.Caliber) != 0 && file.Get(FieldKeys.AmmoPid, -1) == -1)
			{
				sink.Report(Severity.Info, "WeaponWithoutAmmo", entry, FieldKeys.AmmoPid);
			}

			if (maxDamage > 0 && file.Get("ApCostPrimary") == 0)
			{
				sink.Report(Severity.Info, "ZeroApCost", entry, "ApCostPrimary");
			}

			if (maxDamage > 0 && file.Get("MaxRangePrimary") == 0)
			{
				sink.Report(Severity.Info, "ZeroRange", entry, "MaxRangePrimary");
			}
		}
	}
}