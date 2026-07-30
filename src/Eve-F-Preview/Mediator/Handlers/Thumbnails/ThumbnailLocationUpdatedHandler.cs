using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Configuration;
using EveFPreview.Mediator.Messages;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailLocationUpdatedHandler : INotificationHandler<ThumbnailLocationUpdated>
	{
		private readonly IMediator _mediator;
		private readonly IThumbnailConfiguration _configuration;

		public ThumbnailLocationUpdatedHandler(IMediator mediator, IThumbnailConfiguration configuration)
		{
			this._mediator = mediator;
			this._configuration = configuration;
		}

		public Task Handle(ThumbnailLocationUpdated notification, CancellationToken cancellationToken)
		{
			this._configuration.SetThumbnailLocation(notification.ThumbnailName, notification.ActiveClientName, notification.Location);

			return this._mediator.Send(new SaveConfiguration(), cancellationToken);
		}
	}
}