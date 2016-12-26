using System;
namespace Xamarin.Android.Tools.ClassBrowser
{
	public class SourceIdentifier
	{
		public SourceIdentifier (string sourceUri)
		{
			SourceUri = sourceUri;
		}

		public string SourceUri { get; set; }
	}
}
