using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Mediator.Messages;
using EveFPreview.Services;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Services
{
	sealed class StartStopServiceHandler : IRequestHandler<StartService>, IRequestHandler<StopService>
	{
		private readonly IThumbnailManager _manager;

		public StartStopServiceHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task<Unit> Handle(StartService message, CancellationToken cancellationToken)
		{
			this._manager.Start();

			return Unit.Task;
		}

		public Task<Unit> Handle(StopService message, CancellationToken cancellationToken)
		{
			this._manager.Stop();

			return Unit.Task;
		}
	}
}