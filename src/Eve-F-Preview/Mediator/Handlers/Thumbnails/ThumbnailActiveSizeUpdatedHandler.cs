using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Mediator.Messages;
using EveFPreview.Presenters;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailActiveSizeUpdatedHandler : INotificationHandler<ThumbnailActiveSizeUpdated>
	{
		private readonly IMainFormPresenter _presenter;

		public ThumbnailActiveSizeUpdatedHandler(MainFormPresenter presenter)
		{
			this._presenter = presenter;
		}

		public Task Handle(ThumbnailActiveSizeUpdated notification, CancellationToken cancellationToken)
		{
			this._presenter.UpdateThumbnailSize(notification.Value);

			return Task.CompletedTask;
		}
	}
}