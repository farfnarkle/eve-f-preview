using System;

namespace EveFPreview.Services.Implementation
{
	sealed class ProcessInfo : IProcessInfo
	{
		public ProcessInfo(IntPtr handle, string title, bool isExternalApp = false)
		{
			this.Handle = handle;
			this.Title = title;
			this.IsExternalApp = isExternalApp;
		}

		public IntPtr Handle { get; }
		public string Title { get; }
		public bool IsExternalApp { get; }
	}
}
