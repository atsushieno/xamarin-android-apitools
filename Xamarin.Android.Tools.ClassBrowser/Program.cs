using System;
using Xwt;

namespace Xamarin.Android.Tools.ClassBrowser
{
	class ClassBrowserApplication
	{
		public static void Main (string [] args)
		{
			Application.Initialize ();
			new MainWindow () { Width = 100 }.Show ();
			Application.Run ();
		}
	}
}
