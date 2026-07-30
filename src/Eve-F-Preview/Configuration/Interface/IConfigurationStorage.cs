using System.Collections.Generic;

namespace EveFPreview.Configuration
{
	public interface IConfigurationStorage
	{
		void Load();
		void Save();

		/// <summary>Full path to the config file that is currently active (loaded/saved).</summary>
		string ActiveConfigPath { get; }

		/// <summary>File names (not full paths) of config-looking *.json files found next to the executable.</summary>
		IReadOnlyList<string> ListConfigProfiles();

		/// <summary>Saves the current settings, then switches the active config to pathOrFileName and loads it.</summary>
		void SwitchTo(string pathOrFileName);

		/// <summary>Makes pathOrFileName the active config file name and saves the current settings into it.</summary>
		void SaveAs(string pathOrFileName);

		/// <summary>
		/// Reads an external EVE-O/EVE-F or EVE-X settings file, converts it into a new
		/// EVE-F-Preview config, and writes it to destinationFileName (relative to the exe
		/// directory unless rooted). Does not switch the active config.
		/// </summary>
		void ImportFrom(string sourcePath, string destinationFileName);
	}
}
