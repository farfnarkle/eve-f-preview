namespace EveFPreview.Services
{
	/// <summary>
	/// Resolves a character's current solar system by tailing the Local chat logs
	/// under Documents\EVE\logs\Chatlogs.
	/// </summary>
	public interface IEveLocationService
	{
		/// <summary>Re-scan / tail the Local chat logs. Cheap when nothing changed.</summary>
		void Refresh();

		/// <summary>
		/// Looks up the last known system for a client window title (e.g. "EVE - Farfnarkle")
		/// and optional character id from portrait cache.
		/// </summary>
		bool TryGetSystem(string windowTitle, int characterId, out string systemName);
	}
}
