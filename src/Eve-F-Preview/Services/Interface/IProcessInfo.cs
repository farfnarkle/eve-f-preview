using System;

namespace EveFPreview.Services
{
	public interface IProcessInfo
	{
		IntPtr Handle { get; }
		string Title { get; }

		/// <summary>True for a non-EVE program added on the Other apps page (Title is then its executable name).</summary>
		bool IsExternalApp { get; }
	}
}
