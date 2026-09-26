using System;
using System.IO;
using System.Windows;

namespace EveFPreview
{
	// A really very primitive exception handler stuff here
	// No IoC, no fancy DI containers - just a plain exception stacktrace dump
	// If this code is called then something was gone really bad
	// so even the DI infrastructure might be dead already.
	// So this dumb and non elegant approach is used
	sealed class ExceptionHandler
	{
		private const string EXCEPTION_DUMP_FILE_NAME = "EVE-F-Preview.log";
		private const string EXCEPTION_MESSAGE = "EVE-F-Preview has encountered a problem and needs to close. Additional information has been saved in the crash log file.";

		public void SetupExceptionHandlers(Application application)
		{
			if (System.Diagnostics.Debugger.IsAttached)
			{
				return;
			}

			application.DispatcherUnhandledException += delegate (Object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
			{
				this.ExceptionEventHandler(e.Exception);
			};

			AppDomain.CurrentDomain.UnhandledException += delegate (Object sender, UnhandledExceptionEventArgs e)
			{
				this.ExceptionEventHandler(e.ExceptionObject as Exception);
			};
		}

		private void ExceptionEventHandler(Exception exception)
		{
			try
			{
				// Next to the exe (not the working directory, which depends on how the app was
				// started), and appended with a timestamp so an earlier crash isn't overwritten.
				string logPath = Path.Combine(AppContext.BaseDirectory, ExceptionHandler.EXCEPTION_DUMP_FILE_NAME);
				string version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
				File.AppendAllText(logPath,
					$"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} (EVE-F-Preview {version}) ===={Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");

				MessageBox.Show(ExceptionHandler.EXCEPTION_MESSAGE, @"EVE-F-Preview", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			catch
			{
				// We are in unstable state now so even this operation might fail
				// Still we actually don't care anymore - anyway the application has been cashed
			}

			System.Environment.Exit(1);
		}
	}
}