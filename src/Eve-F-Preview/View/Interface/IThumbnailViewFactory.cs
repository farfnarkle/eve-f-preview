using System;
using System.Drawing;

namespace EveFPreview.View
{
	public interface IThumbnailViewFactory
	{
		IThumbnailView Create(IntPtr id, string title, Size size);
	}
}