// EveSettingsSync.cs
//
// RIFT-style EVE Online settings sync for Windows.
// Copies a source character's core_char_*.dat / core_user_*.dat onto selected
// destination character/account files under %LocalAppData%\CCP\EVE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EveFPreview.Services
{
	public enum EveSettingsSyncMode
	{
		Copy,
		Symlink
	}

	public enum EveSettingsBackupMode
	{
		/// <summary>Always create a new numbered file: *_sync_backup_N.dat</summary>
		Manual,
		/// <summary>Create a dated auto-backup and keep the newest 5 per file.</summary>
		Automatic
	}

	public class EveSettingsSyncOptions
	{
		public long SourceCharacterId { get; set; }
		public long SourceUserId { get; set; }
		/// <summary>
		/// Source character's display name (no "EVE - " prefix), used to scrub identity-leaking
		/// text (e.g. edit history) out of copied core_user files. When empty, core_user is copied
		/// raw and a warning is reported.
		/// </summary>
		public string SourceCharacterName { get; set; }
		public IList<long> DestinationCharacterIds { get; set; } = new List<long>();
		public IList<long> DestinationUserIds { get; set; } = new List<long>();
		/// <summary>Channel keys (e.g. player_…) to remove from copied core_char files. Builtins are never removed.</summary>
		public IList<string> ChannelKeysToStrip { get; set; } = new List<string>();

		/// <summary>
		/// Keep the destination's own ship module layout (core_user ui.slotOrder) and per-module
		/// state (core_char auto-repeat / auto-reload) instead of overwriting them with the source's,
		/// whose ship and module itemIDs belong to the source's own ships. Copy mode only.
		/// </summary>
		public bool PreserveModuleState { get; set; } = true;
		public EveSettingsSyncMode Mode { get; set; } = EveSettingsSyncMode.Copy;
		public bool DryRun { get; set; }

		/// <summary>
		/// When set (e.g. settings_Farfnarkle), only that profile folder is synced.
		/// When null/empty, all settings_* profiles are scanned (legacy).
		/// </summary>
		public string ProfileName { get; set; }

		public string EveDataRoot { get; set; } =
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");

		public string ServerFolderPattern { get; set; } = "*_tranquility";
	}

	public class EveSettingsFileInfo
	{
		public string Type; // "char" or "user"
		public long Id;
		public string Path;
		public DateTime LastWrite;
		public string Profile;
	}

	public class EveSettingsSyncReport
	{
		public List<string> Actions = new List<string>();
		public List<string> Warnings = new List<string>();
		public int FilesSynced;
		public int FilesBackedUp;
	}

	public class EveSettingsSync
	{
		private static readonly Regex CharRegex = new Regex(@"^core_char_(\d+)\.dat$", RegexOptions.IgnoreCase);
		private static readonly Regex UserRegex = new Regex(@"^core_user_(\d+)\.dat$", RegexOptions.IgnoreCase);
		private const string ManualBackupMarker = "_sync_backup_";
		private const string AutomaticBackupMarker = "_sync_auto_backup";
		private const int AutomaticBackupRetainCount = 5;

		private readonly EveSettingsSyncOptions _options;

		public EveSettingsSync(EveSettingsSyncOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.SourceCharacterId <= 0)
			{
				throw new ArgumentException("SourceCharacterId is required");
			}

			if (options.SourceUserId <= 0)
			{
				throw new ArgumentException("SourceUserId is required");
			}

			_options = options;
		}

		public static bool IsEveRunning()
		{
			string[] names = { "exefile", "evelauncher", "eve" };
			return names.Any(n => Process.GetProcessesByName(n).Length > 0);
		}

		public static IEnumerable<EveSettingsFileInfo> DiscoverSettingsFiles(string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			string root = eveDataRoot ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");
			var results = new List<EveSettingsFileInfo>();
			if (!Directory.Exists(root))
			{
				return results;
			}

			foreach (string profileDir in EnumerateProfileDirs(root, serverPattern))
			{
				foreach (string file in Directory.EnumerateFiles(profileDir, "core_*.dat"))
				{
					string name = Path.GetFileName(file);
					if (IsBackupFileName(name))
					{
						continue;
					}

					Match charMatch = CharRegex.Match(name);
					Match userMatch = UserRegex.Match(name);
					if (!charMatch.Success && !userMatch.Success)
					{
						continue;
					}

					results.Add(new EveSettingsFileInfo
					{
						Type = charMatch.Success ? "char" : "user",
						Id = long.Parse((charMatch.Success ? charMatch : userMatch).Groups[1].Value),
						Path = file,
						LastWrite = File.GetLastWriteTimeUtc(file),
						Profile = Path.GetFileName(profileDir)
					});
				}
			}

			return results.OrderByDescending(f => f.LastWrite);
		}

		/// <summary>EVE settings profile folder names (e.g. settings_Farfnarkle), excluding backup folders.</summary>
		public static IList<string> DiscoverProfileNames(string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			string root = eveDataRoot ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");
			var names = new List<string>();
			if (!Directory.Exists(root))
			{
				return names;
			}

			foreach (string profileDir in EnumerateProfileDirs(root, serverPattern))
			{
				names.Add(Path.GetFileName(profileDir));
			}

			return names
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>Full path to a settings_* profile folder, or null if not found.</summary>
		public static string FindProfileDirectory(string profileName, string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			if (string.IsNullOrWhiteSpace(profileName))
			{
				return null;
			}

			string root = eveDataRoot ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");
			if (!Directory.Exists(root))
			{
				return null;
			}

			foreach (string profileDir in EnumerateProfileDirs(root, serverPattern))
			{
				if (string.Equals(Path.GetFileName(profileDir), profileName, StringComparison.OrdinalIgnoreCase))
				{
					return profileDir;
				}
			}

			return null;
		}

		/// <summary>EVE LocalAppData CCP\EVE root used for settings discovery.</summary>
		public static string GetEveDataRoot(string eveDataRoot = null)
		{
			return eveDataRoot ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");
		}

		public static HashSet<long> GetCharacterIdsInProfile(string profileName, string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			return DiscoverSettingsFiles(eveDataRoot, serverPattern)
				.Where(f => f.Type == "char"
					&& (string.IsNullOrEmpty(profileName)
						|| string.Equals(f.Profile, profileName, StringComparison.OrdinalIgnoreCase)))
				.Select(f => f.Id)
				.ToHashSet();
		}

		public static HashSet<long> GetUserIdsInProfile(string profileName, string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			return DiscoverSettingsFiles(eveDataRoot, serverPattern)
				.Where(f => f.Type == "user"
					&& (string.IsNullOrEmpty(profileName)
						|| string.Equals(f.Profile, profileName, StringComparison.OrdinalIgnoreCase)))
				.Select(f => f.Id)
				.ToHashSet();
		}

		/// <summary>
		/// Back up every core_char/core_user file.
		/// Manual: always a new numbered *_sync_backup_N.dat.
		/// Automatic: dated *_sync_auto_backup_yyyyMMdd_HHmmss.dat, keep newest 5.
		/// </summary>
		public static EveSettingsSyncReport BackupAll(
			string eveDataRoot = null,
			string serverPattern = "*_tranquility",
			bool dryRun = false,
			EveSettingsBackupMode mode = EveSettingsBackupMode.Manual)
		{
			var report = new EveSettingsSyncReport();
			string root = eveDataRoot ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");

			if (!Directory.Exists(root))
			{
				report.Warnings.Add("EVE data folder not found: " + root);
				return report;
			}

			foreach (string profileDir in EnumerateProfileDirs(root, serverPattern))
			{
				foreach (string file in Directory.EnumerateFiles(profileDir, "core_*.dat"))
				{
					string name = Path.GetFileName(file);
					if (IsBackupFileName(name))
					{
						continue;
					}

					if (!CharRegex.IsMatch(name) && !UserRegex.IsMatch(name))
					{
						continue;
					}

					BackupFile(file, report, dryRun, mode);
				}
			}

			return report;
		}

		public EveSettingsSyncReport Run()
		{
			var report = new EveSettingsSyncReport();

			if (!Directory.Exists(_options.EveDataRoot))
			{
				report.Warnings.Add("EVE data folder not found: " + _options.EveDataRoot);
				return report;
			}

			IList<long> destChars = (_options.DestinationCharacterIds ?? Array.Empty<long>())
				.Where(id => id > 0 && id != _options.SourceCharacterId)
				.Distinct()
				.ToList();
			IList<long> destUsers = (_options.DestinationUserIds ?? Array.Empty<long>())
				.Where(id => id > 0 && id != _options.SourceUserId)
				.Distinct()
				.ToList();

			if (destChars.Count == 0 && destUsers.Count == 0)
			{
				report.Warnings.Add("No destination characters selected.");
				return report;
			}

			if (_options.Mode == EveSettingsSyncMode.Symlink
				&& _options.ChannelKeysToStrip != null
				&& _options.ChannelKeysToStrip.Count > 0)
			{
				report.Warnings.Add("Channel stripping requires Copy mode; refusing Symlink sync with channels selected.");
				return report;
			}

			if (_options.Mode == EveSettingsSyncMode.Symlink && _options.PreserveModuleState)
			{
				report.Warnings.Add("Symlinked characters share one settings file, so ship module layout and auto-repeat / auto-reload cannot be kept separately.");
			}

			foreach (string profileDir in EnumerateProfileDirs(_options.EveDataRoot, _options.ServerFolderPattern))
			{
				if (!string.IsNullOrEmpty(_options.ProfileName)
					&& !string.Equals(Path.GetFileName(profileDir), _options.ProfileName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				SyncType(profileDir, "core_char_", _options.SourceCharacterId, destChars, report);
				SyncType(profileDir, "core_user_", _options.SourceUserId, destUsers, report);
			}

			return report;
		}

		private static IEnumerable<string> EnumerateProfileDirs(string root, string serverPattern)
		{
			foreach (string serverDir in Directory.EnumerateDirectories(root, serverPattern))
			{
				foreach (string profileDir in Directory.EnumerateDirectories(serverDir, "settings_*"))
				{
					if (Path.GetFileName(profileDir).IndexOf("backup", StringComparison.OrdinalIgnoreCase) < 0)
					{
						yield return profileDir;
					}
				}
			}
		}

		private void SyncType(string profileDir, string prefix, long sourceId, IList<long> destinationIds, EveSettingsSyncReport report)
		{
			string profileName = Path.GetFileName(profileDir);
			string source = Path.Combine(profileDir, prefix + sourceId + ".dat");
			if (!File.Exists(source))
			{
				report.Warnings.Add("No " + Path.GetFileName(source) + " in " + profileName + ", skipping");
				return;
			}

			foreach (long destId in destinationIds)
			{
				string target = Path.Combine(profileDir, prefix + destId + ".dat");
				if (!File.Exists(target))
				{
					report.Warnings.Add("Missing destination " + Path.GetFileName(target) + " in " + profileName);
					continue;
				}

				string name = Path.GetFileName(target);
				bool isChar = prefix == "core_char_";
				bool stripChannels = isChar
					&& _options.ChannelKeysToStrip != null
					&& _options.ChannelKeysToStrip.Count > 0;

				if (_options.Mode == EveSettingsSyncMode.Copy)
				{
					if (IsSymlink(target))
					{
						report.Actions.Add("skip (symlink): " + name);
						continue;
					}

					BackupFile(target, report, _options.DryRun, EveSettingsBackupMode.Automatic);
					if (isChar)
					{
						string actionLabel = stripChannels
							? "copy+strip-channels " + Path.GetFileName(source) + " -> " + name
							: "copy+sanitize " + Path.GetFileName(source) + " -> " + name;
						Do(report, actionLabel, () =>
						{
							byte[] blob = EveChatChannelTools.PrepareCoreCharCopy(
								source,
								target,
								_options.ChannelKeysToStrip,
								_options.PreserveModuleState,
								out IList<string> removed,
								out IList<string> sanitized,
								out IList<string> preserved);
							File.WriteAllBytes(target, blob);
							if (removed.Count > 0)
							{
								report.Actions.Add("  stripped: " + string.Join(", ", removed));
							}

							if (sanitized.Count > 0)
							{
								report.Actions.Add("  cleared: " + string.Join(", ", sanitized));
							}

							if (preserved.Count > 0)
							{
								report.Actions.Add("  kept module state: " + string.Join(", ", preserved));
							}
						});
					}
				else if (!string.IsNullOrWhiteSpace(_options.SourceCharacterName) || _options.PreserveModuleState)
				{
					if (string.IsNullOrWhiteSpace(_options.SourceCharacterName))
					{
						report.Warnings.Add("Source character name unknown; edit history in " + Path.GetFileName(source) + " not sanitized.");
					}

					Do(report, "copy+sanitize " + Path.GetFileName(source) + " -> " + name, () =>
					{
						byte[] blob = EveChatChannelTools.PrepareCoreUserCopy(
							source,
							target,
							_options.SourceCharacterName,
							_options.PreserveModuleState,
							out IList<string> sanitized,
							out IList<string> preserved);
						File.WriteAllBytes(target, blob);
						if (sanitized.Count > 0)
						{
							report.Actions.Add("  cleared: " + string.Join(", ", sanitized));
						}

						if (preserved.Count > 0)
						{
							report.Actions.Add("  kept ship HUD state: " + string.Join(", ", preserved));
						}
					});
				}
				else
				{
					report.Warnings.Add("Source character name unknown; copying " + Path.GetFileName(source) + " raw (edit history not sanitized).");
					Do(report, "copy " + Path.GetFileName(source) + " -> " + name,
						() => File.Copy(source, target, true));
				}
			}
			else
			{
				if (IsSymlink(target))
					{
						Do(report, "relink " + name + " -> " + Path.GetFileName(source), () =>
						{
							File.Delete(target);
							CreateSymlink(target, source);
						});
					}
					else
					{
						BackupFile(target, report, _options.DryRun, EveSettingsBackupMode.Automatic);
						Do(report, "link " + name + " -> " + Path.GetFileName(source), () =>
						{
							File.Delete(target);
							CreateSymlink(target, source);
						});
					}
				}

				report.FilesSynced++;
			}
		}

		private static bool IsBackupFileName(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
			{
				return false;
			}

			return fileName.IndexOf(ManualBackupMarker, StringComparison.OrdinalIgnoreCase) >= 0
				|| fileName.IndexOf(AutomaticBackupMarker, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool IsAutomaticBackupFileName(string fileName)
		{
			return !string.IsNullOrEmpty(fileName)
				&& fileName.IndexOf(AutomaticBackupMarker, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Deletes *_sync_backup_N.dat and *_sync_auto_backup*.dat under the given profile
		/// (or all profiles when profileName is null/empty). Live core_char/core_user files are untouched.
		/// </summary>
		public static EveSettingsSyncReport DeleteBackups(string profileName = null, string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			var report = new EveSettingsSyncReport();
			string root = GetEveDataRoot(eveDataRoot);
			if (!Directory.Exists(root))
			{
				report.Warnings.Add("EVE data folder not found: " + root);
				return report;
			}

			foreach (string profileDir in EnumerateProfileDirs(root, serverPattern))
			{
				if (!string.IsNullOrEmpty(profileName)
					&& !string.Equals(Path.GetFileName(profileDir), profileName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				foreach (string file in Directory.EnumerateFiles(profileDir, "core_*.dat"))
				{
					string name = Path.GetFileName(file);
					if (!IsBackupFileName(name))
					{
						continue;
					}

					try
					{
						File.Delete(file);
						report.FilesBackedUp++; // reused as "files deleted" count for this report
						report.Actions.Add("deleted " + name);
					}
					catch (Exception ex)
					{
						report.Warnings.Add("delete " + name + " FAILED: " + ex.Message);
					}
				}
			}

			return report;
		}

		public static int CountBackupFiles(string profileName = null, string eveDataRoot = null, string serverPattern = "*_tranquility")
		{
			string root = GetEveDataRoot(eveDataRoot);
			if (!Directory.Exists(root))
			{
				return 0;
			}

			int count = 0;
			foreach (string profileDir in EnumerateProfileDirs(root, serverPattern))
			{
				if (!string.IsNullOrEmpty(profileName)
					&& !string.Equals(Path.GetFileName(profileDir), profileName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				foreach (string file in Directory.EnumerateFiles(profileDir, "core_*.dat"))
				{
					if (IsBackupFileName(Path.GetFileName(file)))
					{
						count++;
					}
				}
			}

			return count;
		}

		private static void BackupFile(string file, EveSettingsSyncReport report, bool dryRun, EveSettingsBackupMode mode)
		{
			string dir = Path.GetDirectoryName(file);
			string stem = Path.GetFileNameWithoutExtension(file);

			if (mode == EveSettingsBackupMode.Automatic)
			{
				string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string backupName = stem + AutomaticBackupMarker + "_" + stamp + ".dat";
				string backup = Path.Combine(dir, backupName);
				Do(report, "auto-backup " + Path.GetFileName(file) + " -> " + backupName,
					() => File.Copy(file, backup, overwrite: false), dryRun);
				report.FilesBackedUp++;
				PruneAutomaticBackups(dir, stem, report, dryRun);
				return;
			}

			for (int n = 1; ; n++)
			{
				string backup = Path.Combine(dir, stem + ManualBackupMarker + n + ".dat");
				if (File.Exists(backup))
				{
					continue;
				}

				Do(report, "backup " + Path.GetFileName(file) + " -> " + Path.GetFileName(backup),
					() => File.Copy(file, backup), dryRun);
				report.FilesBackedUp++;
				return;
			}
		}

		/// <summary>
		/// Keep the newest AutomaticBackupRetainCount auto-backups for a live settings stem; delete older ones.
		/// </summary>
		private static void PruneAutomaticBackups(string dir, string stem, EveSettingsSyncReport report, bool dryRun)
		{
			if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem) || !Directory.Exists(dir))
			{
				return;
			}

			string pattern = stem + AutomaticBackupMarker + "*.dat";
			List<FileInfo> autoBackups = Directory.EnumerateFiles(dir, pattern)
				.Select(path => new FileInfo(path))
				.Where(fi => IsAutomaticBackupFileName(fi.Name))
				.OrderBy(fi => fi.LastWriteTimeUtc)
				.ThenBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			int removeCount = autoBackups.Count - AutomaticBackupRetainCount;
			for (int i = 0; i < removeCount; i++)
			{
				FileInfo oldest = autoBackups[i];
				Do(report, "prune auto-backup " + oldest.Name, () => oldest.Delete(), dryRun);
			}
		}

		private void Do(EveSettingsSyncReport report, string description, Action action)
		{
			Do(report, description, action, _options.DryRun);
		}

		private static void Do(EveSettingsSyncReport report, string description, Action action, bool dryRun)
		{
			if (dryRun)
			{
				report.Actions.Add("[dry] " + description);
				return;
			}

			try
			{
				action();
				report.Actions.Add(description);
			}
			catch (Exception ex)
			{
				report.Warnings.Add(description + " FAILED: " + ex.Message);
			}
		}

		private static bool IsSymlink(string path)
		{
			return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
		}

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, uint dwFlags);

		private static void CreateSymlink(string linkPath, string targetPath)
		{
			const uint allowUnprivileged = 0x2;
			if (CreateSymbolicLinkW(linkPath, targetPath, allowUnprivileged))
			{
				return;
			}

			if (CreateSymbolicLinkW(linkPath, targetPath, 0))
			{
				return;
			}

			throw new IOException(
				"CreateSymbolicLink failed (error " + Marshal.GetLastWin32Error() +
				"). Run elevated or enable Windows Developer Mode, or use Copy mode.");
		}
	}
}
