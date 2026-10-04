using System;

namespace EveFPreview.Services
{
	/// <summary>
	/// Facts about the EVE Online client that several parts of the app rely on: its process name,
	/// and its window title - "EVE - Character Name" once logged in ("EVE Frontier - Name" for
	/// Frontier), just "EVE" at the login screen.
	/// </summary>
	static class EveClient
	{
		/// <summary>The game client's process name (exefile.exe), as Windows reports it.</summary>
		public const string ProcessName = "exefile";

		/// <summary>The window title of a client sitting at the login / character selection screen.</summary>
		public const string LoginScreenTitle = "EVE";

		private const string TitlePrefix = "EVE - ";
		private const string FrontierTitlePrefix = "EVE Frontier - ";

		/// <summary>The character name in a logged-in client's window title; false for the login screen or any other title.</summary>
		public static bool TryGetCharacterName(string windowTitle, out string characterName)
		{
			characterName = EveClient.TryStripPrefix(windowTitle, out string name) ? name : null;
			return !string.IsNullOrEmpty(characterName);
		}

		/// <summary>The window title without its "EVE - " / "EVE Frontier - " prefix; the title unchanged when it has none.</summary>
		public static string StripTitlePrefix(string windowTitle)
		{
			return EveClient.TryStripPrefix(windowTitle, out string name) ? name : windowTitle;
		}

		private static bool TryStripPrefix(string windowTitle, out string name)
		{
			name = null;
			if (string.IsNullOrWhiteSpace(windowTitle))
			{
				return false;
			}

			if (windowTitle.StartsWith(EveClient.FrontierTitlePrefix, StringComparison.OrdinalIgnoreCase))
			{
				name = windowTitle.Substring(EveClient.FrontierTitlePrefix.Length).Trim();
				return true;
			}

			if (windowTitle.StartsWith(EveClient.TitlePrefix, StringComparison.OrdinalIgnoreCase))
			{
				name = windowTitle.Substring(EveClient.TitlePrefix.Length).Trim();
				return true;
			}

			return false;
		}
	}
}
