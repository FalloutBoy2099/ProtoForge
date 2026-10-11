namespace ProtoForge.Infrastructure.Templates
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.IO;
	using ProtoForge.Core.Templates;
	using Settings;

	/// <summary>
	/// Prototypes saved by the user as reusable templates. A template is simply a <c>.pro</c> file in
	/// <c>%AppData%\ProtoForge\templates</c>, so it can also be shared or edited with any other tool.
	/// </summary>
	public sealed class UserTemplateStore
	{
		private readonly String _folder;

		public UserTemplateStore(String? folder = null)
		{
			_folder = folder ?? Path.Combine(SettingsService.GetDataFolder(), "templates");
		}

		public IReadOnlyList<ProtoTemplate> Load()
		{
			if (!Directory.Exists(_folder))
			{
				return [];
			}

			List<ProtoTemplate> templates = [];
			foreach (String file in Directory.EnumerateFiles(_folder, "*.pro").Order(StringComparer.OrdinalIgnoreCase))
			{
				Byte[] bytes = File.ReadAllBytes(file);
				if (bytes.Length < 12)
				{
					continue;
				}

				ProtoObjectType type = (ProtoObjectType)((BigEndian.ReadInt32(bytes, 0) >> 24) & 0xFF);
				ProtoFile? parsed = ProtoFile.TryParse(bytes, type, out _);
				if (parsed is null)
				{
					continue;
				}

				String name = Path.GetFileNameWithoutExtension(file);
				templates.Add(new ProtoTemplate("user." + name, name, parsed.Kind, new Dictionary<String, Int32>(), "User") { Source = parsed });
			}

			return templates;
		}

		public void Save(String name, ProtoFile file)
		{
			Directory.CreateDirectory(_folder);
			String safe = String.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
			if (safe.Length == 0)
			{
				throw new ArgumentException("The template name is empty.", nameof(name));
			}

			File.WriteAllBytes(Path.Combine(_folder, safe + ".pro"), file.ToArray());
		}

		public void Delete(String name)
		{
			String path = Path.Combine(_folder, name + ".pro");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}
}