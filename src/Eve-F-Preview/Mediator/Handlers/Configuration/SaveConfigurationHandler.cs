using System.Threading;
using System.Threading.Tasks;
using EveFPreview.Configuration;
using EveFPreview.Mediator.Messages;
using MediatR;

namespace EveFPreview.Mediator.Handlers.Configuration
{
	sealed class SaveConfigurationHandler : IRequestHandler<SaveConfiguration>
	{
		private readonly IConfigurationStorage _storage;

		public SaveConfigurationHandler(IConfigurationStorage storage)
		{
			this._storage = storage;
		}

		public Task<Unit> Handle(SaveConfiguration message, CancellationToken cancellationToken)
		{
			this._storage.Save();

			return Unit.Task;
		}
	}
}