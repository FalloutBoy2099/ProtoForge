namespace ProtoForge.Infrastructure.Rules
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using ProtoForge.Core.Editing;
	using Settings;

	/// <summary>
	/// A bulk edit that was saved to be played again (after an update of a big mod, for instance): the kind of prototype, the field,
	/// the operation with its operand and the optional condition.
	/// </summary>
	public sealed record BulkRule(
		String Name,
		String Kind,
		String Field,
		BulkOperation Operation,
		Double Operand,
		String? ConditionField = null,
		BulkComparison ConditionComparison = BulkComparison.Equal,
		Int32 ConditionValue = 0);

	/// <summary>The saved bulk edits, kept in <c>bulk-rules.json</c> in the data folder of the application.</summary>
	public sealed class BulkRuleStore
	{
		private static readonly JsonSerializerOptions Json = new()
		{
			WriteIndented = true,
			Converters = { new JsonStringEnumConverter() },
		};

		private readonly String _path;

		public BulkRuleStore(String? path = null)
		{
			_path = path ?? Path.Combine(SettingsService.GetDataFolder(), "bulk-rules.json");
		}

		public Boolean Exists(String name)
		{
			return Load().Any(r => String.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>The saved rules, by name. A file that cannot be read gives no rule (it is left alone).</summary>
		public IReadOnlyList<BulkRule> Load()
		{
			try
			{
				if (!File.Exists(_path))
				{
					return [];
				}

				List<BulkRule> rules = JsonSerializer.Deserialize<List<BulkRule>>(File.ReadAllText(_path), Json) ?? [];
				return [.. rules.Where(r => !String.IsNullOrWhiteSpace(r.Name)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
			{
				return [];
			}
		}

		/// <summary>Adds the rule, or replaces the one that has the same name.</summary>
		public void Save(BulkRule rule)
		{
			List<BulkRule> rules = [.. Load().Where(r => !String.Equals(r.Name, rule.Name, StringComparison.OrdinalIgnoreCase)), rule];
			Write(rules);
		}

		public void Delete(String name)
		{
			Write([.. Load().Where(r => !String.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))]);
		}

		private void Write(IReadOnlyList<BulkRule> rules)
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
			String temp = _path + ".tmp";
			File.WriteAllText(temp, JsonSerializer.Serialize(rules, Json));
			File.Move(temp, _path, overwrite: true);
		}
	}
}