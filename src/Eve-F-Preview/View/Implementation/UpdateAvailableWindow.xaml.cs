using System;
using System.Windows;

namespace EveFPreview.View
{
	/// <summary>
	/// Small always-on-top popup announcing a newer release: a message, a "View release" button, a
	/// dismiss button and a "don't show again" checkbox.
	/// </summary>
	public partial class UpdateAvailableWindow : Window
	{
		private readonly string _releaseUrl;
		private readonly Action<string> _openUrl;

		/// <summary>True if the user ticked "don't show again" when closing.</summary>
		public bool DontShowAgain => this.DontShowAgainCheckBox.IsChecked == true;

		public UpdateAvailableWindow(string currentVersion, string newVersion, string releaseUrl, Action<string> openUrl)
		{
			this._releaseUrl = releaseUrl;
			this._openUrl = openUrl;

			this.InitializeComponent();

			this.MessageText.Text = "A new version of EVE-F-Preview is available: " + newVersion
				+ (string.IsNullOrWhiteSpace(currentVersion) ? string.Empty : " (you have " + currentVersion + ")")
				+ ".\nUpdating is manual - download it from the release page.";
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
	}
}
