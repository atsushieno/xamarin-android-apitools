using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Android.Tools.ApiXmlAdjuster;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public class ApiComparisonReport
	{
		public ApiComparisonIssue Issue { get; set; }
		public string Message { get; set; }
		public object Context { get; set; }
	}

	public enum ApiComparisonIssue
	{
		MissingType,
		MissingField,
		MissingConstructor,
		MissingMethod,
		TypePropertyMismatch,
		FieldPropertyMismatch,
		MethodPropertyMismatch,
		MissingInterfaceImplementation,
		MissingTypeParameter,
	}

	public class JavaApiComparer
	{
		public bool IgnoreSystemObjectOverrides { get; set; }
		
		public IEnumerable<ApiComparisonReport> Compare (JavaApi reference, JavaApi target)
		{
			var rtypes = reference.Packages.SelectMany (p => p.Types);
			foreach (var rtype in rtypes) {
				var ttype = target.Packages.FirstOrDefault (_ => _.Name == rtype.Parent.Name)?.Types?.FirstOrDefault (t => t.Name == rtype.Name);
				if (ttype == null)
					yield return new ApiComparisonReport { Context = ttype, Issue = ApiComparisonIssue.MissingType, Message = $"Type `{rtype.FullName}` does not exist in the target API." };
				else
					foreach (var r in CompareType (rtype, ttype))
						yield return r;
			}
		}

		IEnumerable<ApiComparisonReport> CompareType (JavaType rtype, JavaType ttype)
		{
			Enumerable.Empty<ApiComparisonReport> ()
				  // .Concat (CompareProperty (nameof (rtype.Abstract), rtype, ttype, _ => _.Abstract))
				  // .Concat (CompareProperty (nameof (rtype.Deprecated), rtype, ttype, _ => _.Deprecated))
				  // .Concat (CompareProperty (nameof (rtype.ExtendedJniSignature), rtype, ttype, _ => _.ExtendedJniSignature))
				  // .Concat (CompareProperty (nameof (rtype.Final), rtype, ttype, _ => _.Final))
				  // .Concat (CompareProperty (nameof (rtype.Static), rtype, ttype, _ => _.Static)) // static-ness is weird in Java to check by this tool...
				  // .Concat (CompareProperty (nameof (rtype.Visibility), rtype, ttype, _ => _.Visibility))
				  // .Concat (CompareImplements (rtype, ttype))
				  // .Concat (CompareTypeParameters (rtype.Name, ttype.Name, rtype.TypeParameters, ttype.TypeParameters))
				  ;
			var done = new List<JavaMember> ();
			foreach (var rf in rtype.Members.OfType<JavaField> ()) {
				var tf = ttype.Members.OfType<JavaField> ().FirstOrDefault (_ => _.Name == rf.Name);
				if (tf == null)
					yield return new ApiComparisonReport { Context = rf, Issue = ApiComparisonIssue.MissingField, Message = $"`{ttype.FullName}` misses field `{rf.Name}`." };
				else
					foreach (var r in CompareField (rf, tf))
						yield return r;
				done.Add (rf);
			}
			foreach (var rm in rtype.Members.OfType<JavaMethod> ()) {
				if (IgnoreSystemObjectOverrides) {
					switch (rm.Name) {
					case "hashCode":
					case "toString":
						if (rm.Parameters.Count == 0)
							continue;
						break;
					case "equals":
						if (rm.Parameters.Count == 1 && rm.Parameters [0].Type == "java.lang.Object")
							continue;
						break;
					}
				}
				var tm = ttype.Members.OfType<JavaMethod> ().Where (_ => _.Name == rm.Name).FirstOrDefault (_ => IsMatchMethod (rm, _));
				if (tm == null)
					yield return new ApiComparisonReport { Context = rm, Issue = ApiComparisonIssue.MissingMethod, Message = $"`{ttype.FullName}` misses method `{rm.ToString ()}`." };
				else
					foreach (var r in CompareMethod (rm, tm))
						yield return r;
				done.Add (rm);
			}
		}

		bool IsMatchMethod (JavaMethod reference, JavaMethod target)
		{
			return reference.Return == target.Return && reference.Parameters.Zip (target.Parameters, (s, d) => s.Type == d.Type).All (_ => _);
		}

		IEnumerable<ApiComparisonReport> CompareField (JavaField rf, JavaField tf)
		{
			// return CompareProperty (nameof (rf.Deprecated), rf, tf, _ => _.Deprecated);
			return CompareProperty (nameof (rf.Final), rf, tf, _ => _.Final);
		}

		IEnumerable<ApiComparisonReport> CompareMethod (JavaMethod rm, JavaMethod tm)
		{
			return Enumerable.Empty<ApiComparisonReport> ()
			          // .Concat (CompareProperty (nameof (rf.Deprecated), rf, tf, _ => _.Deprecated))
			          .Concat (CompareProperty (nameof (rm.Abstract), rm, tm, _ => _.Abstract))
			          .Concat (CompareProperty (nameof (rm.Static), rm, tm, _ => _.Static));
		}

		IEnumerable<ApiComparisonReport> CompareProperty<T> (string propertyName, JavaType rtype, JavaType ttype, Func<JavaType, T> getProperty)
		{
			return CompareProperty (propertyName, rtype, ttype, getProperty, _ => _.Name, ApiComparisonIssue.TypePropertyMismatch);
		}

		IEnumerable<ApiComparisonReport> CompareProperty<T> (string propertyName, JavaField rf, JavaField tf, Func<JavaField, T> getProperty)
		{
			return CompareProperty (propertyName, rf, tf, getProperty, _ => _.Parent.FullName + "." + _.Name, ApiComparisonIssue.FieldPropertyMismatch);
		}

		IEnumerable<ApiComparisonReport> CompareProperty<T> (string propertyName, JavaMethod rm, JavaMethod tm, Func<JavaMethod, T> getProperty)
		{
			return CompareProperty (propertyName, rm, tm, getProperty, _ => _.Parent.FullName + "." + _.ToString (), ApiComparisonIssue.MethodPropertyMismatch);
		}

		IEnumerable<ApiComparisonReport> CompareProperty<TT,TP> (string propertyName, TT robj, TT tobj, Func<TT, TP> getProperty, Func<TT,string> getName, ApiComparisonIssue issue)
		{
			var rprop = getProperty (robj);
			var tprop = getProperty (tobj);
			bool isRNull = false, isTNull = false;
			if (typeof (TP).IsClass && (isRNull = (object)rprop == null) && (isTNull = (object)tprop == null))
				yield break;
			if (!isRNull && !isTNull && rprop.Equals (getProperty (tobj)))
				yield break;
			yield return new ApiComparisonReport { Context = robj, Issue = issue, Message = $"`{getName (robj)}` - `{propertyName}`: expected `{getProperty (robj)}`, got `{getProperty (tobj)}`." };
		}

		IEnumerable<ApiComparisonReport> CompareImplements (JavaType rtype, JavaType ttype)
		{
			foreach (var rImpl in rtype.Implements)
				if (ttype.Implements.All (_ => _.Name != rImpl.Name))
					yield return new ApiComparisonReport { Context = rtype, Issue = ApiComparisonIssue.MissingInterfaceImplementation, Message = $"`{ttype.FullName}` does not implement interface `{rImpl.Name}`." };
		}

		IEnumerable<ApiComparisonReport> CompareTypeParameters (string reference, string target, JavaTypeParameters rTPs, JavaTypeParameters tTPs)
		{
			if (rTPs == null)
				yield break;
			if (tTPs == null)
				yield return new ApiComparisonReport { Context = rTPs, Issue = ApiComparisonIssue.MissingTypeParameter, Message = $"`{target}` does not have any type parameter." };
			else {
				foreach (var rTP in rTPs.TypeParameters) {
					var tTP = tTPs?.TypeParameters?.FirstOrDefault (_ => _.Name == rTP.Name);
					if (tTP == null)
						yield return new ApiComparisonReport { Context = rTP, Issue = ApiComparisonIssue.MissingTypeParameter, Message = $"`{target}` does not have type parameter `{rTP.Name}`." };
				}
			}
		}
	}
}
