using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Mediator.Messages;
using EveFPreview.Services;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Thumbnails
{
	sealed class CharacterPortraitThumbnailListUpdatedHandler : INotificationHandler<ThumbnailListUpdated>
	{
		private readonly ICharacterPortraitService _characterPortraitService;

		public CharacterPortraitThumbnailListUpdatedHandler(ICharacterPortraitService characterPortraitService)
		{
			this._characterPortraitService = characterPortraitService;
		}

		public Task Handle(ThumbnailListUpdated notification, CancellationToken cancellationToken)
		{
			if (notification.Added.Count == 0)
			{
				return Task.CompletedTask;
			}

			_ = Task.Run(async () =>
			{
				await this._characterPortraitService.RefreshPortraitsAsync(notification.Added, forceRedownload: false, cancellationToken)
					.ConfigureAwait(false);
			}, cancellationToken);

			return Task.CompletedTask;
		}
	}
}
