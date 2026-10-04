using System.Collections.Generic;
using System.Threading.Tasks;

namespace EveFPreview.Services
{
	public interface IProcessMonitor
	{
		IProcessInfo GetMainProcess();
		void GetUpdatedProcesses(out ICollection<IProcessInfo> addedProcesses, out ICollection<IProcessInfo> updatedProcesses, out ICollection<IProcessInfo> removedProcesses);

		/// <summary>
		/// Asks every running EVE Online game client (exefile) to close, then force-terminates any still
		/// running after a few seconds. Only EVE clients - never other tracked programs.
		/// </summary>
		Task CloseAllMonitoredClientsAsync();
	}
}