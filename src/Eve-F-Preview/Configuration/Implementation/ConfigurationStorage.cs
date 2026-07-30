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

			string rawData = File.ReadAllText(filename);

			JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
			{
				ObjectCreationHandling = ObjectCreationHandling.Replace
			};

			JsonConvert.PopulateObject(rawData, this._thumbnailConfiguration, jsonSerializerSettings);

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

				File.WriteAllText(filename, rawData);
			}
			catch (IOException)
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
