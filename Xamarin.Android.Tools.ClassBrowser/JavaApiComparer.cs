using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Android.Tools.ApiXmlAdjuster;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public class JavaApiComparer
	{
		public IEnumerable<string> Diff (JavaApi reference, JavaApi target)
		{
			var rtypes = reference.Packages.SelectMany (p => p.Types);
			foreach (var rtype in rtypes) {
				var ttype = target.Packages.FirstOrDefault (_ => _.Name == rtype.Parent.Name)?.Types?.FirstOrDefault (t => t.Name == rtype.Name);
				if (ttype == null) {
					yield return $"Type '{rtype.FullName}' does not exist in the target API.";
					continue;
				}
				foreach (var report in DiffType (rtype, ttype))
					yield return report;
			}
		}

		IEnumerable<string> DiffType (JavaType rtype, JavaType ttype)
		{
			return Enumerable.Empty<string> ()
					 //.Concat (CompareProperty (nameof (rtype.Abstract), rtype, ttype, _ => _.Abstract))
					 //.Concat (CompareProperty (nameof (rtype.Deprecated), rtype, ttype, _ => _.Deprecated))
				         //.Concat (CompareProperty (nameof (rtype.ExtendedJniSignature), rtype, ttype, _ => _.ExtendedJniSignature))
				         //.Concat (CompareProperty (nameof (rtype.Final), rtype, ttype, _ => _.Final))
				         //.Concat (CompareProperty (nameof (rtype.Static), rtype, ttype, _ => _.Static))
				         //.Concat (CompareProperty (nameof (rtype.Visibility), rtype, ttype, _ => _.Visibility))
				         //.Concat (CompareImplements (rtype, ttype))
				         //.Concat (CompareTypeParameters (rtype.Name, ttype.Name, rtype.TypeParameters, ttype.TypeParameters))
					 ;
		}

		IEnumerable<string> CompareProperty<T> (string propertyName, JavaType rtype, JavaType ttype, Func<JavaType, T> getProperty)
		{
			var rprop = getProperty (rtype);
			var tprop = getProperty (ttype);
			bool isRNull = false, isTNull = false;
			if (typeof (T).IsClass && (isRNull = (object) rprop == null) && (isTNull = (object) tprop == null))
				yield break;
			if (!isRNull && !isTNull && rprop.Equals (getProperty (ttype)))
				yield break;
			yield return $"{rtype.FullName}.{propertyName}: expected {getProperty (rtype)}, got {getProperty (ttype)}.";
		}

		IEnumerable<string> CompareImplements (JavaType rtype, JavaType ttype)
		{
			foreach (var rImpl in rtype.Implements)
				if (ttype.Implements.All (_ => _.Name != rImpl.Name))
					yield return $"{ttype} does not implement interface {rImpl.Name}.";
		}

		IEnumerable<string> CompareTypeParameters (string reference, string target, JavaTypeParameters rTPs, JavaTypeParameters tTPs)
		{
			if (rTPs == null)
				yield break;
			if (tTPs == null)
				yield return $"{target} does not have any type parameter.";
			else {
				foreach (var rTP in rTPs.TypeParameters) {
					var tTP = tTPs?.TypeParameters?.FirstOrDefault (_ => _.Name == rTP.Name);
					if (tTP == null)
						yield return $"{target} does not have type parameter {rTP.Name}.";
				}
			}
		}
	}
}
