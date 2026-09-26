using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EveFPreview.Configuration;

namespace EveFPreview.View
{
	/// <summary>
	/// Lets the user assign clients (by EVE window title) to one of the five cycle groups and set
	/// their cycle order, instead of having to hand-edit CycleGroupNClientsOrder in the config file.
	/// </summary>
	public sealed partial class CycleGroupsSettingsControl : UserControl
	{
		private const int GroupCount = 5;
		private const string LoginClientTitle = "EVE";

		private static readonly string[] GroupChoices =
		{
			"None", "Group 1", "Group 2", "Group 3", "Group 4", "Group 5"
		};

		private readonly ObservableCollection<CycleGroupRow> _rows = new ObservableCollection<CycleGroupRow>();

		private IThumbnailConfiguration _configuration;
		private List<string> _activeClientTitles = new List<string>();
		private bool _suppressPersist;
		private bool _isAdjustingRow;

		public Action PersistConfiguration { get; set; }

		public CycleGroupsSettingsControl()
		{
			this.InitializeComponent();
			this.ClientsGrid.ItemsSource = this._rows;
		}

		public void SetConfiguration(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
			this.RefreshGridFromConfiguration();
		}

		/// <summary>Called whenever the set of currently running clients changes, to keep the "add running client" picker current.</summary>
		public void SetActiveClientTitles(IEnumerable<string> titles)
		{
			this._activeClientTitles = (titles ?? Enumerable.Empty<string>())
				.Where(title => !string.IsNullOrWhiteSpace(title) && title != CycleGroupsSettingsControl.LoginClientTitle)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
				.ToList();

			this.RefreshActiveClientChoices();
		}

		private void RefreshGridFromConfiguration()
		{
			this._suppressPersist = true;
			try
			{
				this.ClearRows();

				if (this._configuration == null)
				{
					return;
				}

				var merged = new Dictionary<string, (int Group, int Order)>(StringComparer.OrdinalIgnoreCase);
				this.MergeGroupEntries(merged, this._configuration.CycleGroup1ClientsOrder, 1);
				this.MergeGroupEntries(merged, this._configuration.CycleGroup2ClientsOrder, 2);
				this.MergeGroupEntries(merged, this._configuration.CycleGroup3ClientsOrder, 3);
				this.MergeGroupEntries(merged, this._configuration.CycleGroup4ClientsOrder, 4);
				this.MergeGroupEntries(merged, this._configuration.CycleGroup5ClientsOrder, 5);

				foreach (KeyValuePair<string, (int Group, int Order)> entry in merged
					.OrderBy(x => x.Value.Group)
					.ThenBy(x => x.Value.Order)
					.ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
				{
					this.AddRow(entry.Key, CycleGroupsSettingsControl.GroupChoices[entry.Value.Group], entry.Value.Order.ToString());
				}
			}
			finally
			{
				this._suppressPersist = false;
			}

			this.RefreshActiveClientChoices();
		}

		private void MergeGroupEntries(Dictionary<string, (int Group, int Order)> merged, Dictionary<string, int> source, int group)
		{
			if (source == null)
			{
				return;
			}

			foreach (KeyValuePair<string, int> entry in source)
			{
				if (string.IsNullOrWhiteSpace(entry.Key) || merged.ContainsKey(entry.Key))
				{
					continue;
				}

				merged[entry.Key] = (group, entry.Value);
			}
		}

		private void AddRow(string client, string group, string order)
		{
			var row = new CycleGroupRow(client, group, order);
			row.PropertyChanged += this.Row_PropertyChanged;
			this._rows.Add(row);
		}

		private void RemoveRow(CycleGroupRow row)
		{
			row.PropertyChanged -= this.Row_PropertyChanged;
			this._rows.Remove(row);
		}

		private void ClearRows()
		{
			foreach (CycleGroupRow row in this._rows)
			{
				row.PropertyChanged -= this.Row_PropertyChanged;
			}

			this._rows.Clear();
		}

		private void RefreshActiveClientChoices()
		{
			var alreadyListed = new HashSet<string>(this._rows.Select(row => row.Client), StringComparer.OrdinalIgnoreCase);

			string previousSelection = this.ActiveClientCombo.SelectedItem as string;

			List<string> choices = this._activeClientTitles.Where(title => !alreadyListed.Contains(title)).ToList();
			this.ActiveClientCombo.ItemsSource = choices;

			int index = previousSelection != null ? choices.IndexOf(previousSelection) : -1;
			if (index < 0 && choices.Count > 0)
			{
				index = 0;
			}

			this.ActiveClientCombo.SelectedIndex = index;
			this.AddActiveClientButton.IsEnabled = choices.Count > 0;
		}

		private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (this._suppressPersist || this._isAdjustingRow || sender is not CycleGroupRow row)
			{
				return;
			}

			this._isAdjustingRow = true;
			try
			{
				if (e.PropertyName == nameof(CycleGroupRow.Order))
				{
					if (!int.TryParse(row.Order, out _))
					{
						row.Order = "0";
					}
				}
				else if (e.PropertyName == nameof(CycleGroupRow.Group))
				{
					int groupIndex = Array.IndexOf(CycleGroupsSettingsControl.GroupChoices, row.Group);
					if (groupIndex > 0)
					{
						bool hasOrder = int.TryParse(row.Order, out int existingOrder) && existingOrder > 0;
						if (!hasOrder)
						{
							row.Order = this.GetNextOrderForGroup(groupIndex, row).ToString();
						}
					}
				}
				else if (e.PropertyName == nameof(CycleGroupRow.Client))
				{
					row.Client = row.Client?.Trim();
				}
			}
			finally
			{
				this._isAdjustingRow = false;
			}

			this.PersistGridToConfiguration();
		}

		private int GetNextOrderForGroup(int groupIndex, CycleGroupRow excludeRow)
		{
			int max = 0;

			foreach (CycleGroupRow row in this._rows)
			{
				if (row == excludeRow || Array.IndexOf(CycleGroupsSettingsControl.GroupChoices, row.Group) != groupIndex)
				{
					continue;
				}

				if (int.TryParse(row.Order, out int order) && order > max)
				{
					max = order;
				}
			}

			return max + 1;
		}

		private void DeleteRowButton_Click(object sender, RoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.DataContext is not CycleGroupRow row)
			{
				return;
			}

			this.RemoveRow(row);
			this.RefreshActiveClientChoices();
			this.PersistGridToConfiguration();
		}

		private void AddActiveClientButton_Click(object sender, RoutedEventArgs e)
		{
			if (this.ActiveClientCombo.SelectedItem is string title)
			{
				this.AddClientRow(title);
			}
		}

		private void AddManualClientButton_Click(object sender, RoutedEventArgs e)
		{
			string title = this.ManualClientTextBox.Text?.Trim();
			if (string.IsNullOrEmpty(title))
			{
				return;
			}

			this.ManualClientTextBox.Clear();
			this.AddClientRow(title);
		}

		private void AddClientRow(string title)
		{
			bool alreadyPresent = this._rows.Any(row => string.Equals(row.Client, title, StringComparison.OrdinalIgnoreCase));

			if (alreadyPresent)
			{
				MessageBox.Show(Window.GetWindow(this), title + " is already in the list.", "Cycle groups", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			this.AddRow(title, CycleGroupsSettingsControl.GroupChoices[0], "0");
			this.RefreshActiveClientChoices();
			this.PersistGridToConfiguration();
		}

		private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
		{
			List<CycleGroupRow> rowsToRemove = this.ClientsGrid.SelectedItems.OfType<CycleGroupRow>().ToList();

			if (rowsToRemove.Count == 0)
			{
				return;
			}

			foreach (CycleGroupRow row in rowsToRemove)
			{
				this.RemoveRow(row);
			}

			this.RefreshActiveClientChoices();
			this.PersistGridToConfiguration();
		}

		private void PersistGridToConfiguration()
		{
			if (this._suppressPersist || this._configuration == null)
			{
				return;
			}

			var groups = new Dictionary<string, int>[CycleGroupsSettingsControl.GroupCount + 1];
			for (int i = 1; i <= CycleGroupsSettingsControl.GroupCount; i++)
			{
				groups[i] = new Dictionary<string, int>();
			}

			foreach (CycleGroupRow row in this._rows)
			{
				string client = row.Client?.Trim();
				if (string.IsNullOrEmpty(client))
				{
					continue;
				}

				int groupIndex = Array.IndexOf(CycleGroupsSettingsControl.GroupChoices, row.Group);
				if (groupIndex <= 0)
				{
					continue;
				}

				int.TryParse(row.Order, out int order);
				groups[groupIndex][client] = order;
			}

			this._configuration.CycleGroup1ClientsOrder = groups[1];
			this._configuration.CycleGroup2ClientsOrder = groups[2];
			this._configuration.CycleGroup3ClientsOrder = groups[3];
			this._configuration.CycleGroup4ClientsOrder = groups[4];
			this._configuration.CycleGroup5ClientsOrder = groups[5];

			this.PersistConfiguration?.Invoke();
		}

		/// <summary>One grid row. Editors in the cells write straight back into these properties.</summary>
		public sealed class CycleGroupRow : INotifyPropertyChanged
		{
			private string _client;
			private string _group;
			private string _order;

			public CycleGroupRow(string client, string group, string order)
			{
				this._client = client;
				this._group = group;
				this._order = order;
			}

			public event PropertyChangedEventHandler PropertyChanged;

			public IReadOnlyList<string> GroupChoices => CycleGroupsSettingsControl.GroupChoices;

			public string Client
			{
				get => this._client;
				set => this.Set(ref this._client, value, nameof(this.Client));
			}

			public string Group
			{
				get => this._group;
				set => this.Set(ref this._group, value, nameof(this.Group));
			}

			public string Order
			{
				get => this._order;
				set => this.Set(ref this._order, value, nameof(this.Order));
			}

			private void Set(ref string field, string value, string propertyName)
			{
				if (field == value)
				{
					return;
				}

				field = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			}
		}
	}
}
