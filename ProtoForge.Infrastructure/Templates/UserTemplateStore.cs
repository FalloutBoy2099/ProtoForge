namespace ProtoForge.Infrastructure.Templates
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using ProtoForge.Core.Formats.Pro;
	using ProtoForge.Core.IO;
	using ProtoForge.Core.Templates;
	using ProtoForge.Core.Vfs;
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
				Byte[] bytes;
				try
				{
					bytes = File.ReadAllBytes(file);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					continue;
				}

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

		/// <summary>True when a template with this name (after cleaning it for the file system) already exists.</summary>
		public Boolean Exists(String name)
		{
			return File.Exists(GetPath(name));
		}

		public void Save(String name, ProtoFile file)
		{
			Directory.CreateDirectory(_folder);
			String path = GetPath(name);

			// Written beside the final file and swapped in, so that a failure never leaves a half-written template.
			String temp = path + ".protoforge-tmp";
			File.WriteAllBytes(temp, file.ToArray());
			try
			{
				File.Move(temp, path, overwrite: true);
			}
			catch
			{
				File.Delete(temp);
				throw;
			}
		}

		public void Delete(String name)
		{
			String path = GetPath(name);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		/// <summary>The file of a template. Characters that a file name cannot hold become '_', and reserved device names are prefixed.</summary>
		private String GetPath(String name)
		{
			Char[] invalid = Path.GetInvalidFileNameChars();
			String safe = String.Concat(name.Select(c => invalid.Contains(c) || Char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.');
			if (safe.Length == 0)
			{
				throw new ArgumentException("The template name is empty.", nameof(name));
			}

			if (safe.Length > 100)
			{
				safe = safe[..100].TrimEnd();
			}

			if (GamePath.IsDeviceName(safe))
			{
				safe = "_" + safe;
			}

			return Path.Combine(_folder, safe + ".pro");
		}
	}
}