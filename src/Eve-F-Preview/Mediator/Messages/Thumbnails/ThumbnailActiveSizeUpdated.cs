using System.Drawing;

namespace EveFPreview.Mediator.Messages
{
	sealed class ThumbnailActiveSizeUpdated : NotificationBase<Size>
	{
		public ThumbnailActiveSizeUpdated(Size size)
				: base(size)
		{
		}
	}
}