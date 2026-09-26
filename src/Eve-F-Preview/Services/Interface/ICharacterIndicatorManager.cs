using System;
using System.Collections.Generic;

namespace EveFPreview.Services
{
	/// <summary>
	/// Owns the small always-on-top "current character" indicator window: a compact grid of
	/// squares mirroring the on-screen thumbnail layout, with the active client's square
	/// highlighted. Purely a readout by default - it never changes which client is active,
	/// unless the "clickable" setting is on, in which case clicking a square invokes
	/// <see cref="CellClicked"/> the same way clicking the real thumbnail would.
	/// </summary>
	public interface ICharacterIndicatorManager
	{
		void Start();
		void Stop();

		/// <summary>Invoked (with the client's window handle) when a square is clicked while the "clickable" setting is on.</summary>
		Action<IntPtr> CellClicked { get; set; }

		/// <summary>Invoked (with the client's window handle) when a square is shift-clicked - toggles that client's cycle-group exclusion, same as shift-clicking its thumbnail. Always active, independent of the "clickable" setting.</summary>
		Action<IntPtr> CellShiftClicked { get; set; }

		/// <summary>
		/// Pushes the current thumbnail layout, grouped into rows the same way the dynamic
		/// cycle order groups them (top-to-bottom, left-to-right within a row). No-ops (and
		/// hides the window) while the indicator is disabled in settings. <paramref name="eveHasFocus"/>
		/// hides the window whenever none of the tracked EVE clients (or their thumbnails) is focused,
		/// even though the feature is enabled.
		/// </summary>
		void UpdateLayout(IReadOnlyList<IReadOnlyList<CharacterIndicatorCell>> rows, bool eveHasFocus);
	}

	public readonly struct CharacterIndicatorCell
	{
		public readonly IntPtr Handle;
		public readonly string Title;
		public readonly bool IsActive;
		public readonly bool IsExcludedFromCycleGroup;

		public CharacterIndicatorCell(IntPtr handle, string title, bool isActive, bool isExcludedFromCycleGroup)
		{
			this.Handle = handle;
			this.Title = title;
			this.IsActive = isActive;
			this.IsExcludedFromCycleGroup = isExcludedFromCycleGroup;
		}
	}
}
