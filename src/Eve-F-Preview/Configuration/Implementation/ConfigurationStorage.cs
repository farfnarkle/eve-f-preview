using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace EveFPreview.Configuration.Implementation
{
	class ConfigurationStorage : IConfigurationStorage
	{
		private const string CONFIGURATION_FILE_NAME = "EVE-F-Preview.json";
		private const string LEGACY_CONFIGURATION_FILE_NAME = "EVE-O-Preview.json";
		private const string BACKUP_SUFFIX = ".bak";

		private readonly IAppConfig _appConfig;
		private readonly IThumbnailConfiguration _thumbnailConfiguration;

		public ConfigurationStorage(IAppConfig appConfig, IThumbnailConfiguration thumbnailConfiguration)
		{
			this._appConfig = appConfig;
			this._thumbnailConfiguration = thumbnailConfiguration;
		}

		public void Load()
		{
			string filename = this.ResolveLoadConfigFileName();

			if (!File.Exists(filename))
			{
				return;
			}

			JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
			{
				ObjectCreationHandling = ObjectCreationHandling.Replace
			};

			try
			{
				string rawData = File.ReadAllText(filename);
				if (string.IsNullOrWhiteSpace(rawData))
				{
					throw new JsonSerializationException("Configuration file is empty.");
				}

				JsonConvert.PopulateObject(rawData, this._thumbnailConfiguration, jsonSerializerSettings);
			}
			catch (JsonException)
			{
				// Empty or unreadable (older builds saved in place, so a shutdown mid-save could leave
				// an empty file). Fall back to the copy kept by the previous save; with no usable
				// copy, rethrow rather than start on defaults and overwrite the file on the next save.
				string backup = filename + ConfigurationStorage.BACKUP_SUFFIX;
				string backupData = File.Exists(backup) ? File.ReadAllText(backup) : null;
				if (string.IsNullOrWhiteSpace(backupData))
				{
					throw;
				}

				JsonConvert.PopulateObject(backupData, this._thumbnailConfiguration, jsonSerializerSettings);
			}

			this._thumbnailConfiguration.ApplyRestrictions();
		}

		public void Save()
		{
			string rawData = JsonConvert.SerializeObject(this._thumbnailConfiguration, Formatting.Indented);
			string filename = this.GetSaveConfigFileName();

			try
			{
				string directory = Path.GetDirectoryName(filename);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}

				// Write a temp file and swap it in, so the config is never left half-written (or
				// empty) if the app is killed or the PC shuts down mid-save. The previous version is
				// kept as the backup Load falls back to - unless it's empty, so an already-damaged
				// file never replaces a good backup.
				string tempFile = filename + ".tmp";
				File.WriteAllText(tempFile, rawData);
				if (File.Exists(filename))
				{
					bool previousIsUsable = new FileInfo(filename).Length > 0;
					File.Replace(tempFile, filename, previousIsUsable ? filename + ConfigurationStorage.BACKUP_SUFFIX : null, ignoreMetadataErrors: true);
				}
				else
				{
					File.Move(tempFile, filename);
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// Ignore error if for some reason the updated config cannot be written down
			}
		}

		public string ActiveConfigPath => this.ResolveLoadConfigFileName();

		public IReadOnlyList<string> ListConfigProfiles()
		{
			var results = new List<string>();
			string baseDirectory = AppContext.BaseDirectory;
			if (!Directory.Exists(baseDirectory))
			{
				return results;
			}

			foreach (string file in Directory.EnumerateFiles(baseDirectory, "*.json"))
			{
				if (ConfigurationStorage.LooksLikeThumbnailConfig(file))
				{
					results.Add(Path.GetFileName(file));
				}
			}

			results.Sort(StringComparer.OrdinalIgnoreCase);
			return results;
		}

		public void SwitchTo(string pathOrFileName)
		{
			if (string.IsNullOrWhiteSpace(pathOrFileName))
			{
				throw new ArgumentException("Profile path/file name is required.", nameof(pathOrFileName));
			}

			this.Save();

			this._appConfig.ConfigFileName = Path.IsPathRooted(pathOrFileName)
				? pathOrFileName
				: Path.GetFileName(pathOrFileName);

			this.Load();
		}

		public void SaveAs(string pathOrFileName)
		{
			if (string.IsNullOrWhiteSpace(pathOrFileName))
			{
				throw new ArgumentException("Profile path/file name is required.", nameof(pathOrFileName));
			}

			this._appConfig.ConfigFileName = Path.IsPathRooted(pathOrFileName)
				? pathOrFileName
				: Path.GetFileName(pathOrFileName);

			this.Save();
		}

		public void ImportFrom(string sourcePath, string destinationFileName)
		{
			if (string.IsNullOrWhiteSpace(sourcePath))
			{
				throw new ArgumentException("Source path is required.", nameof(sourcePath));
			}

			if (string.IsNullOrWhiteSpace(destinationFileName))
			{
				throw new ArgumentException("Destination file name is required.", nameof(destinationFileName));
			}

			string destinationPath = this.ResolveConfiguredPath(destinationFileName);
			ConfigImportService.ImportToFile(sourcePath, destinationPath);
		}

		/// <summary>Cheap sniff so ListConfigProfiles doesn't pick up unrelated *.json files sitting next to the exe.</summary>
		private static bool LooksLikeThumbnailConfig(string path)
		{
			try
			{
				var info = new FileInfo(path);
				if (info.Length > 20 * 1024 * 1024)
				{
					return false;
				}

				string content = File.ReadAllText(path);
				return content.IndexOf("\"ThumbnailSize\"", StringComparison.OrdinalIgnoreCase) >= 0
					|| content.IndexOf("\"ConfigVersion\"", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			catch
			{
				return false;
			}
		}

		private string GetSaveConfigFileName()
		{
			if (!string.IsNullOrEmpty(this._appConfig.ConfigFileName))
			{
				return this.ResolveConfiguredPath(this._appConfig.ConfigFileName);
			}

			return this.GetDefaultConfigPath(ConfigurationStorage.CONFIGURATION_FILE_NAME);
		}

		private string ResolveLoadConfigFileName()
		{
			if (!string.IsNullOrEmpty(this._appConfig.ConfigFileName))
			{
				return this.ResolveConfiguredPath(this._appConfig.ConfigFileName);
			}

			string newConfigPath = this.GetDefaultConfigPath(ConfigurationStorage.CONFIGURATION_FILE_NAME);
			if (File.Exists(newConfigPath))
			{
				return newConfigPath;
			}

			string legacyConfigPath = this.GetDefaultConfigPath(ConfigurationStorage.LEGACY_CONFIGURATION_FILE_NAME);
			if (File.Exists(legacyConfigPath))
			{
				return legacyConfigPath;
			}

			return newConfigPath;
		}

		/// <summary>
		/// Always keep settings next to the executable (not the process working directory),
		/// so deploy / shortcuts / different CWDs cannot point at another EVE-F-Preview.json.
		/// </summary>
		private string GetDefaultConfigPath(string fileName)
		{
			return Path.Combine(AppContext.BaseDirectory, fileName);
		}

		private string ResolveConfiguredPath(string configuredPath)
		{
			if (Path.IsPathRooted(configuredPath))
			{
				return configuredPath;
			}

			return Path.Combine(AppContext.BaseDirectory, configuredPath);
		}
	}
}
