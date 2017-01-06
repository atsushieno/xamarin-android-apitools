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
		void LoadAar (string file, string sourceIdentifier = null)
		{
			sourceIdentifier = sourceIdentifier ?? file;
			using (var zip = Xamarin.Tools.Zip.ZipArchive.Open (file, FileMode.Open)) {
				foreach (var jar in zip.AsEnumerable ()
					 .Where (e => Path.GetExtension (e.FullName).Equals (".jar", StringComparison.OrdinalIgnoreCase))) {
					string jarfile = jar.Extract ();
					LoadJar (jarfile, file);
					File.Delete (jarfile);
				}
			}
		}

		void LoadJar (string file, string sourceIdentifier = null)
		{
			sourceIdentifier = sourceIdentifier ?? file;
			var tw = new StringWriter ();
			new ClassPath (file).SaveXmlDescription (tw);
			tw.Close ();
			using (var xr = XmlReader.Create (new StringReader (tw.ToString ())))
				LoadXml (xr, sourceIdentifier);
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
			var name = a.ConstructorArguments [0].Value.ToString ().Replace ('$', '.');
			var idx = isType ? name.LastIndexOf ('/') : -1;
			ret.Package = idx < 0 ? string.Empty : name.Substring (0, idx).Replace ('/', '.');
			ret.Name = idx < 0 ? name : name.Substring (ret.Package.Length + 1);
			if (a.ConstructorArguments.Count () > 1)
				ret.JniSignature = a.ConstructorArguments [1].Value.ToString ();
			var since = a.HasProperties ? a.Properties.FirstOrDefault (p => p.Name == "ApiSince") : default (CustomAttributeNamedArgument);
			ret.ApiSince = since.Argument.Value != null ? (int)since.Argument.Value : 0;
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

		void FillSourceIdentifier (JavaApi api, string sourceIdentifier)
		{
			var ident = new SourceIdentifier (sourceIdentifier);
			Action<JavaMember> processMember = (member) => {
				var existing = member.GetExtension<SourceIdentifier> ();
				if (existing == null)
					member.SetExtension (ident);
			};
			Action<JavaType> processType = (type) => {
				var existing = type.GetExtension<SourceIdentifier> ();
				if (existing == null)
					type.SetExtension (ident);
				foreach (var member in type.Members)
					processMember (member);
			};
			Action<JavaPackage> processPackage = (pkg) => {
				var existing = pkg.GetExtension<SourceIdentifier> ();
				if (existing == null)
					pkg.SetExtension (ident);
				foreach (var type in pkg.Types)
					processType (type);
			};

			foreach (var pkg in api.Packages)
				processPackage (pkg);
		}

		void LoadDll (string file, string sourceIdentifier = null)
		{
			foreach (var ta in AssemblyDefinition.ReadAssembly (file).Modules.SelectMany (m => m.Types.SelectMany (t => FlattenTypeHierarchy (t)))
				 .Where (ta => !ta.Name.EndsWith ("Invoker", StringComparison.Ordinal) && !ta.Name.EndsWith ("Implementor", StringComparison.Ordinal))
				 .Select (t => new Tuple<TypeDefinition, CustomAttribute> (t, GetRegisteredAttribute (t)))
				 .Where (p => p.Item2 != null)) {
				var td = ta.Item1;
				var tatt = PopulateRegisterAttributeInfo (ta.Item2, true);
				var pkg = Api.Packages.FirstOrDefault (p => p.Name == tatt.Package);
				if (pkg == null)
					Api.Packages.Add (pkg = new JavaPackage (Api) { Name = tatt.Package });
				var type = td.IsInterface ? (JavaType)new JavaInterface (pkg) : new JavaClass (pkg);
				type.Name = tatt.Name;
				type.SetExtension (td);
				pkg.Types.Add (type);
				foreach (var fa in td.Fields
					 .Select (f => new Tuple<FieldDefinition, CustomAttribute> (f, GetRegisteredAttribute (f)))
					 .Where (p => p.Item2 != null)) {
					var matt = PopulateRegisterAttributeInfo (fa.Item2);
					var f = new JavaField (type) { Name = matt.Name };
					f.SetExtension (fa.Item1);
					type.Members.Add (f);
				}
				foreach (var ma in GetAllMethods (td)
					 .Where (m => m != null)
					 .Select (m => new Tuple<MethodDefinition, CustomAttribute> (m, GetRegisteredAttribute (m)))
					 .Where (p => p.Item2 != null)) {
					var matt = PopulateRegisterAttributeInfo (ma.Item2);
					var m = new JavaMethod (type) { Name = matt.Name };
					m.SetExtension (ma.Item1);
					type.Members.Add (m);
				}
			}
			FillSourceIdentifier (Api, sourceIdentifier ?? file);
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

		void LoadXml (string file, string sourceIdentifier = null)
		{
			sourceIdentifier = sourceIdentifier ?? file;
			using (var xr = XmlReader.Create (file))
				LoadXml (xr, sourceIdentifier);
		}

		void LoadXml (XmlReader reader, string sourceFile)
		{
			Api.Load (reader, false);
			FillSourceIdentifier (Api, sourceFile);
		}

		string GetFileId (string file)
		{
			var i = LoadedApiInfos.FirstOrDefault (_ => _.ApiFullPath == file);
			if (i == null) {
				var id = LoadedApiInfos.Count.ToString ();
				i = new LoadedApiInfo () { ApiFullPath = file, FileId = id, Selected = true };
				LoadedApiInfos.Add (i);
			}
			return i.FileId;
		}

		#region Events

		public event EventHandler ApiSetUpdated;

		void OnApiSetUpdated ()
		{
			if (ApiSetUpdated != null)
				ApiSetUpdated (this, EventArgs.Empty);
		}

		#endregion

		#region Public API manipulation

		public void ClearApi ()
		{
			LoadedApiInfos.Clear ();
			ApiSet.Clear ();
			ApiSet.Add (new JavaApi ());
			OnApiSetUpdated ();
		}

		public JavaApi Api {
			get {
				return ApiSet.Last ();
			}
		}

		public IList<JavaApi> ApiSet { get; private set; }

		public ClassBrowserModel ()
		{
			ApiSet = new List<JavaApi> ();
			ApiSet.Add (new JavaApi ());
		}

		public IList<LoadedApiInfo> LoadedApiInfos { get; private set; } = new List<LoadedApiInfo> ();

		public void LoadApiFromFiles (string [] files)
		{
			if (Api.Packages.Any ())
				ApiSet.Add (new JavaApi ());
			
			foreach (var file in files) {
				var identifer = GetFileId (file);
				switch (Path.GetExtension (file.ToLowerInvariant ())) {
				case ".aar":
					LoadAar (file, identifer);
					break;
				case ".jar":
					LoadJar (file, identifer);
					break;
				case ".dll":
					LoadDll (file, identifer);
					break;
				default: // load as XML
					LoadXml (file, identifer);
					break;
				}
			}
			OnApiSetUpdated ();
		}

		#endregion

		#region Prefefined Files
		public PredefinedLibraries PredefinedLibraries { get; private set; } = new PredefinedLibraries ();

		#endregion
	}

	public class LoadedApiInfo
	{
		public bool Selected { get; set; }
		public string ApiFullPath { get; set; }
		public string FileId { get; set; }
	}

	public class PredefinedLibraries
	{
		public PredefinedLibraries ()
		{
			AndroidSdkPath = Environment.GetEnvironmentVariable ("ANDROID_SDK_PATH");
			XamarinAndroidSdkPath = Environment.GetEnvironmentVariable ("MONO_ANDROID_PATH");
		}
		public string AndroidSdkPath { get; set; }
		public string XamarinAndroidSdkPath { get; set; }

		public IEnumerable<string> AndroidLibraries {
			get {
				if (string.IsNullOrEmpty (AndroidSdkPath) || !Directory.Exists (AndroidSdkPath))
					return new string [0];
				return Directory.GetDirectories (Path.Combine (AndroidSdkPath, "platforms")).SelectMany (d => Directory.GetFiles (d, "android.jar"));
			}
		}
		public IEnumerable<string> AndroidSdkExtraAars {
			get {
				if (string.IsNullOrEmpty (AndroidSdkPath) || !Directory.Exists (AndroidSdkPath))
					return new string [0];
				return Directory.GetDirectories (Path.Combine (AndroidSdkPath, "extras")).SelectMany (d => Directory.GetFiles (d, "*.aar", SearchOption.AllDirectories));
			}
		}
		public IEnumerable<string> XamarinAndroidLibraries {
			get {
				if (string.IsNullOrEmpty (XamarinAndroidSdkPath) || !Directory.Exists (XamarinAndroidSdkPath))
					return new string [0];
				return Directory.GetDirectories (Path.Combine (XamarinAndroidSdkPath, "lib", "xbuild-frameworks", "MonoAndroid")).SelectMany (d => Directory.GetFiles (d, "Mono.Android.dll"));
			}
		}
	}
}
