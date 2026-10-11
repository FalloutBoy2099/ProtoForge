namespace ProtoForge.Infrastructure.Plugins
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using ProtoForge.Core.Validation;

	/// <summary>
	/// Loads extra <see cref="IValidationRule"/> implementations from the assemblies placed in the <c>plugins</c> folder next to
	/// the application or in the user's application data folder. A plug-in is plain managed code: reference
	/// <c>ProtoForge.Core.dll</c>, implement <see cref="IValidationRule"/> (or derive from <see cref="ValidationRuleBase"/>) and copy the DLL.
	/// </summary>
	public sealed class PluginLoader
	{
		public static IEnumerable<String> GetPluginFolders()
		{
			yield return Path.Combine(AppContext.BaseDirectory, "plugins");
			yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtoForge", "plugins");
		}

		public IReadOnlyList<IValidationRule> LoadRules(ICollection<String> warnings)
		{
			List<IValidationRule> rules = [];
			foreach (String folder in GetPluginFolders().Where(Directory.Exists))
			{
				foreach (String file in Directory.EnumerateFiles(folder, "*.dll"))
				{
					try
					{
						Assembly assembly = Assembly.LoadFrom(file);
						foreach (Type type in assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IValidationRule).IsAssignableFrom(t)))
						{
							if (type.GetConstructor(Type.EmptyTypes) is not null && Activator.CreateInstance(type) is IValidationRule rule)
							{
								rules.Add(rule);
							}
						}
					}
					catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException)
					{
						warnings.Add($"Plug-in '{Path.GetFileName(file)}' could not be loaded: {ex.Message}");
					}
				}
			}

			return rules;
		}
	}
}