using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace EveFPreview.Services
{
	public sealed class WormholeSystemInfo
	{
		[JsonProperty("class")]
		public string Class { get; set; }

		[JsonProperty("statics")]
		public List<string> Statics { get; set; }
	}

	/// <summary>
	/// Per-J-system class + static wormhole destinations (e.g. "J154516" -> Class C2,
	/// statics leading to HS and C4). See Data/WormholeStatics.README.md for provenance -
	/// this is community-compiled data neither CCP's SDE nor ESI carry.
	/// </summary>
	public static class WormholeStatics
	{
		private const string ResourceName = "EveFPreview.Data.WormholeStatics.json";

		private static readonly Dictionary<string, WormholeSystemInfo> Systems = Load();

		/// <summary>True if <paramref name="systemName"/> is a known wormhole system, with its class and statics.</summary>
		public static bool TryGet(string systemName, out WormholeSystemInfo info)
		{
			info = null;
			return !string.IsNullOrEmpty(systemName) && Systems.TryGetValue(systemName, out info);
		}

		private static Dictionary<string, WormholeSystemInfo> Load()
		{
			try
			{
				Assembly assembly = typeof(WormholeStatics).Assembly;
				using Stream stream = assembly.GetManifestResourceStream(ResourceName);
				if (stream == null)
				{
					return new Dictionary<string, WormholeSystemInfo>();
				}

				using var reader = new StreamReader(stream);
				string json = reader.ReadToEnd();
				return JsonConvert.DeserializeObject<Dictionary<string, WormholeSystemInfo>>(json)
					?? new Dictionary<string, WormholeSystemInfo>();
			}
			catch
			{
				// Missing/corrupt data must never break the overlay - just show no statics.
				return new Dictionary<string, WormholeSystemInfo>();
			}
		}
	}
}
