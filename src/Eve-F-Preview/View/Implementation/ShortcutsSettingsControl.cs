using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EveFPreview.Configuration;
using EveFPreview.UI.Hotkeys;

namespace EveFPreview.View
{
	sealed partial class ShortcutsSettingsControl : UserControl
	{
		private const int WM_KEYDOWN = 0x100;
		private const int WM_SYSKEYDOWN = 0x104;
		private const int WM_MBUTTONDOWN = 0x0207;
		private const int WM_XBUTTONDOWN = 0x020B;
		private const int WM_XBUTTONDBLCLK = 0x020D;
		private const int WM_NCXBUTTONDOWN = 0x00AB;
		private const int WM_NCMBUTTONDOWN = 0x00A7;
		private const int XBUTTON1 = 0x0001;
		private const int XBUTTON2 = 0x0002;
		private const string NoModifierChoice = "(none)";

		// Click-through is a held-modifier action, not a hotkey, so it is picked from a list
		// instead of recorded (the recorder intentionally ignores modifier-only presses).
		private static readonly string[] ModifierChoices =
		{
			NoModifierChoice, "Ctrl", "Alt", "Shift", "Win", "Ctrl+Shift", "Ctrl+Alt", "Alt+Shift", "Ctrl+Win", "Ctrl+Alt+Shift"
		};

		private readonly List<HotkeyRow> _rows = new List<HotkeyRow>();
		private readonly List<Button> _actionButtons = new List<Button>();
		private readonly List<Panel> _fieldPanels = new List<Panel>();
		private readonly List<Control> _cycleGroupModeControls = new List<Control>();
		private readonly List<Control> _dynamicCycleModeControls = new List<Control>();
		private List<Control> _currentSectionControls;
		private readonly ToolTip _toolTip = new ToolTip
		{
			AutoPopDelay = 15000,
			InitialDelay = 400,
			ReshowDelay = 200,
			ShowAlways = true
		};
		private ComboBox _clickThroughModifierCombo;
		private TableLayoutPanel _layout;
		private HotkeyRow _recordingRow;
		private HotkeyCaptureFilter _captureFilter;
		private bool _suppressChangeNotification;

		public Action SettingsChanged { get; set; }
		public Action SuspendGlobalHotkeys { get; set; }
		public Action ResumeGlobalHotkeys { get; set; }

		public ShortcutsSettingsControl()
		{
			this.InitializeComponent();
			this.BuildLayoutTable();
			this.BuildRows();
			this.ApplyScaledSizes();
			this.UpdateLayoutSize();
			this.ScrollPanel.Resize += this.ScrollPanel_Resize;
			this.Resize += this.ShortcutsSettingsControl_Resize;
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			this.ApplyScaledSizes();
			this.UpdateLayoutSize();
		}

		protected override void OnDpiChangedAfterParent(EventArgs e)
		{
			base.OnDpiChangedAfterParent(e);
			this.ApplyScaledSizes();
			this._layout?.PerformLayout();
			this.UpdateLayoutSize();
		}

		public void SetDynamicCycleEnabled(bool enabled)
		{
			if (this._recordingRow != null)
			{
				this.StopRecording();
			}

			this.SetSectionVisible(this._cycleGroupModeControls, !enabled);
			this.SetSectionVisible(this._dynamicCycleModeControls, enabled);
			this._layout?.PerformLayout();
			this.UpdateLayoutSize();
		}

		public GlobalShortcutSettings GetSettings()
		{
			var settings = new GlobalShortcutSettings();
			foreach (HotkeyRow row in this._rows)
			{
				row.ApplyTo(settings);
			}

			settings.ClickThroughModifier = this.GetClickThroughModifier();

			return settings;
		}

		public void SetSettings(GlobalShortcutSettings settings)
		{
			if (settings == null)
			{
				return;
			}

			this._suppressChangeNotification = true;
			try
			{
				foreach (HotkeyRow row in this._rows)
				{
					row.LoadFrom(settings);
				}

				this.SetClickThroughModifier(settings.ClickThroughModifier);
				this._layout?.PerformLayout();
				this.UpdateLayoutSize();
			}
			finally
			{
				this._suppressChangeNotification = false;
			}
		}

		protected override void OnHandleDestroyed(EventArgs e)
		{
			this.StopRecording();
			base.OnHandleDestroyed(e);
		}

		private void BuildLayoutTable()
		{
			this._layout = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 3,
				Dock = DockStyle.Top,
				GrowStyle = TableLayoutPanelGrowStyle.AddRows,
				Location = new Point(0, 0),
				Margin = new Padding(0),
				Padding = new Padding(12, 12, 12, 8)
			};
			this._layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			this._layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			this._layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingsHelp.IconColumnWidth));

			this.ScrollPanel.Controls.Add(this._layout);
			this.UpdateLayoutSize();
		}

		private void ScrollPanel_Resize(object sender, EventArgs e)
		{
			this.UpdateLayoutSize();
		}

		private void ShortcutsSettingsControl_Resize(object sender, EventArgs e)
		{
			this.UpdateLayoutSize();
		}

		private void UpdateLayoutSize()
		{
			if (this._layout == null || this.ScrollPanel == null)
			{
				return;
			}

			int width = Math.Max(200, this.ScrollPanel.ClientSize.Width);
			if (this.ScrollPanel.VerticalScroll.Visible)
			{
				width -= SystemInformation.VerticalScrollBarWidth;
			}

			this._layout.MinimumSize = new Size(width, 0);
			this._layout.Width = width;
			this._layout.PerformLayout();

			// PreferredSize on TableLayoutPanel can be wildly inflated; use actual height only.
			int contentHeight = this._layout.Height + this._layout.Padding.Vertical;
			if (contentHeight > this.ScrollPanel.ClientSize.Height)
			{
				this.ScrollPanel.AutoScrollMinSize = new Size(0, contentHeight);
			}
			else
			{
				this.ScrollPanel.AutoScrollMinSize = Size.Empty;
			}
		}

		private void BuildRows()
		{
			this._currentSectionControls = this._cycleGroupModeControls;
			this.AddSectionHeader("Cycle groups", SettingsHelp.Text.CycleGroups);
			this.AddCycleGroupRow("Group 1 forward", HotkeyRowKind.CycleGroup1Forward);
			this.AddCycleGroupRow("Group 1 backward", HotkeyRowKind.CycleGroup1Backward);
			this.AddCycleGroupRow("Group 2 forward", HotkeyRowKind.CycleGroup2Forward);
			this.AddCycleGroupRow("Group 2 backward", HotkeyRowKind.CycleGroup2Backward);
			this.AddCycleGroupRow("Group 3 forward", HotkeyRowKind.CycleGroup3Forward);
			this.AddCycleGroupRow("Group 3 backward", HotkeyRowKind.CycleGroup3Backward);
			this.AddCycleGroupRow("Group 4 forward", HotkeyRowKind.CycleGroup4Forward);
			this.AddCycleGroupRow("Group 4 backward", HotkeyRowKind.CycleGroup4Backward);
			this.AddCycleGroupRow("Group 5 forward", HotkeyRowKind.CycleGroup5Forward);
			this.AddCycleGroupRow("Group 5 backward", HotkeyRowKind.CycleGroup5Backward);

			this._currentSectionControls = this._dynamicCycleModeControls;
			this.AddSectionHeader("Dynamic cycle", SettingsHelp.Text.DynamicCycleHotkeys);
			this.AddCycleGroupRow("Dynamic forward", HotkeyRowKind.DynamicCycleForward);
			this.AddCycleGroupRow("Dynamic backward", HotkeyRowKind.DynamicCycleBackward);

			this._currentSectionControls = null;
			this.AddSectionHeader("Other");
			this.AddCycleGroupRow("Minimize all clients", HotkeyRowKind.MinimizeAllClients);
			this.AddCycleGroupRow("Toggle thumbnails visibility", HotkeyRowKind.ToggleThumbnails);
			this.AddModifierRow("Click-through while held", SettingsHelp.Text.ClickThrough);

			var note = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 8, 0, 0),
				Text = "Click Set to record a hotkey (keyboard, mouse 4/5 side buttons, or middle click). Click Clear to remove it. Per-client activation hotkeys are set on the Clients tab (Ctrl+click a character)."
			};
			this.AddFullWidthControl(note, SizeType.AutoSize);
			this.SetDynamicCycleEnabled(false);
		}

		private void AddSectionHeader(string text, string helpText = null)
		{
			var header = new Label
			{
				AutoSize = true,
				Font = new Font(this.Font, FontStyle.Bold),
				Margin = new Padding(0, 2, 6, 0),
				Text = text
			};

			if (string.IsNullOrEmpty(helpText))
			{
				header.Dock = DockStyle.Fill;
				header.Margin = new Padding(0, 10, 0, 4);
				this.AddFullWidthControl(header, SizeType.AutoSize);
				return;
			}

			var flow = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				Margin = new Padding(0, 10, 0, 4),
				WrapContents = false
			};
			flow.Controls.Add(header);
			flow.Controls.Add(SettingsHelp.CreateIcon(helpText));
			this.AddFullWidthControl(flow, SizeType.AutoSize);
		}

		private void AddFullWidthControl(Control control, SizeType rowSizeType)
		{
			int rowIndex = this._layout.RowCount;
			this._layout.RowStyles.Add(new RowStyle(rowSizeType));
			this._layout.RowCount++;
			this._layout.Controls.Add(control, 0, rowIndex);
			this._layout.SetColumnSpan(control, 3);
			this.TrackSectionControl(control);
		}

		private void TrackSectionControl(Control control)
		{
			this._currentSectionControls?.Add(control);
		}

		private void SetSectionVisible(List<Control> controls, bool visible)
		{
			foreach (Control control in controls)
			{
				control.Visible = visible;
				int row = this._layout.GetRow(control);
				if (row >= 0 && row < this._layout.RowStyles.Count)
				{
					this._layout.RowStyles[row] = visible
						? new RowStyle(SizeType.AutoSize)
						: new RowStyle(SizeType.Absolute, 0);
				}
			}
		}

		private void AddCycleGroupRow(string labelText, HotkeyRowKind kind)
		{
			int rowIndex = this._layout.RowCount;
			this._layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			this._layout.RowCount++;

			var label = new Label
			{
				Anchor = AnchorStyles.Left,
				AutoSize = true,
				Margin = new Padding(0, 8, 8, 8),
				Text = labelText,
				TextAlign = ContentAlignment.MiddleLeft
			};

			var textBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 0, 8, 0),
				ReadOnly = true
			};

			var setButton = this.CreateActionButton("Set");
			setButton.Dock = DockStyle.Right;
			setButton.Margin = new Padding(0);
			this._toolTip.SetToolTip(setButton, "Set records a hotkey, including mouse side buttons. Clear removes the current hotkey.");

			var fieldPanel = new Panel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 4, 0, 4)
			};
			fieldPanel.Controls.Add(textBox);
			fieldPanel.Controls.Add(setButton);
			this._fieldPanels.Add(fieldPanel);

			this._layout.Controls.Add(label, 0, rowIndex);
			this._layout.Controls.Add(fieldPanel, 1, rowIndex);
			this.TrackSectionControl(label);
			this.TrackSectionControl(fieldPanel);

			var row = new HotkeyRow(kind, textBox, setButton);
			this._rows.Add(row);
			setButton.Click += (_, _) => this.SetButton_Click(row);
		}

		private void AddModifierRow(string labelText, string helpText = null)
		{
			int rowIndex = this._layout.RowCount;
			this._layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			this._layout.RowCount++;

			var label = new Label
			{
				Anchor = AnchorStyles.Left,
				AutoSize = true,
				Margin = new Padding(0, 8, 8, 8),
				Text = labelText,
				TextAlign = ContentAlignment.MiddleLeft
			};

			this._clickThroughModifierCombo = new ComboBox
			{
				Dock = DockStyle.Fill,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Margin = new Padding(0, 4, 0, 4)
			};
			this._clickThroughModifierCombo.Items.AddRange(ModifierChoices);
			this._clickThroughModifierCombo.SelectedIndex = 0;
			this._clickThroughModifierCombo.SelectedIndexChanged += (_, _) => this.NotifySettingsChanged();

			this._layout.Controls.Add(label, 0, rowIndex);
			this._layout.Controls.Add(this._clickThroughModifierCombo, 1, rowIndex);
			if (!string.IsNullOrEmpty(helpText))
			{
				this._layout.Controls.Add(SettingsHelp.CreateIcon(helpText), 2, rowIndex);
			}
		}

		private string GetClickThroughModifier()
		{
			string value = this._clickThroughModifierCombo?.SelectedItem as string;

			return string.IsNullOrEmpty(value) || value == NoModifierChoice ? string.Empty : value;
		}

		private void SetClickThroughModifier(string value)
		{
			if (this._clickThroughModifierCombo == null)
			{
				return;
			}

			int index = Array.IndexOf(ModifierChoices, ShortcutsSettingsControl.NormalizeModifiers(value));
			this._clickThroughModifierCombo.SelectedIndex = index < 0 ? 0 : index;
		}

		// Keeps only the modifier part of a stored value so combinations saved by the old
		// full-hotkey editor (like "Ctrl+Shift+C") still map onto a choice in the list.
		private static string NormalizeModifiers(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return NoModifierChoice;
			}

			var parts = new List<string>();
			foreach (string token in value.Split('+'))
			{
				string modifier = token.Trim().ToUpperInvariant() switch
				{
					"CTRL" or "CONTROL" => "Ctrl",
					"ALT" or "MENU" => "Alt",
					"SHIFT" => "Shift",
					"WIN" or "WINDOWS" => "Win",
					_ => null
				};

				if (modifier != null && !parts.Contains(modifier))
				{
					parts.Add(modifier);
				}
			}

			if (parts.Count == 0)
			{
				return NoModifierChoice;
			}

			// Canonical order matches the choice list
			var ordered = new List<string>();
			foreach (string modifier in new[] { "Ctrl", "Alt", "Shift", "Win" })
			{
				if (parts.Contains(modifier))
				{
					ordered.Add(modifier);
				}
			}

			return string.Join("+", ordered);
		}

		private Button CreateActionButton(string text)
		{
			var button = new Button
			{
				Margin = new Padding(4, 4, 0, 4),
				Text = text
			};
			SettingsHelp.StyleActionButton(this, button);
			this._actionButtons.Add(button);
			return button;
		}

		private void ApplyScaledSizes()
		{
			SettingsHelp.ApplyScaledButtonSizes(this, this._actionButtons.ToArray());
			int rowHeight = this.LogicalToDeviceUnits(SettingsHelp.ActionButtonMinHeight);
			foreach (Panel panel in this._fieldPanels)
			{
				panel.MinimumSize = new Size(0, rowHeight);
				panel.Height = rowHeight;
			}
		}

		private void SetButton_Click(HotkeyRow row)
		{
			if (this._recordingRow == row)
			{
				this.StopRecording();
				return;
			}

			if (row.HasHotkey)
			{
				if (this._recordingRow != null)
				{
					this.StopRecording();
				}

				row.TextBox.Text = string.Empty;
				row.SyncActionButton();
				this._layout.PerformLayout();
				this.NotifySettingsChanged();
				return;
			}

			this.StartRecording(row);
		}

		private void StartRecording(HotkeyRow row)
		{
			this.StopRecording();
			this._recordingRow = row;
			row.SetButton.Text = "Cancel";
			row.SetButton.Parent?.PerformLayout();
			this._layout.PerformLayout();
			row.TextBox.BackColor = SystemColors.Info;
			this.SuspendGlobalHotkeys?.Invoke();
			this._captureFilter = new HotkeyCaptureFilter(this.OnHotkeyCaptured, this.OnHotkeyCaptureCancelled);
			Application.AddMessageFilter(this._captureFilter);
		}

		private void StopRecording()
		{
			if (this._captureFilter != null)
			{
				this._captureFilter.Stop();
				Application.RemoveMessageFilter(this._captureFilter);
				this._captureFilter = null;
			}

			if (this._recordingRow != null)
			{
				this._recordingRow.SyncActionButton();
				this._recordingRow.SetButton.Parent?.PerformLayout();
				this._layout.PerformLayout();
				this._recordingRow.RestoreIdleFieldAppearance();
				this._recordingRow = null;
			}

			this.ResumeGlobalHotkeys?.Invoke();
		}

		private void OnHotkeyCaptured(Keys keys)
		{
			HotkeyRow row = this._recordingRow;
			this.StopRecording();
			if (row == null)
			{
				return;
			}

			row.TextBox.Text = keys == Keys.None ? string.Empty : HotkeyFormatting.ToDisplayString(keys);
			row.SyncActionButton();
			this._layout.PerformLayout();
			this.NotifySettingsChanged();
		}

		private void OnHotkeyCaptureCancelled()
		{
			this.StopRecording();
		}

		private void NotifySettingsChanged()
		{
			if (this._suppressChangeNotification)
			{
				return;
			}

			this.SettingsChanged?.Invoke();
		}

		private sealed class HotkeyCaptureFilter : IMessageFilter
		{
			private readonly Action<Keys> _onCaptured;
			private readonly Action _onCancelled;
			private readonly bool _mouseHookActive;

			public HotkeyCaptureFilter(Action<Keys> onCaptured, Action onCancelled)
			{
				this._onCaptured = onCaptured;
				this._onCancelled = onCancelled;
				this._mouseHookActive = MouseButtonHotkeyMonitor.BeginCapture(onCaptured);
			}

			public void Stop()
			{
				MouseButtonHotkeyMonitor.EndCapture();
			}

			public bool PreFilterMessage(ref Message m)
			{
				if (!this._mouseHookActive && TryCaptureMouseButton(m, out Keys mouseKeys))
				{
					this._onCaptured(mouseKeys);
					return true;
				}

				if (m.Msg != WM_KEYDOWN && m.Msg != WM_SYSKEYDOWN)
				{
					return false;
				}

				Keys keyCode = (Keys)(m.WParam.ToInt64() & 0xFFFF);
				if (keyCode == Keys.Escape)
				{
					this._onCancelled();
					return true;
				}

				if (keyCode == Keys.Delete || keyCode == Keys.Back)
				{
					this._onCaptured(Keys.None);
					return true;
				}

				if (IsModifierKey(keyCode))
				{
					return true;
				}

				Keys keys = keyCode | Control.ModifierKeys;
				this._onCaptured(keys);
				return true;
			}

			private static bool TryCaptureMouseButton(Message m, out Keys keys)
			{
				keys = Keys.None;
				if (m.Msg == WM_MBUTTONDOWN || m.Msg == WM_NCMBUTTONDOWN)
				{
					keys = Keys.MButton | Control.ModifierKeys;
					return true;
				}

				if (m.Msg != WM_XBUTTONDOWN && m.Msg != WM_NCXBUTTONDOWN && m.Msg != WM_XBUTTONDBLCLK)
				{
					return false;
				}

				int xButton = (int)((m.WParam.ToInt64() >> 16) & 0xFFFF);
				if (xButton == XBUTTON1)
				{
					keys = Keys.XButton1 | Control.ModifierKeys;
					return true;
				}

				if (xButton == XBUTTON2)
				{
					keys = Keys.XButton2 | Control.ModifierKeys;
					return true;
				}

				return false;
			}

			private static bool IsModifierKey(Keys keyCode)
			{
				return keyCode == Keys.ShiftKey
					|| keyCode == Keys.ControlKey
					|| keyCode == Keys.Menu
					|| keyCode == Keys.LShiftKey
					|| keyCode == Keys.RShiftKey
					|| keyCode == Keys.LControlKey
					|| keyCode == Keys.RControlKey
					|| keyCode == Keys.LMenu
					|| keyCode == Keys.RMenu;
			}
		}

		private enum HotkeyRowKind
		{
			CycleGroup1Forward,
			CycleGroup1Backward,
			CycleGroup2Forward,
			CycleGroup2Backward,
			CycleGroup3Forward,
			CycleGroup3Backward,
			CycleGroup4Forward,
			CycleGroup4Backward,
			CycleGroup5Forward,
			CycleGroup5Backward,
			DynamicCycleForward,
			DynamicCycleBackward,
			MinimizeAllClients,
			ToggleThumbnails
		}

		private sealed class HotkeyRow
		{
			public HotkeyRow(HotkeyRowKind kind, TextBox textBox, Button setButton)
			{
				this.Kind = kind;
				this.TextBox = textBox;
				this.SetButton = setButton;
			}

			public HotkeyRowKind Kind { get; }
			public TextBox TextBox { get; }
			public Button SetButton { get; }

			public bool HasHotkey => !string.IsNullOrWhiteSpace(this.TextBox.Text);

			public void SyncActionButton()
			{
				this.SetButton.Text = this.HasHotkey ? "Clear" : "Set";
			}

			public void RestoreIdleFieldAppearance()
			{
				this.TextBox.ResetBackColor();
			}

			public void LoadFrom(GlobalShortcutSettings settings)
			{
				this.TextBox.Text = this.Kind switch
				{
					HotkeyRowKind.CycleGroup1Forward => settings.CycleGroup1Forward,
					HotkeyRowKind.CycleGroup1Backward => settings.CycleGroup1Backward,
					HotkeyRowKind.CycleGroup2Forward => settings.CycleGroup2Forward,
					HotkeyRowKind.CycleGroup2Backward => settings.CycleGroup2Backward,
					HotkeyRowKind.CycleGroup3Forward => settings.CycleGroup3Forward,
					HotkeyRowKind.CycleGroup3Backward => settings.CycleGroup3Backward,
					HotkeyRowKind.CycleGroup4Forward => settings.CycleGroup4Forward,
					HotkeyRowKind.CycleGroup4Backward => settings.CycleGroup4Backward,
					HotkeyRowKind.CycleGroup5Forward => settings.CycleGroup5Forward,
					HotkeyRowKind.CycleGroup5Backward => settings.CycleGroup5Backward,
					HotkeyRowKind.DynamicCycleForward => settings.DynamicCycleForward,
					HotkeyRowKind.DynamicCycleBackward => settings.DynamicCycleBackward,
					HotkeyRowKind.MinimizeAllClients => settings.MinimizeAllClients,
					HotkeyRowKind.ToggleThumbnails => settings.ToggleThumbnails,
					_ => string.Empty
				};
				this.SyncActionButton();
			}

			public void ApplyTo(GlobalShortcutSettings settings)
			{
				switch (this.Kind)
				{
					case HotkeyRowKind.CycleGroup1Forward:
						settings.CycleGroup1Forward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup1Backward:
						settings.CycleGroup1Backward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup2Forward:
						settings.CycleGroup2Forward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup2Backward:
						settings.CycleGroup2Backward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup3Forward:
						settings.CycleGroup3Forward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup3Backward:
						settings.CycleGroup3Backward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup4Forward:
						settings.CycleGroup4Forward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup4Backward:
						settings.CycleGroup4Backward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup5Forward:
						settings.CycleGroup5Forward = this.TextBox.Text;
						break;
					case HotkeyRowKind.CycleGroup5Backward:
						settings.CycleGroup5Backward = this.TextBox.Text;
						break;
					case HotkeyRowKind.DynamicCycleForward:
						settings.DynamicCycleForward = this.TextBox.Text;
						break;
					case HotkeyRowKind.DynamicCycleBackward:
						settings.DynamicCycleBackward = this.TextBox.Text;
						break;
					case HotkeyRowKind.MinimizeAllClients:
						settings.MinimizeAllClients = this.TextBox.Text;
						break;
					case HotkeyRowKind.ToggleThumbnails:
						settings.ToggleThumbnails = this.TextBox.Text;
						break;
				}
			}
		}
	}
}
