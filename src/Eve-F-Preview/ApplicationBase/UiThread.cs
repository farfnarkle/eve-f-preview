using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EveFPreview
{
	/// <summary>Marshals work onto the WPF UI thread (the one running the application's dispatcher).</summary>
	static class UiThread
	{
		private static Dispatcher Dispatcher => Application.Current?.Dispatcher;

		/// <summary>Runs <paramref name="action"/> inline when already on the UI thread (or before the app exists), otherwise queues it there.</summary>
		public static void Run(Action action)
		{
			Dispatcher dispatcher = UiThread.Dispatcher;
			if (dispatcher == null || dispatcher.CheckAccess())
			{
				action();
				return;
			}

			dispatcher.BeginInvoke(action);
		}

		/// <summary>Runs <paramref name="action"/> on the UI thread (inline if already there) and completes when it has run.</summary>
		public static Task RunAsync(Action action)
		{
			Dispatcher dispatcher = UiThread.Dispatcher;
			if (dispatcher == null || dispatcher.CheckAccess())
			{
				action();
				return Task.CompletedTask;
			}

			return dispatcher.InvokeAsync(action).Task;
		}

		public static bool IsRequired
		{
			get
			{
				Dispatcher dispatcher = UiThread.Dispatcher;
				return dispatcher != null && !dispatcher.CheckAccess();
			}
		}
	}
}
