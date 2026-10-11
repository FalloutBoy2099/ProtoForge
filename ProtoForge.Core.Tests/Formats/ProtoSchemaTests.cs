namespace ProtoForge.Core.Tests.Formats
{
	using System;
	using System.Linq;
	using Xunit;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.Templates;

	public class ProtoSchemaTests
	{
		public static TheoryData<ProtoKind> AllKinds()
		{
			TheoryData<ProtoKind> data = [];
			foreach (ProtoKind kind in ProtoKindExtensions.All)
			{
				data.Add(kind);
			}

			return data;
		}

		[Theory]
		[MemberData(nameof(AllKinds))]
		public void Schema_CoversTheExpectedSize_WithoutGapsOrOverlaps(ProtoKind kind)
		{
			ProtoSchema schema = ProtoSchemas.For(kind);
			var real = schema.Fields.Where(f => !f.IsVirtual).OrderBy(f => f.Offset).ToList();

			Int32 cursor = 0;
			foreach (ProtoField field in real)
			{
				Assert.True(field.Offset >= cursor, $"{kind}.{field.Key} overlaps the previous field.");
				cursor = field.End;
			}

			// Critters have an optional last field in the Fallout 1 layout; every other kind must end exactly at the expected size.
			Assert.Equal(kind.GetExpectedSize(), real[^1].End);
		}

		[Theory]
		[MemberData(nameof(AllKinds))]
		public void Schema_KeysAreUnique(ProtoKind kind)
		{
			ProtoSchema schema = ProtoSchemas.For(kind);
			Assert.Equal(schema.Fields.Count, schema.Fields.Select(f => f.Key).Distinct().Count());
		}

		[Theory]
		[InlineData("weapon.pro", ProtoKind.Weapon, 3)]
		[InlineData("armor.pro", ProtoKind.Armor, 0)]
		[InlineData("ammo.pro", ProtoKind.Ammo, 4)]
		[InlineData("drugs.pro", ProtoKind.Drug, 2)]
		[InlineData("container.pro", ProtoKind.Container, 1)]
		[InlineData("key.pro", ProtoKind.Key, 6)]
		[InlineData("misc.pro", ProtoKind.MiscItem, 5)]
		public void Fixture_ParsesAsExpectedItemKind(String fixture, ProtoKind kind, Int32 subType)
		{
			Byte[] bytes = TestEnvironment.ReadFixture(fixture);
			ProtoFile? file = ProtoFile.TryParse(bytes, ProtoObjectType.Item, out String? error);

			Assert.Null(error);
			Assert.NotNull(file);
			Assert.Equal(kind, file.Kind);
			Assert.True(file.HasExpectedLength);
			Assert.Equal(subType, file.Get(FieldKeys.SubType));
			Assert.Equal(bytes, file.ToArray());
		}

		[Fact]
		public void CritterFixture_HasDocumentedValues()
		{
			ProtoFile file = ProtoFile.TryParse(TestEnvironment.ReadFixture("critter.pro"), ProtoObjectType.Critter, out _)!;

			Assert.Equal(ProtoKind.Critter, file.Kind);
			Assert.Equal(5, file.Get("Strength"));
			Assert.Equal(30, file.Get("MaxHp"));
			Assert.Equal(150, file.Get("CarryWeight"));
			Assert.Equal(25, file.Get("Age"));
			Assert.Equal(0x1E, file.Get("MaxHp") & 0xFF);
			Assert.Equal(0x4C, file.Schema.Require("MaxHp").Offset);
		}

		[Fact]
		public void WeaponAttackModes_AreStoredInTheLowByteOfTheExtendedFlags()
		{
			ProtoFile file = ProtoFactory.CreateDefault(ProtoKind.Weapon, ProtoId.From(ProtoObjectType.Item, 1));
			file.TrySet(FieldKeys.FlagsExt, 0xA000);

			Assert.True(file.TrySet("PrimaryAttackMode", 6));
			Assert.True(file.TrySet("SecondaryAttackMode", 7));

			Assert.Equal(0xA076, file.FlagsExt);
			Assert.Equal(6, file.Get("PrimaryAttackMode"));
			Assert.Equal(7, file.Get("SecondaryAttackMode"));
		}

		[Fact]
		public void Fallout1Critter_HasNoDamageTypeField()
		{
			Byte[] bytes = TestEnvironment.ReadFixture("critter.pro")[..412];
			ProtoFile file = ProtoFile.TryParse(bytes, ProtoObjectType.Critter, out _)!;

			Assert.True(file.IsFallout1Critter);
			Assert.True(file.HasExpectedLength);
			Assert.Null(file.TryGet("DamageType"));
			Assert.False(file.TrySet("DamageType", 3));
			Assert.Equal(412, file.ToArray().Length);
		}

		[Theory]
		[MemberData(nameof(AllKinds))]
		public void Factory_CreatesFilesOfTheExpectedSize_AndReadableKind(ProtoKind kind)
		{
			ProtoId pid = ProtoId.From(kind.GetObjectType(), 7);
			ProtoFile file = ProtoFactory.CreateDefault(kind, pid);

			Assert.Equal(kind.GetExpectedSize(), file.Length);
			Assert.Equal(pid.Value, file.StoredProtoId);

			ProtoFile? reparsed = ProtoFile.TryParse(file.ToArray(), kind.GetObjectType(), out String? error);
			Assert.Null(error);
			Assert.Equal(kind, reparsed!.Kind);
		}

		[Fact]
		public void ProtoId_RoundTripsThroughText()
		{
			ProtoId id = ProtoId.From(ProtoObjectType.Critter, 42);

			Assert.Equal(0x0100002A, id.Value);
			Assert.True(ProtoId.TryParse(id.ToString(), out ProtoId parsed));
			Assert.Equal(id, parsed);
			Assert.Equal(ProtoObjectType.Critter, parsed.Type);
			Assert.Equal(42, parsed.Index);
		}
	}
}