﻿using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Android.Tools.Bytecode;
using Xamarin.Android.Tools.ApiXmlAdjuster;
using System.IO;
using System.Xml;
using Mono.Cecil;
using System.Diagnostics;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public class ClassBrowserModel
	{
		void LoadApk (string file, string sourceIdentifier = null)
		{
			sourceIdentifier = sourceIdentifier ?? file;
			var dir = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ().Substring (0, 8));
			Directory.CreateDirectory (dir);
			try {
				using (var zip = Xamarin.Tools.Zip.ZipArchive.Open (file, FileMode.Open)) {
					foreach (var dex in zip.AsEnumerable ()
						 .Where (e => Path.GetExtension (e.FullName).Equals (".dex", StringComparison.OrdinalIgnoreCase))) {
						string dexfile = dex.Extract (destinationDir: dir);
						LoadDex (dexfile, file);
						File.Delete (dexfile);
					}
				}
			} finally {
				Directory.Delete (dir);
			}
		}

		void LoadDex (string file, string sourceIdentifier = null)
		{
			bool isUnix = Environment.OSVersion.Platform == PlatformID.Unix;
			var ext = isUnix ? ".sh" : ".bat";
			var jar = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ().Substring (0, 8) + Path.GetFileNameWithoutExtension (file) + ".jar");
			var appPath = Path.GetDirectoryName (System.Reflection.Assembly.GetEntryAssembly ().Location);
			var psi = isUnix ?
				new ProcessStartInfo ("bash", Path.Combine (appPath, "dex2jar", "d2j-dex2jar" + ext) + $" -o {jar} {file}") :
				new ProcessStartInfo (Path.Combine (appPath, "dex2jar", "d2j-dex2jar" + ext), $"-o {jar} {file}"); // maybe it works?
			var proc = Process.Start (psi);
			proc.WaitForExit ();
			if (proc.ExitCode == 0) {
				LoadJar (jar, sourceIdentifier);
				File.Delete (jar);
			}
			else
				throw new ApplicationException ($"dex2jar failed at exit code {proc.ExitCode}");
		}

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
				var type = td.IsInterface ? (JavaType) new JavaInterface (pkg) : new JavaClass (pkg);
				type.Name = tatt.Name;
				type.SetExtension (td);
				pkg.Types.Add (type);
				foreach (var fa in td.Fields
					 .Select (f => new Tuple<FieldDefinition, CustomAttribute> (f, GetRegisteredAttribute (f)))
					 .Where (p => p.Item2 != null)) {
					var matt = PopulateRegisterAttributeInfo (fa.Item2);
					var f = new JavaField (type) { Name = matt.Name, Static = fa.Item1.IsStatic, Final = fa.Item1.HasConstant };
					f.SetExtension (fa.Item1);
					type.Members.Add (f);
				}
				foreach (var ma in GetAllMethods (td)
					 .Where (m => m != null)
					 .Select (m => new Tuple<MethodDefinition, CustomAttribute> (m, GetRegisteredAttribute (m)))
					 .Where (p => p.Item2 != null)) {
					var matt = PopulateRegisterAttributeInfo (ma.Item2);
					var m = new JavaMethod (type) { Name = matt.Name, Abstract = ma.Item1.IsAbstract, Static = ma.Item1.IsStatic  };
					var jniParameters = matt.JniSignature.Substring (0, matt.JniSignature.IndexOf (')')).Substring (1);
					m.Return = ParseJniParameters (matt.JniSignature.Substring (matt.JniSignature.IndexOf (')') + 1)).First ();
					m.Parameters = ParseJniParameters (jniParameters)
						.Zip (ma.Item1.Parameters, (s, mp) => new { Type = s, ManagedParameter = mp })
						.Select (_ => new JavaParameter (m) { Name = _.ManagedParameter.Name, Type = _.Type })
						.ToArray ();
					m.SetExtension (ma.Item1);
					type.Members.Add (m);
				}
			}
			FillSourceIdentifier (Api, sourceIdentifier ?? file);
		}

		IEnumerable<string> ParseJniParameters (string jni)
		{
			int idx = 0;
			while (idx < jni.Length)
				yield return ParseJniParameter (jni, ref idx);
		}

		string ParseJniParameter (string jni, ref int pos)
		{
			switch (jni [pos++]) {
			case 'Z':
				return "bool";
			case 'B':
				return "byte";
			case 'C':
				return "char";
			case 'S':
				return "short";
			case 'I':
				return "int";
			case 'J':
				return "long";
			case 'F':
				return "float";
			case 'D':
				return "double";
			case 'V':
				return "void";
			case '[':
				return ParseJniParameter (jni, ref pos) + "[]";
			case 'L':
				var ret = jni.Substring (pos, jni.IndexOf (';', pos) - pos).Replace ('/', '.');
				pos += ret.Length + 1;
				return ret;
			}
			throw new Exception ($"Unexpected JNI description: {jni} ({jni[pos - 1]})");
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
				case ".apk":
					LoadApk (file, identifer);
					break;
				case ".dex":
					LoadDex (file, identifer);
					break;
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
				return Directory.GetDirectories (Path.Combine (XamarinAndroidSdkPath, "lib", "xamarin.android", "xbuild-frameworks", "MonoAndroid")).SelectMany (d => Directory.GetFiles (d, "Mono.Android.dll"));
			}
		}
	}
}
