using System;
using System.Windows;

namespace EveFPreview.View
{
	/// <summary>
	/// Manual fallback for when auto-detection (reading /LauncherData= off the running client's
	/// command line) can't find a character's account ID - e.g. it was launched via a quick-login
	/// shortcut that only passes /autoSelectCharacter:, with no account info in the command line.
	/// </summary>
	public partial class SettingsSyncAccountIdDialog : Window
	{
		public long AccountId { get; private set; }

		public SettingsSyncAccountIdDialog(string characterDisplayName, long currentAccountId)
		{
			this.InitializeComponent();

			this.PromptText.Text = $"Account ID for {characterDisplayName}:\n(shown in the EVE Launcher's account list, or Task Manager's command line for a running client)";
			this.AccountIdInput.Value = currentAccountId > 0 ? currentAccountId : 0;
			this.Loaded += (_, _) =>
			{
				this.Activate();
				this.AccountIdInput.Focus();
			};
		}

		private void SaveButton_Click(object sender, RoutedEventArgs e)
		{
			this.AccountId = (long)this.AccountIdInput.Value;
			this.DialogResult = true;
		}
	}
}
