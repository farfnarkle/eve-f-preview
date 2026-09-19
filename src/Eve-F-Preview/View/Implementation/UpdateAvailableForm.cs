using System;
using System.Drawing;
using System.Windows.Forms;

namespace EveFPreview.View
{
	/// <summary>
	/// Small always-on-top popup announcing a newer release. Built in code (no designer) since it's
	/// just a message, a "View release" button, a dismiss button and a "don't show again" checkbox.
	/// </summary>
	sealed class UpdateAvailableForm : Form
	{
		private readonly CheckBox _dontShowAgainCheckBox;

		/// <summary>True if the user ticked "don't show again" when closing.</summary>
		public bool DontShowAgain => this._dontShowAgainCheckBox.Checked;

		public UpdateAvailableForm(string currentVersion, string newVersion, string releaseUrl, Action<string> openUrl)
		{
			this.Text = "EVE-F-Preview update available";
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.StartPosition = FormStartPosition.CenterScreen;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.ShowInTaskbar = true;
			this.TopMost = true;
			this.ClientSize = new Size(400, 150);
			this.Padding = new Padding(14);

			Label message = new Label
			{
				AutoSize = false,
				Location = new Point(14, 14),
				Size = new Size(372, 48),
				Text = "A new version of EVE-F-Preview is available: " + newVersion
					+ (string.IsNullOrWhiteSpace(currentVersion) ? string.Empty : " (you have " + currentVersion + ")")
					+ ".\nUpdating is manual - download it from the release page."
			};

			this._dontShowAgainCheckBox = new CheckBox
			{
				AutoSize = true,
				Location = new Point(16, 74),
				Text = "Don't show again for this version"
			};

			Button viewButton = new Button
			{
				Text = "View release",
				Size = new Size(110, 28),
				Location = new Point(this.ClientSize.Width - 14 - 110 - 8 - 90, 108)
			};
			viewButton.Click += (s, e) =>
			{
				openUrl?.Invoke(releaseUrl);
				this.Close();
			};

			Button laterButton = new Button
			{
				Text = "Later",
				Size = new Size(90, 28),
				Location = new Point(this.ClientSize.Width - 14 - 90, 108)
			};
			// Not DialogResult.Cancel: this form is shown modeless (Show), where setting a
			// DialogResult doesn't reliably close it. Close explicitly instead (also covers Esc).
			laterButton.Click += (s, e) => this.Close();

			this.Controls.Add(message);
			this.Controls.Add(this._dontShowAgainCheckBox);
			this.Controls.Add(viewButton);
			this.Controls.Add(laterButton);
			this.AcceptButton = viewButton;
			this.CancelButton = laterButton;
		}
	}
}
