using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using EveFPreview.Configuration;
using EveFPreview.UI.Hotkeys;

namespace EveFPreview.View
{
	public sealed partial class ShortcutsSettingsControl : UserControl
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
		private HotkeyRow _recordingRow;
		private HotkeyCaptureFilter _captureFilter;
		private bool _suppressChangeNotification;

		public Action SettingsChanged { get; set; }
		public Action SuspendGlobalHotkeys { get; set; }
		public Action ResumeGlobalHotkeys { get; set; }

		public ShortcutsSettingsControl()
		{
			this.InitializeComponent();

			this.CycleGroupRows.ItemsSource = new List<HotkeyPair>
			{
				this.CreatePair("Group 1", HotkeyRowKind.CycleGroup1Forward, HotkeyRowKind.CycleGroup1Backward),
				this.CreatePair("Group 2", HotkeyRowKind.CycleGroup2Forward, HotkeyRowKind.CycleGroup2Backward),
				this.CreatePair("Group 3", HotkeyRowKind.CycleGroup3Forward, HotkeyRowKind.CycleGroup3Backward),
				this.CreatePair("Group 4", HotkeyRowKind.CycleGroup4Forward, HotkeyRowKind.CycleGroup4Backward),
				this.CreatePair("Group 5", HotkeyRowKind.CycleGroup5Forward, HotkeyRowKind.CycleGroup5Backward)
			};

			this.DynamicCycleRows.ItemsSource = new List<HotkeyPair>
			{
				this.CreatePair("Dynamic cycle", HotkeyRowKind.DynamicCycleForward, HotkeyRowKind.DynamicCycleBackward)
			};

			this.OtherRows.ItemsSource = this.CreateRows(
				("Minimize all clients", HotkeyRowKind.MinimizeAllClients),
				("Toggle thumbnails visibility", HotkeyRowKind.ToggleThumbnails));

			this.ClickThroughModifierCombo.ItemsSource = ModifierChoices;
			this.ClickThroughModifierCombo.SelectedIndex = 0;

			this.SetDynamicCycleEnabled(false);
		}

		public void SetDynamicCycleEnabled(bool enabled)
		{
			if (this._recordingRow != null)
			{
				this.StopRecording();
			}

			this.CycleGroupSection.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
			this.DynamicCycleSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
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
			}
			finally
			{
				this._suppressChangeNotification = false;
			}
		}

		private HotkeyPair CreatePair(string label, HotkeyRowKind forward, HotkeyRowKind backward)
		{
			List<HotkeyRow> rows = this.CreateRows((label + " forward", forward), (label + " backward", backward));
			return new HotkeyPair(label, rows[0], rows[1]);
		}

		private List<HotkeyRow> CreateRows(params (string Label, HotkeyRowKind Kind)[] definitions)
		{
			var rows = new List<HotkeyRow>();
			foreach ((string label, HotkeyRowKind kind) in definitions)
			{
				var row = new HotkeyRow(kind, label);
				rows.Add(row);
				this._rows.Add(row);
			}

			return rows;
		}

		private void UserControl_Unloaded(object sender, RoutedEventArgs e)
		{
			this.StopRecording();
		}

		private void ClickThroughModifierCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			this.NotifySettingsChanged();
		}

		private string GetClickThroughModifier()
		{
			string value = this.ClickThroughModifierCombo?.SelectedItem as string;

			return string.IsNullOrEmpty(value) || value == NoModifierChoice ? string.Empty : value;
		}

		private void SetClickThroughModifier(string value)
		{
			if (this.ClickThroughModifierCombo == null)
			{
				return;
			}

			int index = Array.IndexOf(ModifierChoices, ShortcutsSettingsControl.NormalizeModifiers(value));
			this.ClickThroughModifierCombo.SelectedIndex = index < 0 ? 0 : index;
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

		/// <summary>Clicking a hotkey box records a new combination (clicking it again cancels).</summary>
		private void HotkeyChip_Click(object sender, RoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.DataContext is not HotkeyRow row)
			{
				return;
			}

			if (this._recordingRow == row)
			{
				this.StopRecording();
				return;
			}

			this.StartRecording(row);
		}

		private void HotkeyClearButton_Click(object sender, RoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.DataContext is not HotkeyRow row)
			{
				return;
			}

			if (this._recordingRow != null)
			{
				this.StopRecording();
			}

			row.Text = string.Empty;
			this.NotifySettingsChanged();
		}

		private void StartRecording(HotkeyRow row)
		{
			this.StopRecording();
			this._recordingRow = row;
			row.IsRecording = true;
			this.SuspendGlobalHotkeys?.Invoke();
			this._captureFilter = new HotkeyCaptureFilter(this.OnHotkeyCaptured, this.OnHotkeyCaptureCancelled);
		}

		private void StopRecording()
		{
			if (this._captureFilter != null)
			{
				this._captureFilter.Stop();
				this._captureFilter = null;
			}

			if (this._recordingRow != null)
			{
				this._recordingRow.IsRecording = false;
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

			row.Text = keys == Keys.None ? string.Empty : HotkeyFormatting.ToDisplayString(keys);
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

		/// <summary>
		/// Sees keyboard (and, if the low-level hook isn't available, mouse button) messages before
		/// the rest of the app does while a row is recording, so the pressed combination is captured
		/// instead of typed or acted on.
		/// </summary>
		private sealed class HotkeyCaptureFilter
		{
			private readonly Action<Keys> _onCaptured;
			private readonly Action _onCancelled;
			private readonly bool _mouseHookActive;

			public HotkeyCaptureFilter(Action<Keys> onCaptured, Action onCancelled)
			{
				this._onCaptured = onCaptured;
				this._onCancelled = onCancelled;
				this._mouseHookActive = MouseButtonHotkeyMonitor.BeginCapture(onCaptured);
				ComponentDispatcher.ThreadFilterMessage += this.ThreadFilterMessage;
			}

			public void Stop()
			{
				ComponentDispatcher.ThreadFilterMessage -= this.ThreadFilterMessage;
				MouseButtonHotkeyMonitor.EndCapture();
			}

			private void ThreadFilterMessage(ref MSG m, ref bool handled)
			{
				if (handled)
				{
					return;
				}

				handled = this.PreFilterMessage(m);
			}

			private bool PreFilterMessage(MSG m)
			{
				if (!this._mouseHookActive && TryCaptureMouseButton(m, out Keys mouseKeys))
				{
					this._onCaptured(mouseKeys);
					return true;
				}

				if (m.message != WM_KEYDOWN && m.message != WM_SYSKEYDOWN)
				{
					return false;
				}

				Keys keyCode = (Keys)(m.wParam.ToInt64() & 0xFFFF);
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

				Keys keys = keyCode | KeyboardState.GetModifierKeys();
				this._onCaptured(keys);
				return true;
			}

			private static bool TryCaptureMouseButton(MSG m, out Keys keys)
			{
				keys = Keys.None;
				if (m.message == WM_MBUTTONDOWN || m.message == WM_NCMBUTTONDOWN)
				{
					keys = Keys.MButton | KeyboardState.GetModifierKeys();
					return true;
				}

				if (m.message != WM_XBUTTONDOWN && m.message != WM_NCXBUTTONDOWN && m.message != WM_XBUTTONDBLCLK)
				{
					return false;
				}

				int xButton = (int)((m.wParam.ToInt64() >> 16) & 0xFFFF);
				if (xButton == XBUTTON1)
				{
					keys = Keys.XButton1 | KeyboardState.GetModifierKeys();
					return true;
				}

				if (xButton == XBUTTON2)
				{
					keys = Keys.XButton2 | KeyboardState.GetModifierKeys();
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

		/// <summary>A cycle group's forward and backward hotkeys, shown side by side on one row.</summary>
		private sealed class HotkeyPair
		{
			public HotkeyPair(string label, HotkeyRow forward, HotkeyRow backward)
			{
				this.Label = label;
				this.Forward = forward;
				this.Backward = backward;
			}

			public string Label { get; }

			public HotkeyRow Forward { get; }

			public HotkeyRow Backward { get; }
		}

		/// <summary>One hotkey field: bound to a hotkey box in the XAML.</summary>
		private sealed class HotkeyRow : INotifyPropertyChanged
		{
			private string _text = string.Empty;
			private bool _isRecording;

			public HotkeyRow(HotkeyRowKind kind, string label)
			{
				this.Kind = kind;
				this.Label = label;
			}

			public event PropertyChangedEventHandler PropertyChanged;

			public HotkeyRowKind Kind { get; }

			public string Label { get; }

			public string Text
			{
				get => this._text;
				set
				{
					this._text = value ?? string.Empty;
					this.OnPropertyChanged(nameof(this.Text));
					this.OnPropertyChanged(nameof(this.HasHotkey));
					this.OnPropertyChanged(nameof(this.DisplayText));
				}
			}

			public bool IsRecording
			{
				get => this._isRecording;
				set
				{
					this._isRecording = value;
					this.OnPropertyChanged(nameof(this.IsRecording));
					this.OnPropertyChanged(nameof(this.DisplayText));
				}
			}

			public bool HasHotkey => !string.IsNullOrWhiteSpace(this.Text);

			public string DisplayText => this.IsRecording ? "Press keys…" : this.HasHotkey ? this.Text : "Not set";

			public void LoadFrom(GlobalShortcutSettings settings)
			{
				this.Text = this.Kind switch
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
			}

			public void ApplyTo(GlobalShortcutSettings settings)
			{
				switch (this.Kind)
				{
					case HotkeyRowKind.CycleGroup1Forward:
						settings.CycleGroup1Forward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup1Backward:
						settings.CycleGroup1Backward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup2Forward:
						settings.CycleGroup2Forward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup2Backward:
						settings.CycleGroup2Backward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup3Forward:
						settings.CycleGroup3Forward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup3Backward:
						settings.CycleGroup3Backward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup4Forward:
						settings.CycleGroup4Forward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup4Backward:
						settings.CycleGroup4Backward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup5Forward:
						settings.CycleGroup5Forward = this.Text;
						break;
					case HotkeyRowKind.CycleGroup5Backward:
						settings.CycleGroup5Backward = this.Text;
						break;
					case HotkeyRowKind.DynamicCycleForward:
						settings.DynamicCycleForward = this.Text;
						break;
					case HotkeyRowKind.DynamicCycleBackward:
						settings.DynamicCycleBackward = this.Text;
						break;
					case HotkeyRowKind.MinimizeAllClients:
						settings.MinimizeAllClients = this.Text;
						break;
					case HotkeyRowKind.ToggleThumbnails:
						settings.ToggleThumbnails = this.Text;
						break;
				}
			}

			private void OnPropertyChanged(string propertyName)
			{
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			}
		}
	}
}
