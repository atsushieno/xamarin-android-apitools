using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Android.Tools.ApiXmlAdjuster;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public static class JavaApiExtensions
	{
		static Dictionary<object, IList<object>> extensions = new Dictionary<object, IList<object>> ();

		public static T GetExtension<T> (this object javaObject) where T : class
		{
			IList<object> list;
			if (!extensions.TryGetValue (javaObject, out list))
				return default (T);
			return list.OfType<T> ().FirstOrDefault ();
		}

		public static void SetExtension<T> (this object javaObject, T value) where T : class
		{
			IList<object> list;
			if (!extensions.TryGetValue (javaObject, out list))
				extensions [javaObject] = list = new List<object> ();
			T existing = list.OfType<T> ().FirstOrDefault ();
			if (existing != null)
				list.Remove (existing);
			list.Add (value);
		}
	}
}
