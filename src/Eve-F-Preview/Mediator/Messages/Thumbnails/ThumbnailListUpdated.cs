using System.Collections.Generic;
using MediatR;

namespace EveFPreview.Mediator.Messages
{
	sealed class ThumbnailListUpdated : INotification
	{
		public ThumbnailListUpdated(IList<string> addedThumbnails, IList<string> removedThumbnails)
		{
			this.Added = addedThumbnails;
			this.Removed = removedThumbnails;
		}

		public IList<string> Added { get; }
		public IList<string> Removed { get; }
	}
}