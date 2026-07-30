using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Mediator.Messages;
using EveFPreview.Services;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailCycleGroupIndicatorUpdatedHandler : INotificationHandler<ThumbnailCycleGroupIndicatorUpdated>
	{
		private readonly IThumbnailManager _manager;

		public ThumbnailCycleGroupIndicatorUpdatedHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task Handle(ThumbnailCycleGroupIndicatorUpdated notification, CancellationToken cancellationToken)
		{
			this._manager.UpdateCycleGroupIndicator();

			return Task.CompletedTask;
		}
	}
}