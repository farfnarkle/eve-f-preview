using System.Runtime.InteropServices;

namespace EveFPreview.UI.Hotkeys
{
	static class WinMmNativeMethods
	{
		[DllImport("winmm.dll")]
		public static extern uint timeBeginPeriod(uint uPeriod);

		[DllImport("winmm.dll")]
		public static extern uint timeEndPeriod(uint uPeriod);
	}
}
