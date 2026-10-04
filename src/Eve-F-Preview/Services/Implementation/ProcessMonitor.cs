using EveFPreview.Configuration;
using EveFPreview.Services.Implementation;
using EveFPreview.Services.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace EveFPreview.Services
{
	sealed class ProcessMonitor : IProcessMonitor
	{
		#region Private constants
		/// <summary>Windows / Wine EVE Online client process name (no .exe).</summary>
		private const string EveOnlineClientProcessName = "exefile";

		/// <summary>How long Close all EVE clients waits for clients to close themselves before force-closing them.</summary>
		private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(5);
		#endregion

		#region Private fields
		private readonly IDictionary<IntPtr, IProcessInfo> _processCache;
		// Process id -> process name, for every process that currently has a main window.
		private readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();
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
			foreach (KeyValuePair<uint, IntPtr> entry in this.FindMainWindows())
			{
				if (!this._processNames.TryGetValue(entry.Key, out string processName)
					|| !this.IsMonitoredProcess(processName, out CycleApp app))
				{
					continue;
				}

				IntPtr mainWindowHandle = entry.Value;
				// An added app goes by its executable name: its window title changes with whatever it
				// shows, and every per-client setting (position, cycle group, ...) is keyed by title.
				string mainWindowTitle = app != null ? app.Executable : ProcessMonitor.GetWindowTitle(mainWindowHandle).Replace("—", "-");
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

			foreach (IntPtr index in knownProcesses)
			{
				removedProcesses.Add(this._processCache[index]);
				this._processCache.Remove(index);
			}
		}

		/// <summary>
		/// Each process's main window - what Process.MainWindowHandle returns: the first window in
		/// z-order that is visible and has no owner - found in a single pass over the top-level
		/// windows. Process.GetProcesses() plus MainWindowHandle (a separate full window walk for
		/// every process asked) cost ~4.5 ms per refresh tick; this is ~0.1 ms.
		/// Also keeps _processNames current for every process found.
		/// </summary>
		private Dictionary<uint, IntPtr> FindMainWindows()
		{
			var mainWindows = new Dictionary<uint, IntPtr>(128);
			User32NativeMethods.EnumWindows((handle, _) =>
			{
				if (User32NativeMethods.IsWindowVisible(handle)
					&& User32NativeMethods.GetWindow(handle, User32NativeMethods.GW_OWNER) == IntPtr.Zero)
				{
					User32NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
					mainWindows.TryAdd(processId, handle);
				}

				return true;
			}, IntPtr.Zero);

			// A process's name never changes, so it's looked up once - in one process snapshot for
			// all newcomers - and forgotten as soon as the process has no window left. Process ids
			// are only reused after a process exits, and an exiting process loses its windows.
			bool hasUnknownProcess = false;
			foreach (uint processId in mainWindows.Keys)
			{
				if (!this._processNames.ContainsKey(processId))
				{
					hasUnknownProcess = true;
					break;
				}
			}

			if (hasUnknownProcess)
			{
				foreach (Process process in Process.GetProcesses())
				{
					using (process)
					{
						if (mainWindows.ContainsKey((uint)process.Id))
						{
							this._processNames[(uint)process.Id] = process.ProcessName;
						}
					}
				}
			}

			if (this._processNames.Count > mainWindows.Count)
			{
				var gone = new List<uint>();
				foreach (uint processId in this._processNames.Keys)
				{
					if (!mainWindows.ContainsKey(processId))
					{
						gone.Add(processId);
					}
				}

				foreach (uint processId in gone)
				{
					this._processNames.Remove(processId);
				}
			}

			return mainWindows;
		}

		private static string GetWindowTitle(IntPtr handle)
		{
			int length = User32NativeMethods.GetWindowTextLength(handle);
			if (length <= 0)
			{
				return string.Empty;
			}

			var buffer = new char[length + 1];
			int copied = User32NativeMethods.GetWindowText(handle, buffer, buffer.Length);
			return new string(buffer, 0, Math.Max(0, copied));
		}

		/// <summary>
		/// Used to kill every client outright. Closing a client's window first lets EVE shut down the
		/// way it does when you close it yourself (writing out its settings); only clients still running
		/// after GracefulCloseTimeout - stuck, or held open by EVE's own quit prompt - are killed.
		/// </summary>
		public Task CloseAllMonitoredClientsAsync()
		{
			return ProcessMonitor.CloseGracefullyAsync(EveOnlineClientProcessName, ProcessMonitor.GracefulCloseTimeout);
		}

		internal static async Task CloseGracefullyAsync(string processName, TimeSpan timeout)
		{
			Process[] clients = Process.GetProcessesByName(processName);
			try
			{
				foreach (Process client in clients)
				{
					try
					{
						client.CloseMainWindow();
					}
					catch (InvalidOperationException)
					{
						// Already gone.
					}
				}

				DateTime deadline = DateTime.UtcNow + timeout;
				while (DateTime.UtcNow < deadline && clients.Any(client => !ProcessMonitor.HasExited(client)))
				{
					await Task.Delay(250);
				}

				foreach (Process client in clients.Where(client => !ProcessMonitor.HasExited(client)))
				{
					try
					{
						client.Kill(entireProcessTree: true);
					}
					catch (InvalidOperationException)
					{
					}
					catch (System.ComponentModel.Win32Exception)
					{
					}
				}
			}
			finally
			{
				foreach (Process client in clients)
				{
					client.Dispose();
				}
			}
		}

		private static bool HasExited(Process process)
		{
			try
			{
				return process.HasExited;
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
			{
				// Not ours to watch (e.g. elevated); waiting on it can't help.
				return true;
			}
		}
	}
}
