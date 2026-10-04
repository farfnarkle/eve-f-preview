namespace EveFPreview.Configuration
{
	/// <summary>
	/// A non-EVE program added to cycling. Its window is tracked like an EVE client, under the
	/// executable name rather than the window title (which changes with whatever the app shows),
	/// so its thumbnail position, cycle group and per-client settings stay attached to it.
	/// </summary>
	public class CycleApp
	{
		public CycleApp()
		{
		}

		public CycleApp(string executable, bool includeInDynamicCycle)
		{
			this.Executable = executable;
			this.IncludeInDynamicCycle = includeInDynamicCycle;
		}

		/// <summary>Process name as Windows reports it, without ".exe" (e.g. "Discord").</summary>
		public string Executable { get; set; }

		/// <summary>Whether dynamic cycling (and a group hotkey with no clients assigned) steps through this app.</summary>
		public bool IncludeInDynamicCycle { get; set; }
	}
}
