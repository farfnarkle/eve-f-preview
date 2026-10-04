using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EveFPreview.Configuration;
using EveFPreview.Services;

namespace EveFPreview.View
{
	/// <summary>
	/// Lets the user add non-EVE programs (picked from the ones running now) to cycling. They're
	/// stored in IThumbnailConfiguration.CycleApps; ProcessMonitor picks up changes on its next tick.
	/// </summary>
	public sealed partial class CycleAppsSettingsControl : UserControl
	{
		private const string EveClientProcessName = EveClient.ProcessName;

		private readonly ObservableCollection<CycleAppRow> _rows = new ObservableCollection<CycleAppRow>();

		private IThumbnailConfiguration _configuration;

		public Action PersistConfiguration { get; set; }

		public CycleAppsSettingsControl()
		{
			this.InitializeComponent();
			this.AppsList.ItemsSource = this._rows;
			this.UpdateEmptyState();
		}

		public void SetConfiguration(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
			this.RefreshFromConfiguration();
		}

		private void RefreshFromConfiguration()
		{
			foreach (CycleAppRow row in this._rows)
			{
				row.PropertyChanged -= this.Row_PropertyChanged;
			}

			this._rows.Clear();

			foreach (CycleApp app in this._configuration?.CycleApps ?? Enumerable.Empty<CycleApp>())
			{
				var row = new CycleAppRow(app);
				row.PropertyChanged += this.Row_PropertyChanged;
				this._rows.Add(row);
			}

			this.UpdateEmptyState();
		}

		private void UpdateEmptyState()
		{
			this.NoAppsText.Visibility = this._rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		}

		private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			this.PersistConfiguration?.Invoke();
		}

		private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
		{
			if (this._configuration == null || (sender as FrameworkElement)?.DataContext is not CycleAppRow row)
			{
				return;
			}

			this._configuration.CycleApps.Remove(row.App);
			row.PropertyChanged -= this.Row_PropertyChanged;
			this._rows.Remove(row);
			this.UpdateEmptyState();
			this.PersistConfiguration?.Invoke();
			this.RefreshRunningPrograms();
		}

		private void AddAppButton_Click(object sender, RoutedEventArgs e)
		{
			if (this._configuration == null || this.RunningProgramsCombo.SelectedItem is not RunningProgram program)
			{
				return;
			}

			if (this._configuration.TryGetCycleApp(program.ProcessName, out _))
			{
				return;
			}

			// On by default - adding an app to cycling is what this page is for. Group-only use
			// (via the Cycle groups page) just means switching this off.
			var app = new CycleApp(program.ProcessName, includeInDynamicCycle: true);
			this._configuration.CycleApps.Add(app);

			var row = new CycleAppRow(app);
			row.PropertyChanged += this.Row_PropertyChanged;
			this._rows.Add(row);
			this.UpdateEmptyState();
			this.PersistConfiguration?.Invoke();
			this.RefreshRunningPrograms();
		}

		private void RefreshRunningProgramsButton_Click(object sender, RoutedEventArgs e)
		{
			this.RefreshRunningPrograms();
		}

		private void CycleAppsSettingsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (this.IsVisible)
			{
				this.RefreshRunningPrograms();
			}
		}

		private async void RefreshRunningPrograms()
		{
			var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CycleAppsSettingsControl.EveClientProcessName };
			foreach (CycleAppRow row in this._rows)
			{
				excluded.Add(row.Executable);
			}

			// Walking every process (and reading window titles) is slow enough to stall the UI.
			List<RunningProgram> programs = await Task.Run(() => CycleAppsSettingsControl.GetRunningPrograms(excluded));

			string previousSelection = (this.RunningProgramsCombo.SelectedItem as RunningProgram)?.ProcessName;
			this.RunningProgramsCombo.ItemsSource = programs;

			int index = programs.FindIndex(x => string.Equals(x.ProcessName, previousSelection, StringComparison.OrdinalIgnoreCase));
			this.RunningProgramsCombo.SelectedIndex = index >= 0 ? index : (programs.Count > 0 ? 0 : -1);
			this.AddAppButton.IsEnabled = programs.Count > 0;
		}

		private static List<RunningProgram> GetRunningPrograms(ISet<string> excluded)
		{
			int ownProcessId = Environment.ProcessId;
			var programs = new Dictionary<string, RunningProgram>(StringComparer.OrdinalIgnoreCase);

			foreach (Process process in Process.GetProcesses())
			{
				using (process)
				{
					try
					{
						if (process.Id == ownProcessId
							|| excluded.Contains(process.ProcessName)
							|| programs.ContainsKey(process.ProcessName)
							|| process.MainWindowHandle == IntPtr.Zero
							|| string.IsNullOrWhiteSpace(process.MainWindowTitle))
						{
							continue;
						}

						programs[process.ProcessName] = new RunningProgram(process.ProcessName, process.MainWindowTitle);
					}
					catch (InvalidOperationException)
					{
						// Exited while we were looking at it.
					}
				}
			}

			return programs.Values
				.OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>A picker entry. Public because WPF bindings (DisplayMemberPath) only see public types.</summary>
		public sealed class RunningProgram
		{
			public RunningProgram(string processName, string windowTitle)
			{
				this.ProcessName = processName;
				this.DisplayText = processName + "  -  " + windowTitle;
			}

			public string ProcessName { get; }

			public string DisplayText { get; }
		}

		/// <summary>One row in the apps list; the toggle writes straight through to the stored CycleApp.</summary>
		public sealed class CycleAppRow : INotifyPropertyChanged
		{
			public CycleAppRow(CycleApp app)
			{
				this.App = app;
			}

			public event PropertyChangedEventHandler PropertyChanged;

			public CycleApp App { get; }

			public string Executable => this.App.Executable;

			public bool IncludeInDynamicCycle
			{
				get => this.App.IncludeInDynamicCycle;
				set
				{
					if (this.App.IncludeInDynamicCycle == value)
					{
						return;
					}

					this.App.IncludeInDynamicCycle = value;
					this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IncludeInDynamicCycle)));
				}
			}
		}
	}
}
