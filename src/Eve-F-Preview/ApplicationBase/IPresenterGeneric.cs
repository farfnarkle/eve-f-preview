namespace EveFPreview
{
	public interface IPresenter<in TArgument>
	{
		void Run(TArgument args);
	}
}