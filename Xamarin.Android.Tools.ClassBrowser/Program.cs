using System;
using Xwt;

namespace Xamarin.Android.Tools.ClassBrowser
{
	class ClassBrowserApplication
	{
		public static void Main (string [] args)
		{
			Application.Initialize ();
			var mainWindow = new MainWindow () { Width = 100 };
			mainWindow.Closed += (sender, e) => Application.Exit ();
			mainWindow.Show ();
			Application.Run ();
		}
	}
}
