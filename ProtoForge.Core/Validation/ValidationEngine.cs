namespace ProtoForge.Core.Validation
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading;
	using Domain;

	/// <summary>Runs a set of <see cref="IValidationRule"/> over the catalog.</summary>
	public sealed class ValidationEngine
	{
		public ValidationEngine(IEnumerable<IValidationRule> rules)
		{
			Rules = [.. rules];
		}

		public List<IValidationRule> Rules { get; }

		public static ValidationEngine CreateDefault(IEnumerable<IValidationRule>? extraRules = null)
		{
			List<IValidationRule> rules =
			[
				new FileIntegrityRule(),
				new MessageRule(),
				new ArtReferenceRule(),
				new ScriptRule(),
				new ValueRule(),
				new ConsistencyRule(),
			];
			if (extraRules is not null)
			{
				rules.AddRange(extraRules);
			}

			return new ValidationEngine(rules);
		}

		public IReadOnlyList<ValidationIssue> Run(
			ValidationContext context,
			IEnumerable<ProtoEntry> entries,
			IProgress<Double>? progress = null,
			CancellationToken cancellationToken = default)
		{
			List<ValidationIssue> issues = [];
			List<ProtoEntry> list = [.. entries];
			Int32 done = 0;

			foreach (ProtoEntry entry in list)
			{
				cancellationToken.ThrowIfCancellationRequested();
				foreach (IValidationRule rule in Rules)
				{
					try
					{
						rule.Validate(context, entry, new IssueSink(issues, rule.Id));
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						issues.Add(new ValidationIssue(Severity.Error, rule.Id, "RuleFailed", entry.Pid, null, [ex.Message],
							$"Rule '{rule.Id}' failed: {ex.Message}"));
					}
				}

				done++;
				if ((done & 0xFF) == 0)
				{
					progress?.Report((Double)done / list.Count);
				}
			}

			foreach (IValidationRule rule in Rules)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					rule.ValidateAll(context, new IssueSink(issues, rule.Id));
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					issues.Add(new ValidationIssue(Severity.Error, rule.Id, "RuleFailed", null, null, [ex.Message],
						$"Rule '{rule.Id}' failed: {ex.Message}"));
				}
			}

			progress?.Report(1);
			return [.. issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Pid?.Value ?? Int32.MaxValue)];
		}
	}
}