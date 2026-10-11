namespace ProtoForge.Core.Formats.Ai
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	/// <summary>One combat AI packet of <c>data/AI.txt</c> (a section with a <c>packet_num</c> and behaviour settings).</summary>
	public sealed class AiPacket
	{
		public AiPacket(String name, Int32 number, IReadOnlyDictionary<String, String> properties)
		{
			Name = name;
			Number = number;
			Properties = properties;
		}

		public String Name { get; }

		public Int32 Number { get; }

		public IReadOnlyDictionary<String, String> Properties { get; }

		public String DisplayName
		{
			get { return $"{Number}: {Name}"; }
		}
	}

	/// <summary>The AI packets known to the game, used to describe the <c>AiPacket</c> field of critters.</summary>
	public sealed class AiPacketCatalog
	{
		public static readonly AiPacketCatalog Empty = new([]);

		private readonly Dictionary<Int32, AiPacket> _byNumber;

		public AiPacketCatalog(IEnumerable<AiPacket> packets)
		{
			Packets = [.. packets.OrderBy(p => p.Number)];
			_byNumber = new Dictionary<Int32, AiPacket>();
			foreach (AiPacket packet in Packets)
			{
				_byNumber.TryAdd(packet.Number, packet);
			}
		}

		public IReadOnlyList<AiPacket> Packets { get; }

		public static AiPacketCatalog Parse(Byte[] bytes)
		{
			IniFile ini = IniFile.Parse(bytes);
			List<AiPacket> packets = [];
			foreach (String section in ini.SectionNames)
			{
				IReadOnlyDictionary<String, String> values = ini.GetSection(section);
				if (values.TryGetValue("packet_num", out String? number) && Int32.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out Int32 value))
				{
					packets.Add(new AiPacket(section, value, values));
				}
			}

			return new AiPacketCatalog(packets);
		}

		public AiPacket? Find(Int32 number)
		{
			return _byNumber.TryGetValue(number, out AiPacket? packet) ? packet : null;
		}
	}
}