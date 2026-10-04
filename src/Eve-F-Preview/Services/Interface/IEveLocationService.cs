using System;
using System.Collections.Generic;

namespace EveFPreview.Services
{
	/// <summary>
	/// Resolves a character's current solar system by tailing the Local chat logs
	/// under Documents\EVE\logs\Chatlogs.
	/// </summary>
	public interface IEveLocationService
	{
		/// <summary>
		/// Starts a background pass over the Local chat logs of the given running clients and returns
		/// at once (a pass that's still running isn't doubled up). <see cref="SystemsChanged"/> fires,
		/// on a worker thread, when it finds a client in a new system.
		/// </summary>
		void RequestRefresh(IEnumerable<(string WindowTitle, int CharacterId)> clients);

		/// <summary>Raised on a worker thread when a refresh pass found a changed system.</summary>
		event Action SystemsChanged;

		/// <summary>
		/// Looks up the last known system for a client window title (e.g. "EVE - Farfnarkle")
		/// and optional character id from portrait cache.
		/// </summary>
		bool TryGetSystem(string windowTitle, int characterId, out string systemName);
	}
}
