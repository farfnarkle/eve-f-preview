using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace EveFPreview.View
{
	/// <summary>
	/// Small always-on-top popup announcing a newer release: a message, the release notes, a
	/// "View release" button, a dismiss button and a "don't show again" checkbox.
	/// </summary>
	public partial class UpdateAvailableWindow : Window
	{
		private static readonly Regex MarkdownLinkRegex = new Regex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);

		private readonly string _releaseUrl;
		private readonly Action<string> _openUrl;

		/// <summary>True if the user ticked "don't show again" when closing.</summary>
		public bool DontShowAgain => this.DontShowAgainCheckBox.IsChecked == true;

		public UpdateAvailableWindow(string currentVersion, string newVersion, string releaseUrl, string releaseNotes, Action<string> openUrl)
		{
			this._releaseUrl = releaseUrl;
			this._openUrl = openUrl;

			this.InitializeComponent();

			this.MessageText.Text = "A new version of EVE-F-Preview is available: " + newVersion
				+ (string.IsNullOrWhiteSpace(currentVersion) ? string.Empty : " (you have " + currentVersion + ")")
				+ ".\nUpdating is manual - download it from the release page.";

			if (!string.IsNullOrWhiteSpace(releaseNotes))
			{
				UpdateAvailableWindow.RenderReleaseNotes(this.ReleaseNotesText.Inlines, releaseNotes);
				this.ReleaseNotesPanel.Visibility = Visibility.Visible;
			}
		}

		private void ViewReleaseButton_Click(object sender, RoutedEventArgs e)
		{
			this._openUrl?.Invoke(this._releaseUrl);
			this.Close();
		}

		private void LaterButton_Click(object sender, RoutedEventArgs e)
		{
			// Shown modeless, so IsCancel alone doesn't close it (it only sets DialogResult for ShowDialog).
			this.Close();
		}

		/// <summary>
		/// Renders the small subset of GitHub markdown the release notes use - headings, bullets and
		/// **bold** - as plain text runs. Links are reduced to their text on purpose: the only thing
		/// in this popup that opens a browser is the (validated) "View release" button.
		/// </summary>
		private static void RenderReleaseNotes(InlineCollection inlines, string markdown)
		{
			bool isFirstLine = true;
			bool pendingGap = false;

			foreach (string rawLine in markdown.Split('\n'))
			{
				string line = rawLine.TrimEnd();
				if (line.Length == 0)
				{
					pendingGap = !isFirstLine;
					continue;
				}

				if (!isFirstLine)
				{
					inlines.Add(new LineBreak());
					if (pendingGap)
					{
						inlines.Add(new LineBreak());
					}
				}

				isFirstLine = false;
				pendingGap = false;

				string text = line.TrimStart();
				if (text.StartsWith("#", StringComparison.Ordinal))
				{
					inlines.Add(new Bold(new Run(UpdateAvailableWindow.StripInlineMarkdown(text.TrimStart('#').Trim().Replace("**", string.Empty)))));
					continue;
				}

				int indent = line.Length - text.Length;
				if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
				{
					inlines.Add(new Run(new string(' ', indent) + "•  "));
					text = text.Substring(2);
				}

				UpdateAvailableWindow.AddInlineMarkdown(inlines, text);
			}
		}

		private static void AddInlineMarkdown(InlineCollection inlines, string text)
		{
			string[] parts = text.Split(new[] { "**" }, StringSplitOptions.None);

			// An unbalanced ** is just literal text, not the start of a bold span.
			if (parts.Length % 2 == 0)
			{
				inlines.Add(new Run(UpdateAvailableWindow.StripInlineMarkdown(text)));
				return;
			}

			for (int i = 0; i < parts.Length; i++)
			{
				if (parts[i].Length == 0)
				{
					continue;
				}

				Run run = new Run(UpdateAvailableWindow.StripInlineMarkdown(parts[i]));
				inlines.Add(i % 2 == 1 ? new Bold(run) : run);
			}
		}

		private static string StripInlineMarkdown(string text)
		{
			return UpdateAvailableWindow.MarkdownLinkRegex.Replace(text, "$1").Replace("`", string.Empty);
		}
	}
}
