using System;
using System.Drawing;
using System.Windows.Forms;

namespace EveFPreview.View
{
	internal static class SettingsHelp
	{
		public const int IconColumnWidth = 20;
		public const int ActionButtonMinWidth = 72;
		public const int ActionButtonMinHeight = 28;

		private static readonly ToolTip ToolTip = new ToolTip
		{
			AutoPopDelay = 20000,
			InitialDelay = 400,
			ReshowDelay = 200,
			ShowAlways = true
		};

		public static TableLayoutPanel CreateScrollTable()
		{
			var table = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 2,
				Dock = DockStyle.Top,
				GrowStyle = TableLayoutPanelGrowStyle.AddRows,
				Margin = new Padding(0),
				Padding = new Padding(12)
			};
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IconColumnWidth));
			return table;
		}

		public static void HostInScrollPanel(Panel panel, TableLayoutPanel table)
		{
			panel.AutoScroll = true;
			panel.Controls.Add(table);
			table.BringToFront();
			AttachHostResize(panel, table);
		}

		public static void AttachHostResize(ScrollableControl host, TableLayoutPanel table)
		{
			void SyncWidth(object sender, EventArgs e)
			{
				int width = Math.Max(200, host.ClientSize.Width);
				if (host.VerticalScroll.Visible)
				{
					width -= SystemInformation.VerticalScrollBarWidth;
				}

				table.Width = width;
			}

			host.Resize += SyncWidth;
			SyncWidth(host, EventArgs.Empty);
		}

		public static Size ScaledActionButtonMinimumSize(Control host)
		{
			return host.LogicalToDeviceUnits(new Size(ActionButtonMinWidth, ActionButtonMinHeight));
		}

		public static void StyleActionButton(Control host, Button button, bool fillWidth = false)
		{
			button.AutoSize = true;
			button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			button.Padding = new Padding(10, 4, 10, 4);
			button.MinimumSize = ScaledActionButtonMinimumSize(host);
			button.UseVisualStyleBackColor = true;
			if (fillWidth)
			{
				button.Dock = DockStyle.Fill;
				button.Margin = new Padding(0, 8, 0, 4);
			}
			else
			{
				button.Anchor = AnchorStyles.Left;
			}
		}

		public static void ApplyScaledButtonSizes(Control host, params Button[] buttons)
		{
			Size minimumSize = ScaledActionButtonMinimumSize(host);
			foreach (Button button in buttons)
			{
				if (button != null)
				{
					button.MinimumSize = minimumSize;
				}
			}
		}

		public static Label CreateIcon(string helpText)
		{
			var icon = new Label
			{
				Anchor = AnchorStyles.Top,
				AutoSize = false,
				Cursor = Cursors.Help,
				ForeColor = Color.FromArgb(0, 102, 180),
				Margin = new Padding(0, 6, 0, 0),
				Size = new Size(18, 18),
				Text = "ⓘ",
				TextAlign = ContentAlignment.MiddleCenter
			};
			ToolTip.SetToolTip(icon, helpText);
			return icon;
		}

		public static void AddRow(TableLayoutPanel table, Control control, string helpText = null)
		{
			int rowIndex = table.RowCount;
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowCount++;

			ResetForTable(control);
			control.Margin = new Padding(0, 6, 8, 6);
			if (control is ComboBox || control is TrackBar || control is TableLayoutPanel)
			{
				control.Dock = DockStyle.Fill;
			}

			table.Controls.Add(control, 0, rowIndex);

			if (string.IsNullOrEmpty(helpText))
			{
				table.SetColumnSpan(control, 2);
			}
			else
			{
				table.Controls.Add(CreateIcon(helpText), 1, rowIndex);
			}
		}

		public static void AddFixedHeight(TableLayoutPanel table, Control control, int height, string helpText = null)
		{
			int rowIndex = table.RowCount;
			table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
			table.RowCount++;

			ResetForTable(control);
			control.Dock = DockStyle.Fill;
			control.Margin = new Padding(0, 4, 8, 4);
			table.Controls.Add(control, 0, rowIndex);

			if (string.IsNullOrEmpty(helpText))
			{
				table.SetColumnSpan(control, 2);
			}
			else
			{
				table.Controls.Add(CreateIcon(helpText), 1, rowIndex);
			}
		}

		public static void AddFullWidthButton(TableLayoutPanel table, Button button)
		{
			AddRow(table, button);
			StyleActionButton(table, button, fillWidth: true);
		}

		public static FlowLayoutPanel CreateFlow(params Control[] controls)
		{
			var flow = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				FlowDirection = FlowDirection.LeftToRight,
				Margin = new Padding(0),
				Padding = Padding.Empty,
				WrapContents = false
			};

			foreach (Control control in controls)
			{
				ResetForTable(control);
				if (control is Button button)
				{
					StyleActionButton(flow, button);
					control.Margin = new Padding(0, 4, 8, 4);
				}
				else
				{
					control.Margin = new Padding(0, 2, 8, 2);
				}

				flow.Controls.Add(control);
			}

			return flow;
		}

		public static TableLayoutPanel CreateLabeledFill(Label label, Control fill)
		{
			var inner = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 2,
				Dock = DockStyle.Fill,
				Margin = new Padding(0)
			};
			inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

			ResetForTable(label);
			label.Anchor = AnchorStyles.Left;
			label.AutoSize = true;
			label.Margin = new Padding(0, 4, 8, 4);

			ResetForTable(fill);
			fill.Dock = DockStyle.Fill;
			fill.Margin = new Padding(0, 2, 0, 2);

			inner.Controls.Add(label, 0, 0);
			inner.Controls.Add(fill, 1, 0);
			return inner;
		}

		private static void ResetForTable(Control control)
		{
			control.Dock = DockStyle.None;
			control.Anchor = AnchorStyles.Left;
			control.Location = Point.Empty;
		}

		internal static class Text
		{
			public const string TrackClientLocations = "Restore EVE window positions when clients are detected.";
			public const string HideCaptionBar = "Hide the Windows title bar on EVE client windows.";
			public const string CycleHotkeysWhenEveActive = "Avoid stealing cycle hotkeys when you are not in EVE.";
			public const string DynamicCycleGroup = "When enabled, cycle in on-screen thumbnail order. Shortcuts then show Dynamic cycle hotkeys instead of numbered groups.";
			public const string AccountBasedPositioning = "Remember thumbnail positions per EVE account.";
			public const string UniqueLayout = "Store a separate thumbnail layout for each EVE client.";
			public const string AutoSettingsSync = "Optional settings sync on startup when EVE is closed.";
			public const string ConfigProfile = "Load, Save As, or Import (EVE-O / EVE-X) a preview configuration profile.";
			public const string AnimationStyle = "How thumbnail windows animate when they are shown or hidden.";
			public const string OverwatchMode = "Enlarged focused preview. Ctrl+click a thumbnail to pin it.";
			public const string LockThumbnailLocation = "Prevent dragging thumbnails to a new position.";
			public const string SnapToEdges = "Snap thumbnails to screen and other thumbnail edges while dragging.";
			public const string SnapToGrid = "Snap thumbnail positions to the grid size below.";
			public const string DoNotDisplayPreviews = "Show character portraits instead of live client capture.";
			public const string ShowOverlay = "Show character name labels on thumbnails.";
			public const string ShowSystemName = "Show [SYSTEM] from Local chat. Requires chat logging in EVE.";
			public const string ShowFrames = "Draw a border around each thumbnail.";
			public const string CycleGroupIndicator = "Where the cycle-group badge is drawn on each thumbnail.";
			public const string CycleGroups = "Forward and backward hotkeys for each numbered cycle group.";
			public const string DynamicCycleHotkeys = "Cycle all non-excluded clients in on-screen thumbnail order.";
			public const string ClickThrough = "Hold this modifier to click through thumbnails to windows behind them (for example Ctrl+Shift).";
			public const string SettingsProfile = "Which EVE settings profile folder to copy from and to.";
			public const string ChannelsToKeep = "Chat channels to keep on destination characters when copying settings.";
			public const string PreserveModuleLayout = "Do not overwrite each alt's fitted module layout from the source character.";
		}
	}
}
