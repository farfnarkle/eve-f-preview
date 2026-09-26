using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Mediator.Messages;
using EveFPreview.Services;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailOverlayLabelUpdatedHandler : INotificationHandler<ThumbnailOverlayLabelUpdated>
	{
		private readonly IThumbnailManager _manager;

		public ThumbnailOverlayLabelUpdatedHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task Handle(ThumbnailOverlayLabelUpdated notification, CancellationToken cancellationToken)
		{
			this._manager.UpdateOverlayLabels();

			return Task.CompletedTask;
		}
	}
}
