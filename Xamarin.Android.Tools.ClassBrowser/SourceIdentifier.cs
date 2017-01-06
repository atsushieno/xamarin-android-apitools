using System;
using System.Collections.Generic;
using System.Linq;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public class SourceIdentifiers
	{
		static List<IList<SourceIdentifier>> lists = new List<IList<SourceIdentifier>> ();

		public static IList<SourceIdentifier> Get (params SourceIdentifier [] identifiers)
		{
			return lists.FirstOrDefault (l => l.Count == identifiers.Length && l.All (i => identifiers.Contains (i)));
		}

		public static void Add (params SourceIdentifier [] identifiers)
		{
			if (Get (identifiers) == null)
				lists.Add (new List<SourceIdentifier> (identifiers));
		}
	}

	public class SourceIdentifier
	{
		public SourceIdentifier (string sourceUri)
		{
			SourceUri = sourceUri;
		}

		public string SourceUri { get; set; }
	}
}
