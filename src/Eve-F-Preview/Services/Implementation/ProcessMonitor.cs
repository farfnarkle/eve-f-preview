using EveFPreview.Configuration;
using EveFPreview.Services.Implementation;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EveFPreview.Services
{
	sealed class ProcessMonitor : IProcessMonitor
	{
		#region Private constants
		/// <summary>Windows / Wine EVE Online client process name (no .exe).</summary>
		private const string EveOnlineClientProcessName = "exefile";
		#endregion

		#region Private fields
		private readonly IDictionary<IntPtr, IProcessInfo> _processCache;
		private IProcessInfo _currentProcessInfo;
		private readonly IThumbnailConfiguration _configuration;
		#endregion

		public ProcessMonitor(IThumbnailConfiguration configuration)
		{
			this._processCache = new Dictionary<IntPtr, IProcessInfo>(512);
			this._configuration = configuration;

			// This field cannot be initialized properly in constructor
			// At the moment this code is executed the main application window is not yet initialized
			this._currentProcessInfo = new ProcessInfo(IntPtr.Zero, "");
		}

		private bool IsMonitoredProcess(string processName, out CycleApp app)
		{
			app = null;
			return this._configuration.IsExecutableToPreview(processName)
				|| this._configuration.TryGetCycleApp(processName, out app);
		}

		private IProcessInfo GetCurrentProcessInfo()
		{
			var currentProcess = Process.GetCurrentProcess();
			return new ProcessInfo(currentProcess.MainWindowHandle, currentProcess.MainWindowTitle);
		}

		public IProcessInfo GetMainProcess()
		{
			if (this._currentProcessInfo.Handle == IntPtr.Zero)
			{
				var processInfo = this.GetCurrentProcessInfo();

				// Are we initialized yet?
				if (processInfo.Title != "")
				{
					this._currentProcessInfo = processInfo;
				}
			}

			return this._currentProcessInfo;
		}

		public ICollection<IProcessInfo> GetAllProcesses()
		{
			ICollection<IProcessInfo> result = new List<IProcessInfo>(this._processCache.Count);

			// TODO Lock list here just in case
			foreach (IProcessInfo entry in this._processCache.Values)
			{
				result.Add(entry);
			}

			return result;
		}

		public void GetUpdatedProcesses(out ICollection<IProcessInfo> addedProcesses, out ICollection<IProcessInfo> updatedProcesses, out ICollection<IProcessInfo> removedProcesses)
		{
			addedProcesses = new List<IProcessInfo>(16);
			updatedProcesses = new List<IProcessInfo>(16);
			removedProcesses = new List<IProcessInfo>(16);

			IList<IntPtr> knownProcesses = new List<IntPtr>(this._processCache.Keys);
			foreach (Process process in Process.GetProcesses())
			{
				using (process)
				{
					if (!this.IsMonitoredProcess(process.ProcessName, out CycleApp app))
					{
						continue;
					}

					IntPtr mainWindowHandle = process.MainWindowHandle;
					if (mainWindowHandle == IntPtr.Zero)
					{
						continue; // No need to monitor non-visual processes
					}

					// An added app goes by its executable name: its window title changes with whatever it
					// shows, and every per-client setting (position, cycle group, ...) is keyed by title.
					string mainWindowTitle = app != null ? app.Executable : process.MainWindowTitle.Replace("—", "-");
					this._processCache.TryGetValue(mainWindowHandle, out IProcessInfo cached);

					if (cached == null)
					{
						// This is a new process in the list
						var info = new ProcessInfo(mainWindowHandle, mainWindowTitle, app != null);
						this._processCache.Add(mainWindowHandle, info);
						addedProcesses.Add(info);
					}
					else
					{
						// This is an already known process
						if (cached.Title != mainWindowTitle)
						{
							var info = new ProcessInfo(mainWindowHandle, mainWindowTitle, app != null);
							this._processCache[mainWindowHandle] = info;
							updatedProcesses.Add(info);
						}

						knownProcesses.Remove(mainWindowHandle);
					}
				}
			}

			foreach (IntPtr index in knownProcesses)
			{
				removedProcesses.Add(this._processCache[index]);
				this._processCache.Remove(index);
			}
		}

		public void CloseAllMonitoredClients()
		{
			foreach (Process process in Process.GetProcesses())
			{
				using (process)
				{
					if (!string.Equals(process.ProcessName, EveOnlineClientProcessName, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					try
					{
						process.Kill(entireProcessTree: true);
					}
					catch (InvalidOperationException)
					{
					}
					catch (System.ComponentModel.Win32Exception)
					{
					}
				}
			}
		}
	}
}
