using System.Windows;

namespace EveFPreview
{
	/// <summary>
	/// The WPF application object. It only carries the shared styles (App.xaml); startup - the
	/// single-instance check, DI wiring and running the main view - still happens in <see cref="Program"/>.
	/// </summary>
	public partial class App : Application
	{
	}
}
