using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Mediator.Messages;
using EveFPreview.Services;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailFrameSettingsUpdatedHandler : INotificationHandler<ThumbnailFrameSettingsUpdated>
	{
		private readonly IThumbnailManager _manager;

		public ThumbnailFrameSettingsUpdatedHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task Handle(ThumbnailFrameSettingsUpdated notification, CancellationToken cancellationToken)
		{
			this._manager.UpdateThumbnailFrames();

			return Task.CompletedTask;
		}
	}
}