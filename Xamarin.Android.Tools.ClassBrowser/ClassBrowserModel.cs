using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Android.Tools.Bytecode;
using Xamarin.Android.Tools.ApiXmlAdjuster;
using System.IO;
using System.IO.Compression;
using System.Xml;
using Mono.Cecil;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public class ClassBrowserModel
	{
		public void LoadFiles (string [] files)
		{
			Api = new JavaApi ();
			foreach (var file in files) {
				switch (Path.GetExtension (file.ToLowerInvariant ())) {
				case ".aar":
					LoadAar (file);
					break;
				case ".jar":
					LoadJar (file);
					break;
				case ".dll":
					LoadDll (file);
					break;
				default: // load as XML
					LoadXml (file);
					break;
				}
			}
			//libs = files.Select (file => new ClassPath (file)).ToList ();
			OnApiSetUpdated ();
		}

		void LoadAar (string file)
		{
			using (var zip = Xamarin.Tools.Zip.ZipArchive.Open (file, FileMode.Open)) {
				foreach (var jar in zip.AsEnumerable ()
					 .Where (e => Path.GetExtension (e.FullName).Equals (".jar", StringComparison.OrdinalIgnoreCase))) {
					var ms = new MemoryStream ();
					string jarfile = jar.Extract ();
					LoadJar (jarfile);
					File.Delete (jarfile);
				}
			}
		}

		void LoadJar (string file)
		{
			var tw = new StringWriter ();
			new ClassPath (file).SaveXmlDescription (tw);
			tw.Close ();
			using (var xr = XmlReader.Create (new StringReader (tw.ToString ())))
				LoadXml (xr);
		}

		class RegisterAttributeInfo
		{
			public string Package { get; set; }
			public string Name { get; set; }
			public string Return { get; set; }
			public string JniSignature { get; set; }
			public int ApiSince { get; set; }
		}

		RegisterAttributeInfo PopulateRegisterAttributeInfo (CustomAttribute a, bool isType = false)
		{
			var ret = new RegisterAttributeInfo ();
			var name = a.ConstructorArguments [0].Value.ToString ();
			var idx = isType ? name.LastIndexOf ('/') : -1;
			ret.Package = idx< 0 ? string.Empty : name.Substring (0, idx).Replace ('/', '.');
			ret.Name = idx < 0 ? name : name.Substring (ret.Package.Length + 1);
			if (a.ConstructorArguments.Count () > 1)
				ret.JniSignature = a.ConstructorArguments [1].Value.ToString ();
			var since = a.HasProperties ? a.Properties.FirstOrDefault (p => p.Name == "ApiSince") : default (CustomAttributeNamedArgument);
			ret.ApiSince = since.Argument.Value != null ? (int) since.Argument.Value : 0;
			return ret;
		}

		CustomAttribute GetRegisteredAttribute (IMemberDefinition m)
		{
			return m.CustomAttributes.FirstOrDefault (a => a.AttributeType.Namespace == "Android.Runtime" && a.AttributeType.Name == "RegisterAttribute");
		}

		IEnumerable<TypeDefinition> FlattenTypeHierarchy (TypeDefinition td)
		{
			yield return td;
			foreach (var nt in td.NestedTypes)
				foreach (var tt in FlattenTypeHierarchy (nt))
					yield return tt;
		}

		void LoadDll (string file)
		{
			foreach (var ta in AssemblyDefinition.ReadAssembly (file).Modules.SelectMany (m => m.Types.SelectMany (t => FlattenTypeHierarchy (t)))
			         .Where (ta => !ta.Name.EndsWith ("Invoker", StringComparison.Ordinal) && !ta.Name.EndsWith ("Implementor", StringComparison.Ordinal))
			         .Select (t => new Tuple<TypeDefinition,CustomAttribute> (t, GetRegisteredAttribute (t)))
			         .Where (p => p.Item2 != null)) {
				var td = ta.Item1;
				var tatt = PopulateRegisterAttributeInfo (ta.Item2, true);
				var pkg = Api.Packages.FirstOrDefault (p => p.Name == tatt.Package);
				if (pkg == null)
					Api.Packages.Add (pkg = new JavaPackage (Api) { Name = tatt.Package });
				var type = td.IsInterface ? (JavaType) new JavaInterface (pkg) : new JavaClass (pkg);
				type.Name = tatt.Name;
				pkg.Types.Add (type);
				foreach (var fa in td.Fields
				         .Select (f => new Tuple<FieldDefinition, CustomAttribute> (f, GetRegisteredAttribute (f)))
				         .Where (p => p.Item2 != null)) {
					var matt = PopulateRegisterAttributeInfo (fa.Item2);
					type.Members.Add (new JavaField (type) { Name = matt.Name });
				}
				foreach (var ma in GetAllMethods (td)
				         .Where (m => m != null)
				         .Select (m => new Tuple<MethodDefinition, CustomAttribute> (m, GetRegisteredAttribute (m)))
				         .Where (p => p.Item2 != null)) {
					var matt = PopulateRegisterAttributeInfo (ma.Item2);
					type.Members.Add (new JavaMethod (type) { Name = matt.Name });
				}
			}
		}

		IEnumerable<MethodDefinition> GetAllMethods (TypeDefinition td)
		{
			foreach (var p in td.Properties) {
				yield return p.GetMethod;
				yield return p.SetMethod;
			}
			foreach (var m in td.Methods)
				yield return m;
		}

		void LoadXml (string file)
		{
			using (var xr = XmlReader.Create (file))
				LoadXml (xr);
		}

		void LoadXml (XmlReader reader)
		{
			Api.Load (reader, false);
		}

		public event EventHandler ApiSetUpdated;

		void OnApiSetUpdated ()
		{
			if (ApiSetUpdated != null)
				ApiSetUpdated (this, EventArgs.Empty);
		}

		public JavaApi Api { get; private set; }
	}
}
