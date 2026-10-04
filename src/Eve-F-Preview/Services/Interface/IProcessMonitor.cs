using System.Collections.Generic;
using System.Threading.Tasks;

namespace EveFPreview.Services
{
	public interface IProcessMonitor
	{
		IProcessInfo GetMainProcess();
		void GetUpdatedProcesses(out ICollection<IProcessInfo> addedProcesses, out ICollection<IProcessInfo> updatedProcesses, out ICollection<IProcessInfo> removedProcesses);

		/// <summary>
		/// Force-terminates every running EVE Online game client (exefile) at once. Only EVE clients -
		/// never other tracked programs.
		/// </summary>
		Task CloseAllMonitoredClientsAsync();
	}
}