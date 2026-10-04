using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace EveFPreview.Configuration.Implementation
{
	class ConfigurationStorage : IConfigurationStorage
	{
		private const string CONFIGURATION_FILE_NAME = "EVE-F-Preview.json";
		private const string LEGACY_CONFIGURATION_FILE_NAME = "EVE-O-Preview.json";
		private const string BACKUP_SUFFIX = ".bak";
		private const string LOG_FILE_NAME = "EVE-F-Preview.log";
		private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromSeconds(2);
		private const int MaxQuickRetries = 3;

		private static readonly JsonSerializerSettings LoadSettings = new JsonSerializerSettings()
		{
			ObjectCreationHandling = ObjectCreationHandling.Replace
		};

		// Every saved setting at its default value; applied before a file so settings the file
		// doesn't mention are reset instead of keeping the previously loaded profile's values.
		private static readonly Lazy<string> DefaultSettingsJson =
			new Lazy<string>(() => JsonConvert.SerializeObject(new ThumbnailConfiguration()));

		private readonly IAppConfig _appConfig;
		private readonly IThumbnailConfiguration _thumbnailConfiguration;
		private readonly object _saveSync = new object();
		private DispatcherTimer _saveRetryTimer;
		private int _consecutiveSaveFailures;
		private bool _userToldAboutSaveFailure;

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

			try
			{
				string rawData = File.ReadAllText(filename);
				if (string.IsNullOrWhiteSpace(rawData))
				{
					throw new JsonSerializationException("Configuration file is empty.");
				}

				this.PopulateFrom(rawData);
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

				this.PopulateFrom(backupData);
			}

			this._thumbnailConfiguration.ApplyRestrictions();
		}

		/// <summary>
		/// Resets every saved setting to its default, then applies <paramref name="rawData"/> on top.
		/// Loading onto the live object alone would let a profile that omits a setting inherit the
		/// value from whichever profile was loaded before it.
		/// </summary>
		private void PopulateFrom(string rawData)
		{
			JsonConvert.PopulateObject(ConfigurationStorage.DefaultSettingsJson.Value, this._thumbnailConfiguration, ConfigurationStorage.LoadSettings);
			JsonConvert.PopulateObject(rawData, this._thumbnailConfiguration, ConfigurationStorage.LoadSettings);
		}

		public void Save()
		{
			// Settings are changed on the UI thread, so serialize them there too: serializing from
			// another thread while the UI edits a dictionary can throw (or write a torn snapshot).
			if (UiThread.IsRequired)
			{
				UiThread.Run(this.Save);
				return;
			}

			lock (this._saveSync)
			{
				this.SaveCore();
			}
		}

		private void SaveCore()
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
				this.OnSaveFailed(filename, ex);
				return;
			}

			if (this._consecutiveSaveFailures > 0)
			{
				ConfigurationStorage.Log($"Settings saved again after {this._consecutiveSaveFailures} failed attempt(s).");
				this._consecutiveSaveFailures = 0;
			}
		}

		/// <summary>
		/// Every save writes the whole config, so one failed write (antivirus or a backup tool briefly
		/// holding the file, ...) is made good by the next save. It's logged and retried shortly; if
		/// the retry fails too, the user is told once per session, since changes would then be lost
		/// if the app closed. It used to be ignored silently.
		/// </summary>
		private void OnSaveFailed(string filename, Exception ex)
		{
			this._consecutiveSaveFailures++;
			ConfigurationStorage.Log($"Could not save settings to {filename} (attempt {this._consecutiveSaveFailures}): {ex.Message}");

			if (this._consecutiveSaveFailures >= 2 && !this._userToldAboutSaveFailure)
			{
				this._userToldAboutSaveFailure = true;
				string message = "EVE-F-Preview can't save your settings to:\n" + filename + "\n\n" + ex.Message +
					"\n\nIt will keep trying. Changes made since the last successful save will be lost if the app is closed before then.";
				// Not from inside the save itself: a modal box would run a nested message loop mid-save.
				Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
					MessageBox.Show(message, "EVE-F-Preview", MessageBoxButton.OK, MessageBoxImage.Warning)));
			}

			// A few quick retries cover a briefly locked file; past that, the next ordinary save tries again.
			if (Application.Current?.Dispatcher == null || this._consecutiveSaveFailures > ConfigurationStorage.MaxQuickRetries)
			{
				return;
			}

			if (this._saveRetryTimer == null)
			{
				this._saveRetryTimer = new DispatcherTimer { Interval = ConfigurationStorage.SaveRetryDelay };
				this._saveRetryTimer.Tick += (_, _) =>
				{
					this._saveRetryTimer.Stop();
					this.Save();
				};
			}

			this._saveRetryTimer.Stop();
			this._saveRetryTimer.Start();
		}

		private static void Log(string message)
		{
			try
			{
				File.AppendAllText(Path.Combine(AppContext.BaseDirectory, ConfigurationStorage.LOG_FILE_NAME),
					$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// The log lives next to the config; if that folder can't be written to either, there's nowhere to say so.
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

			string previousConfigFileName = this._appConfig.ConfigFileName;
			this._appConfig.ConfigFileName = Path.IsPathRooted(pathOrFileName)
				? pathOrFileName
				: Path.GetFileName(pathOrFileName);

			try
			{
				this.Load();
			}
			catch
			{
				// Unreadable profile: go back to the one that was just saved, rather than stay pointed
				// at the broken file with half-loaded settings (the next save would overwrite it).
				this._appConfig.ConfigFileName = previousConfigFileName;
				this.Load();
				throw;
			}
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
