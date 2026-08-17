using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EveFPreview.Configuration;

namespace EveFPreview.View
{
	/// <summary>
	/// Lets the user assign clients (by EVE window title) to one of the five cycle groups and set
	/// their cycle order, instead of having to hand-edit CycleGroupNClientsOrder in the config file.
	/// </summary>
	sealed class CycleGroupsSettingsControl : UserControl
	{
		private const int GroupCount = 5;
		private const string LoginClientTitle = "EVE";

		private static readonly string[] GroupChoices =
		{
			"None", "Group 1", "Group 2", "Group 3", "Group 4", "Group 5"
		};

		private readonly DataGridView _grid;
		private readonly ComboBox _activeClientCombo;
		private readonly Button _addActiveClientButton;
		private readonly TextBox _manualClientTextBox;
		private readonly Button _addManualClientButton;
		private readonly Button _removeSelectedButton;

		private IThumbnailConfiguration _configuration;
		private List<string> _activeClientTitles = new List<string>();
		private bool _suppressPersist;
		private bool _isAdjustingCell;

		public Action PersistConfiguration { get; set; }

		public CycleGroupsSettingsControl()
		{
			this.SuspendLayout();
			this.AutoScaleMode = AutoScaleMode.Inherit;

			var root = new TableLayoutPanel
			{
				ColumnCount = 1,
				Dock = DockStyle.Fill,
				RowCount = 2
			};
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			this._grid = new DataGridView
			{
				AllowUserToAddRows = false,
				AllowUserToResizeRows = false,
				AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
				BackgroundColor = SystemColors.Window,
				BorderStyle = BorderStyle.FixedSingle,
				Dock = DockStyle.Fill,
				EditMode = DataGridViewEditMode.EditOnEnter,
				Margin = new Padding(4),
				MultiSelect = true,
				RowHeadersVisible = false,
				SelectionMode = DataGridViewSelectionMode.FullRowSelect
			};

			var clientColumn = new DataGridViewTextBoxColumn
			{
				FillWeight = 60,
				HeaderText = "Client (window title)",
				Name = "ClientColumn"
			};

			var groupColumn = new DataGridViewComboBoxColumn
			{
				// AutoComplete forces ComboBox.RecreateHandle on DPI font scaling.
				// That throws Win32Exception 1400 on mixed-DPI setups (e.g. 4K primary + 2K secondary).
				AutoComplete = false,
				FillWeight = 25,
				FlatStyle = FlatStyle.Flat,
				HeaderText = "Cycle group",
				Name = "GroupColumn"
			};
			groupColumn.Items.AddRange(CycleGroupsSettingsControl.GroupChoices);

			var orderColumn = new DataGridViewTextBoxColumn
			{
				FillWeight = 15,
				HeaderText = "Order",
				Name = "OrderColumn"
			};

			var deleteColumn = new DataGridViewButtonColumn
			{
				AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
				FlatStyle = FlatStyle.Flat,
				HeaderText = string.Empty,
				Name = "DeleteColumn",
				Resizable = DataGridViewTriState.False,
				SortMode = DataGridViewColumnSortMode.NotSortable,
				Text = "\U0001F5D1",
				UseColumnTextForButtonValue = true,
				Width = 30
			};

			this._grid.Columns.Add(clientColumn);
			this._grid.Columns.Add(groupColumn);
			this._grid.Columns.Add(orderColumn);
			this._grid.Columns.Add(deleteColumn);
			this._grid.CellValueChanged += this.Grid_CellValueChanged;
			this._grid.CurrentCellDirtyStateChanged += this.Grid_CurrentCellDirtyStateChanged;
			this._grid.CellContentClick += this.Grid_CellContentClick;
			this._grid.EditingControlShowing += this.Grid_EditingControlShowing;

			var bottomPanel = new FlowLayoutPanel
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				Padding = new Padding(6),
				WrapContents = false
			};

			var activeRow = new FlowLayoutPanel
			{
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false
			};
			this._activeClientCombo = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Margin = new Padding(0, 3, 4, 3),
				Width = 200
			};
			this._addActiveClientButton = new Button
			{
				AutoSize = true,
				Margin = new Padding(0, 3, 0, 3),
				Text = "Add running client",
				UseVisualStyleBackColor = true
			};
			this._addActiveClientButton.Click += this.AddActiveClientButton_Click;
			activeRow.Controls.Add(this._activeClientCombo);
			activeRow.Controls.Add(this._addActiveClientButton);

			var manualRow = new FlowLayoutPanel
			{
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false
			};
			this._manualClientTextBox = new TextBox
			{
				Margin = new Padding(0, 3, 4, 3),
				Width = 200
			};
			this._addManualClientButton = new Button
			{
				AutoSize = true,
				Margin = new Padding(0, 3, 0, 3),
				Text = "Add by exact window title",
				UseVisualStyleBackColor = true
			};
			this._addManualClientButton.Click += this.AddManualClientButton_Click;
			manualRow.Controls.Add(this._manualClientTextBox);
			manualRow.Controls.Add(this._addManualClientButton);

			this._removeSelectedButton = new Button
			{
				AutoSize = true,
				Margin = new Padding(0, 3, 0, 3),
				Text = "Remove selected",
				UseVisualStyleBackColor = true
			};
			this._removeSelectedButton.Click += this.RemoveSelectedButton_Click;

			var hintLabel = new Label
			{
				AutoSize = true,
				Margin = new Padding(0, 6, 0, 0),
				MaximumSize = new Size(300, 0),
				Text = "Assign each client to a cycle group and an order (lower cycles first). Group hotkeys are set on the Shortcuts tab."
			};

			bottomPanel.Controls.Add(activeRow);
			bottomPanel.Controls.Add(manualRow);
			bottomPanel.Controls.Add(this._removeSelectedButton);
			bottomPanel.Controls.Add(hintLabel);

			root.Controls.Add(this._grid, 0, 0);
			root.Controls.Add(bottomPanel, 0, 1);
			this.Controls.Add(root);

			this.ResumeLayout(false);
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
				this._grid.Rows.Clear();

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
					this._grid.Rows.Add(entry.Key, CycleGroupsSettingsControl.GroupChoices[entry.Value.Group], entry.Value.Order);
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

		private void RefreshActiveClientChoices()
		{
			var alreadyListed = new HashSet<string>(
				this._grid.Rows.Cast<DataGridViewRow>()
					.Where(row => !row.IsNewRow)
					.Select(row => Convert.ToString(row.Cells["ClientColumn"].Value)),
				StringComparer.OrdinalIgnoreCase);

			string previousSelection = this._activeClientCombo.SelectedItem as string;

			this._activeClientCombo.Items.Clear();
			foreach (string title in this._activeClientTitles)
			{
				if (!alreadyListed.Contains(title))
				{
					this._activeClientCombo.Items.Add(title);
				}
			}

			if (previousSelection != null)
			{
				int index = this._activeClientCombo.Items.IndexOf(previousSelection);
				if (index >= 0)
				{
					this._activeClientCombo.SelectedIndex = index;
				}
			}

			if (this._activeClientCombo.SelectedIndex < 0 && this._activeClientCombo.Items.Count > 0)
			{
				this._activeClientCombo.SelectedIndex = 0;
			}

			this._addActiveClientButton.Enabled = this._activeClientCombo.Items.Count > 0;
		}

		private void Grid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
		{
			if (e.Control is not ComboBox combo)
			{
				return;
			}

			combo.AutoCompleteMode = AutoCompleteMode.None;
			combo.AutoCompleteSource = AutoCompleteSource.None;
		}

		private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
		{
			if (this._grid.IsCurrentCellDirty)
			{
				this._grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
		}

		private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			if (this._suppressPersist || this._isAdjustingCell || e.RowIndex < 0)
			{
				return;
			}

			DataGridViewRow row = this._grid.Rows[e.RowIndex];
			if (row.IsNewRow)
			{
				return;
			}

			string columnName = this._grid.Columns[e.ColumnIndex].Name;

			this._isAdjustingCell = true;
			try
			{
				if (columnName == "OrderColumn")
				{
					if (!int.TryParse(Convert.ToString(row.Cells["OrderColumn"].Value), out _))
					{
						row.Cells["OrderColumn"].Value = 0;
					}
				}
				else if (columnName == "GroupColumn")
				{
					int groupIndex = Array.IndexOf(CycleGroupsSettingsControl.GroupChoices, Convert.ToString(row.Cells["GroupColumn"].Value));
					if (groupIndex > 0)
					{
						bool hasOrder = int.TryParse(Convert.ToString(row.Cells["OrderColumn"].Value), out int existingOrder) && existingOrder > 0;
						if (!hasOrder)
						{
							row.Cells["OrderColumn"].Value = this.GetNextOrderForGroup(groupIndex, e.RowIndex);
						}
					}
				}
				else if (columnName == "ClientColumn")
				{
					row.Cells["ClientColumn"].Value = Convert.ToString(row.Cells["ClientColumn"].Value)?.Trim();
				}
			}
			finally
			{
				this._isAdjustingCell = false;
			}

			this.PersistGridToConfiguration();
		}

		private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex < 0 || this._grid.Columns[e.ColumnIndex].Name != "DeleteColumn")
			{
				return;
			}

			DataGridViewRow row = this._grid.Rows[e.RowIndex];
			if (row.IsNewRow)
			{
				return;
			}

			this._grid.Rows.Remove(row);
			this.RefreshActiveClientChoices();
			this.PersistGridToConfiguration();
		}

		private int GetNextOrderForGroup(int groupIndex, int excludeRowIndex)
		{
			int max = 0;

			for (int i = 0; i < this._grid.Rows.Count; i++)
			{
				if (i == excludeRowIndex)
				{
					continue;
				}

				DataGridViewRow row = this._grid.Rows[i];
				if (row.IsNewRow)
				{
					continue;
				}

				if (Array.IndexOf(CycleGroupsSettingsControl.GroupChoices, Convert.ToString(row.Cells["GroupColumn"].Value)) != groupIndex)
				{
					continue;
				}

				if (int.TryParse(Convert.ToString(row.Cells["OrderColumn"].Value), out int order) && order > max)
				{
					max = order;
				}
			}

			return max + 1;
		}

		private void AddActiveClientButton_Click(object sender, EventArgs e)
		{
			if (this._activeClientCombo.SelectedItem is string title)
			{
				this.AddClientRow(title);
			}
		}

		private void AddManualClientButton_Click(object sender, EventArgs e)
		{
			string title = this._manualClientTextBox.Text?.Trim();
			if (string.IsNullOrEmpty(title))
			{
				return;
			}

			this._manualClientTextBox.Clear();
			this.AddClientRow(title);
		}

		private void AddClientRow(string title)
		{
			bool alreadyPresent = this._grid.Rows.Cast<DataGridViewRow>()
				.Where(row => !row.IsNewRow)
				.Any(row => string.Equals(Convert.ToString(row.Cells["ClientColumn"].Value), title, StringComparison.OrdinalIgnoreCase));

			if (alreadyPresent)
			{
				MessageBox.Show(this, title + " is already in the list.", "Cycle groups", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			this._grid.Rows.Add(title, CycleGroupsSettingsControl.GroupChoices[0], 0);
			this.RefreshActiveClientChoices();
			this.PersistGridToConfiguration();
		}

		private void RemoveSelectedButton_Click(object sender, EventArgs e)
		{
			List<DataGridViewRow> rowsToRemove = this._grid.SelectedRows.Cast<DataGridViewRow>()
				.Where(row => !row.IsNewRow)
				.ToList();

			if (rowsToRemove.Count == 0)
			{
				return;
			}

			foreach (DataGridViewRow row in rowsToRemove)
			{
				this._grid.Rows.Remove(row);
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

			foreach (DataGridViewRow row in this._grid.Rows)
			{
				if (row.IsNewRow)
				{
					continue;
				}

				string client = Convert.ToString(row.Cells["ClientColumn"].Value)?.Trim();
				if (string.IsNullOrEmpty(client))
				{
					continue;
				}

				int groupIndex = Array.IndexOf(CycleGroupsSettingsControl.GroupChoices, Convert.ToString(row.Cells["GroupColumn"].Value));
				if (groupIndex <= 0)
				{
					continue;
				}

				int.TryParse(Convert.ToString(row.Cells["OrderColumn"].Value), out int order);
				groups[groupIndex][client] = order;
			}

			this._configuration.CycleGroup1ClientsOrder = groups[1];
			this._configuration.CycleGroup2ClientsOrder = groups[2];
			this._configuration.CycleGroup3ClientsOrder = groups[3];
			this._configuration.CycleGroup4ClientsOrder = groups[4];
			this._configuration.CycleGroup5ClientsOrder = groups[5];

			this.PersistConfiguration?.Invoke();
		}
	}
}
