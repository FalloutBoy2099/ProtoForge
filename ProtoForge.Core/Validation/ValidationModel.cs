namespace ProtoForge.Core.Validation
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using Domain;
	using Formats.Ai;
	using Formats.Frm;
	using Formats.Pro;
	using Reference;
	using Vfs;

	public enum Severity
	{
		Info,
		Warning,
		Error,
	}

	/// <summary>
	/// One problem found by a rule. <see cref="Code"/> and <see cref="Arguments"/> are what the UI localises;
	/// <see cref="Message"/> is the English text used in logs and exports.
	/// </summary>
	public sealed record ValidationIssue(
		Severity Severity,
		String RuleId,
		String Code,
		ProtoId? Pid,
		String? FieldKey,
		IReadOnlyList<Object> Arguments,
		String Message)
	{
		public String PidText
		{
			get { return Pid?.ToString() ?? String.Empty; }
		}
	}

	/// <summary>Collects the issues of a rule and formats their messages.</summary>
	public sealed class IssueSink
	{
		private readonly List<ValidationIssue> _issues;
		private readonly String _ruleId;

		public IssueSink(List<ValidationIssue> issues, String ruleId)
		{
			_issues = issues;
			_ruleId = ruleId;
		}

		public void Report(Severity severity, String code, ProtoEntry? entry, String? fieldKey, params Object[] arguments)
		{
			String message = ValidationMessages.Format(code, arguments);
			_issues.Add(new ValidationIssue(severity, _ruleId, code, entry?.Pid, fieldKey, arguments, message));
		}
	}

	/// <summary>Everything a rule may need to inspect.</summary>
	public sealed class ValidationContext
	{
		public ValidationContext(
			ProtoCatalog catalog,
			IGameFileSystem files,
			MessageStore messages,
			ProtoTextService texts,
			ArtResolver art,
			ChoiceCatalog choices,
			AiPacketCatalog aiPackets,
			Int32 scriptCount,
		IReadOnlyList<String>? scriptNames = null)
		{
			Catalog = catalog;
			Files = files;
			Messages = messages;
			Texts = texts;
			Art = art;
			Choices = choices;
			AiPackets = aiPackets;
			ScriptCount = scriptCount;
			ScriptNames = scriptNames ?? [];
		}

		public ProtoCatalog Catalog { get; }

		public IGameFileSystem Files { get; }

		public MessageStore Messages { get; }

		public ProtoTextService Texts { get; }

		public ArtResolver Art { get; }

		public ChoiceCatalog Choices { get; }

		public AiPacketCatalog AiPackets { get; }

		/// <summary>Number of lines in <c>scripts/scripts.lst</c> (0 when unknown).</summary>
		public Int32 ScriptCount { get; }

		/// <summary>The file names listed by <c>scripts/scripts.lst</c>, in order (empty when unknown).</summary>
		public IReadOnlyList<String> ScriptNames { get; }
	}

	/// <summary>
	/// A validation rule. Implement this interface in a plug-in assembly dropped in the <c>plugins</c> folder to add custom checks.
	/// </summary>
	public interface IValidationRule
	{
		String Id { get; }

		/// <summary>Called once per prototype.</summary>
		void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink);

		/// <summary>Called once per validation run, for checks that look at the whole catalog.</summary>
		void ValidateAll(ValidationContext context, IssueSink sink);
	}

	/// <summary>Convenience base class: override only what you need.</summary>
	public abstract class ValidationRuleBase : IValidationRule
	{
		public abstract String Id { get; }

		public virtual void Validate(ValidationContext context, ProtoEntry entry, IssueSink sink)
		{
		}

		public virtual void ValidateAll(ValidationContext context, IssueSink sink)
		{
		}
	}

	/// <summary>English message templates for the built-in issue codes (the UI replaces them with localised ones).</summary>
	public static class ValidationMessages
	{
		private static readonly Dictionary<String, String> Templates = new(StringComparer.Ordinal)
		{
			["FileMissing"] = "The LST lists '{0}' but no such file exists in the game data.",
			["FileUnreadable"] = "The file cannot be read: {0}",
			["BadSize"] = "The file is {0} bytes long; a {1} prototype should be {2} bytes.",
			["PidMismatch"] = "The PID stored in the file ({0}) does not match its position in the LST ({1}).",
			["DuplicateFile"] = "The file '{0}' is referenced by several LST lines.",
			["BlankLstLine"] = "Line {0} of the LST file is blank; it still counts as a prototype slot.",
			["OrphanFile"] = "The file '{0}' is in the prototype folder but not listed in the LST.",
			["MissingName"] = "No name found for message id {0}.",
			["MissingDescription"] = "No description found for message id {0}.",
			["NoMessageId"] = "The prototype has no message id, so it has no name.",
			["DuplicateMessageId"] = "Message id {0} is also used by {1}.",
			["MessageIdConvention"] = "Message id {0} differs from the usual convention (index × 100 = {1}).",
			["MissingArt"] = "The art for {0} ({1}) was not found: {2}",
			["InvalidArtIndex"] = "Frame id {0} points to index {1}, which is not in the art list.",
			["ScriptOutOfRange"] = "Script index {0} is outside scripts.lst ({1} entries).",
			["MinGreaterThanMax"] = "Minimum damage ({0}) is greater than maximum damage ({1}).",
			["AmmoDivisorZero"] = "The damage divisor is 0: this can crash the game.",
			["AmmoMultiplierZero"] = "The damage multiplier is 0: attacks with this ammo deal no damage.",
			["AmmoReferenceInvalid"] = "The default ammo {0} is not an ammo item.",
			["CaliberMismatch"] = "The weapon caliber ({0}) differs from the caliber of its default ammo ({1}).",
			["ProjectileInvalid"] = "The projectile {0} is not a misc prototype.",
			["ValueOutOfRange"] = "{0} = {1} is outside the usual range {2}..{3}.",
			["TransparencyConflict"] = "Several transparency flags are set together.",
			["RuntimeFlag"] = "The flag '{0}' is a runtime flag and should not be set in a prototype.",
			["UnknownFlagBits"] = "{0} has undocumented bits set: 0x{1}.",
			["LightOutOfRange"] = "Light radius {0} / intensity {1} is outside the engine range (0..8 / 0..65536).",
			["AiPacketMissing"] = "AI packet {0} is not defined in AI.txt.",
			["UninitializedValue"] = "{0} contains 0xCCCCCCCC: an uninitialised value left by an editor.",
			["PerkInvalid"] = "{0} refers to perk {1}, which does not exist.",
			["ArtTypeMismatch"] = "{0} has art type {1} but {2} is expected.",
			["RuleFailed"] = "A validation rule failed: {0}",
			["InvalidChoice"] = "{0} = {1} is not a known value.",
			["ScriptFileMissing"] = "Script {0} ('{1}') is listed in scripts.lst but the file scripts/{1} was not found.",
			["ReferenceMissing"] = "{0} points at {1}, which does not exist.",
			["ReferenceWrongKind"] = "{0} points at {1}, which is not a {2} prototype.",
			["NoInventoryArt"] = "The item has no inventory image: it will show nothing in the inventory.",
			["BurstExceedsMagazine"] = "A burst fires {0} rounds but the magazine holds only {1}.",
			["WeaponWithoutAmmo"] = "The weapon uses ammunition but has no default ammo.",
			["ZeroApCost"] = "The primary attack costs 0 action points.",
			["ZeroRange"] = "The primary attack has a range of 0.",
			["AmmoWithoutWeapon"] = "No weapon uses caliber {0}: this ammo cannot be loaded into anything.",
		};

		public static String Format(String code, params Object[] arguments)
		{
			if (!Templates.TryGetValue(code, out String? template))
			{
				return code;
			}

			try
			{
				return String.Format(CultureInfo.InvariantCulture, template, arguments);
			}
			catch (FormatException)
			{
				return template;
			}
		}
	}
}