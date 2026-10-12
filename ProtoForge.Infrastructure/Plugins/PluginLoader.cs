namespace ProtoForge.Infrastructure.Plugins
{
	using ProtoForge.Core;
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
			HashSet<String> loaded = new(StringComparer.OrdinalIgnoreCase);
			foreach (String folder in GetPluginFolders().Where(Directory.Exists))
			{
				foreach (String file in Directory.EnumerateFiles(folder, "*.dll"))
				{
					try
					{
						// The same plug-in dropped in both folders must not register its rules twice.
						if (!loaded.Add(AssemblyName.GetAssemblyName(file).Name ?? Path.GetFileNameWithoutExtension(file)))
						{
							continue;
						}

						Assembly assembly = Assembly.LoadFrom(file);
						foreach (Type type in assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IValidationRule).IsAssignableFrom(t)))
						{
							try
							{
								if (type.GetConstructor(Type.EmptyTypes) is not null && Activator.CreateInstance(type) is IValidationRule rule)
								{
									rules.Add(rule);
								}
							}
							catch (Exception ex) when (ex is not OutOfMemoryException)
							{
								warnings.Add(UserTexts.Format("plugin.ruleFailed", "Plug-in rule '{0}' of '{1}' could not be created: {2}", type.Name, Path.GetFileName(file), ex.Message));
							}
						}
					}
					catch (Exception ex) when (ex is not OutOfMemoryException)
					{
						// Whatever a faulty plug-in throws, it must never prevent a game from being opened.
						warnings.Add(UserTexts.Format("plugin.loadFailed", "Plug-in '{0}' could not be loaded: {1}", Path.GetFileName(file), ex.Message));
					}
				}
			}

			return rules;
		}
	}
}