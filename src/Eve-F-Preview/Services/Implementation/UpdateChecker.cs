using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EveFPreview.Services
{
	/// <summary>
	/// Asks GitHub whether a newer release than the running build exists. Notify-only: it never
	/// downloads or installs anything. Any failure (offline, rate-limited, odd tag) just means "no update".
	/// </summary>
	static class UpdateChecker
	{
		private const string LatestReleaseApiUrl = "https://api.github.com/repos/farfnarkle/eve-f-preview/releases/latest";

		private static readonly HttpClient Client = CreateClient();

		public sealed class UpdateInfo
		{
			public string Tag { get; set; }
			public Version Version { get; set; }
			public string Url { get; set; }
		}

		/// <summary>The newer release if there is one, otherwise null.</summary>
		public static async Task<UpdateInfo> CheckAsync(Version current)
		{
			try
			{
				string json = await Client.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
				JObject release = JObject.Parse(json);

				if (release.Value<bool?>("draft") == true || release.Value<bool?>("prerelease") == true)
				{
					return null;
				}

				string tag = release.Value<string>("tag_name");
				string url = release.Value<string>("html_url");
				if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
				{
					return null;
				}

				if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version latest) || latest <= current)
				{
					return null;
				}

				return new UpdateInfo { Tag = tag, Version = latest, Url = url };
			}
			catch
			{
				return null;
			}
		}

		private static HttpClient CreateClient()
		{
			HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			// GitHub's API rejects requests without a User-Agent.
			client.DefaultRequestHeaders.UserAgent.ParseAdd("EVE-F-Preview-UpdateCheck");
			client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
			return client;
		}
	}
}
