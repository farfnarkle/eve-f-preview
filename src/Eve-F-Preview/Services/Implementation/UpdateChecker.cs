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
		private const string RepositoryPath = "farfnarkle/eve-f-preview";
		private const string LatestReleaseApiUrl = "https://api.github.com/repos/" + RepositoryPath + "/releases/latest";
		private const string ReleasePagePathPrefix = "/" + RepositoryPath + "/releases/";

		// Release notes only need to give a feel for what changed; the full text is one click away.
		private const int MaxReleaseNotesLength = 4000;

		private static readonly HttpClient Client = CreateClient();

		public sealed class UpdateInfo
		{
			public string Tag { get; set; }
			public Version Version { get; set; }
			public string Url { get; set; }
			/// <summary>The release's description (GitHub markdown), or null if it has none.</summary>
			public string ReleaseNotes { get; set; }
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
				if (string.IsNullOrWhiteSpace(tag) || !UpdateChecker.IsTrustedReleaseUrl(url))
				{
					return null;
				}

				if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version latest) || latest <= current)
				{
					return null;
				}

				return new UpdateInfo
				{
					Tag = tag,
					Version = latest,
					Url = url,
					ReleaseNotes = UpdateChecker.TrimReleaseNotes(release.Value<string>("body"))
				};
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// The release URL is handed to the shell to open, so only accept this repository's own
		/// release pages on github.com over https - never another host, scheme, or repository.
		/// </summary>
		internal static bool IsTrustedReleaseUrl(string url)
		{
			return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
				&& uri.Scheme == Uri.UriSchemeHttps
				&& string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
				&& uri.IsDefaultPort
				&& string.IsNullOrEmpty(uri.UserInfo)
				&& uri.AbsolutePath.StartsWith(UpdateChecker.ReleasePagePathPrefix, StringComparison.OrdinalIgnoreCase);
		}

		private static string TrimReleaseNotes(string notes)
		{
			if (string.IsNullOrWhiteSpace(notes))
			{
				return null;
			}

			notes = notes.Replace("\r\n", "\n").Trim();
			if (notes.Length <= UpdateChecker.MaxReleaseNotesLength)
			{
				return notes;
			}

			// Cut at a line break so a bullet isn't chopped mid-sentence.
			int cut = notes.LastIndexOf('\n', UpdateChecker.MaxReleaseNotesLength);
			if (cut <= 0)
			{
				cut = UpdateChecker.MaxReleaseNotesLength;
			}

			return notes.Substring(0, cut).TrimEnd() + "\n\n…";
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
