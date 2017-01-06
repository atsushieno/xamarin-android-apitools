using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xamarin.Android.Tools.ApiXmlAdjuster;
using Xwt;
using Mono.Cecil;

namespace Xamarin.Android.Tools.ClassBrowser
{
	public class MainWindow : Window
	{
		ClassBrowserModel model = new ClassBrowserModel ();

		public MainWindow ()
		{
			var vbox = new VBox () { WidthRequest = 600, HeightRequest = 500 };

			var menu = new Menu ();
			var commands = new List<KeyValuePair<string,List<KeyValuePair<Command, Action>>>> ();
			var fileCommands = new List<KeyValuePair<Command, Action>> ();
			fileCommands.Add (new KeyValuePair<Command, Action> (new Command ("_Open"), () => OpenJavaLibraries ()));
			fileCommands.Add (new KeyValuePair<Command, Action> (new Command ("Clear"), () => ClearJavaLibraries ()));
			fileCommands.Add (new KeyValuePair<Command, Action> (new Command ("_Exit"), () => CloseApplicationWindow ()));
			commands.Add (new KeyValuePair<string, List<KeyValuePair<Command, Action>>> ("_File", fileCommands));

			foreach (var cl in commands) {
				var submenu = new Menu ();
				foreach (var item in cl.Value) {
					var mi = new MenuItem (item.Key);
					mi.Clicked += (sender, e) => item.Value ();
					submenu.Items.Add (mi);
				}
				menu.Items.Add (new MenuItem () { Label = cl.Key, SubMenu = submenu });
			}

			var quickLoadMenu = new Menu ();
			Func<string, string> genMenuItemNameSdk = s => Path.Combine (Path.GetFileName (Path.GetDirectoryName (s)), Path.GetFileName (s));
			Func<string, Action> genLoad = s => () => ThreadPool.QueueUserWorkItem ((state) => model.LoadApiFromFiles (new string [] { s }), null);
			Func<string, Func<string, string>, MenuItem> genMenuItem = (s, genMenuItemName) => {
				var mi = new MenuItem (genMenuItemName (s));
				mi.Clicked += (sender, e) => genLoad (s) ();
				return mi;
			};
			var androidLibs = new MenuItem () { Label = "_Android SDK", SubMenu = new Menu () };
			foreach (var s in model.PredefinedLibraries.AndroidLibraries)
				androidLibs.SubMenu.Items.Add (genMenuItem (s, genMenuItemNameSdk));
			quickLoadMenu.Items.Add (androidLibs);

			var extrasLibs = new MenuItem () { Label = "_Android SDK Extras", SubMenu = new Menu () };
			Func<string, string> genMenuItemNameExtras = s => Path.Combine (Path.GetFileName (Path.GetDirectoryName (s)), Path.GetFileName (s));
			var extraCategories = new Dictionary<string,string> {
				{"databinding", "android/m2repository/com/android/databinding"},
				{"support library", "android/m2repository/com/android/support"},
				{"google (play)", "google/m2repository/com/google/android"},
				{"firebase", "google/m2repository/com/google/firebase"},
			};
			var extraSubdirs = model.PredefinedLibraries.AndroidSdkExtraAars.Select (s => Path.GetDirectoryName (Path.GetDirectoryName (s))).Distinct ().ToList ();
			var populated = new List<string> ();
			var extraAars = model.PredefinedLibraries.AndroidSdkExtraAars.ToList ();
			foreach (var categoryPair in extraCategories) {
				var category = categoryPair.Value.Replace ('/', Path.DirectorySeparatorChar);
				var catFullPath = Path.Combine (model.PredefinedLibraries.AndroidSdkPath, "extras", category);
				var cmi = new MenuItem (categoryPair.Key) { SubMenu = new Menu () };
				var matchSubdirs = extraSubdirs.Where (_ => _.StartsWith (catFullPath, StringComparison.OrdinalIgnoreCase)).ToList ();
				foreach (var subdirFullPath in matchSubdirs) {
					var subdir = subdirFullPath.Substring (catFullPath.Length + 1);
					var smi = new MenuItem (subdir) { SubMenu = new Menu () };
					foreach (var s in extraAars.Where (_ => _.StartsWith (subdirFullPath, StringComparison.OrdinalIgnoreCase))) {
						smi.SubMenu.Items.Add (genMenuItem (s, genMenuItemNameExtras));
						populated.Add (s);
					}
					cmi.SubMenu.Items.Add (smi);
				}
				extrasLibs.SubMenu.Items.Add (cmi);
			}
			var miscSmi = new MenuItem ("(others)") { SubMenu = new Menu () };
			foreach (var s in model.PredefinedLibraries.AndroidSdkExtraAars.Except (populated))
				miscSmi.SubMenu.Items.Add (genMenuItem (s, _ => _.Substring (Path.Combine (model.PredefinedLibraries.AndroidSdkPath, "extras").Length + 1)));
			extrasLibs.SubMenu.Items.Add (miscSmi);
			quickLoadMenu.Items.Add (extrasLibs);

			var xaLibs = new MenuItem () { Label = "_Xamarin Android SDK", SubMenu = new Menu () };
			foreach (var s in model.PredefinedLibraries.XamarinAndroidLibraries)
				xaLibs.SubMenu.Items.Add (genMenuItem (s, genMenuItemNameSdk));
			quickLoadMenu.Items.Add (xaLibs);

			menu.Items.Add (new MenuItem () { Label = "_Quick Load", SubMenu = quickLoadMenu });

			this.MainMenu = menu;

			var vpaned = new VPaned ();

			var idList = new ListView () { ExpandHorizontal = true, HeightRequest = 30 };
			var selectedField = new DataField<bool> ();
			var idField = new DataField<string> ();
			var fileField = new DataField<string> ();
			var listModel = new ListStore (selectedField, idField, fileField);
			idList.DataSource = listModel;
			idList.Columns.Add (" ", new CheckBoxCellView (selectedField) { Editable = true });
			idList.Columns.Add ("ID", idField);
			idList.Columns.Add ("File", fileField);
			foreach (var c in idList.Columns)
				c.CanResize = true;
			model.ApiSetUpdated += (sender, e) => {
				Application.InvokeAsync (() => {
					listModel.Clear ();
					foreach (var i in model.LoadedApiInfos)
						listModel.SetValues (listModel.AddRow (), selectedField, i.Selected, idField, i.FileId, fileField, i.ApiFullPath);
				});
			};
			vpaned.Panel1.Resize = true;
			vpaned.Panel1.Shrink = true;
			vpaned.Panel1.Content = idList;


			var tree = new TreeView () { ExpandVertical = true, ExpandHorizontal = true, HeightRequest = 300 };
			var nameField = new DataField<string> ();
			var sourceField = new DataField<string> ();
			var bindingField = new DataField<string> ();
			var treeModel = new TreeStore (nameField, sourceField, bindingField);
			tree.DataSource = treeModel;
			tree.Columns.Add ("Name", nameField);
			tree.Columns.Add ("Source", sourceField);
			tree.Columns.Add ("Binding", bindingField);
			foreach (var c in tree.Columns)
				c.CanResize = true;
			model.ApiSetUpdated += (sender, e) => {
				Application.InvokeAsync (() => {
					treeModel.Clear ();
					foreach (var pkg in model.Api.Packages.OrderBy (p => p.Name)) {
						Application.InvokeAsync (() => {
							var pkgNode = treeModel.AddNode ();
							pkgNode.SetValue (nameField, pkg.Name);
							foreach (var type in pkg.Types) {
								var typeNode = pkgNode.AddChild ();
								typeNode.SetValue (nameField, (type is JavaInterface ? "[IF]" : "[CLS]") + type.Name);
								typeNode.SetValue (sourceField, type.GetExtension<SourceIdentifier> ()?.SourceUri);
								typeNode.SetValue (bindingField, type.GetExtension<TypeDefinition> ()?.FullName);
								foreach (var fld in type.Members.OfType<JavaField> ()) {
									var fieldNode = typeNode.AddChild ();
									fieldNode.SetValue (nameField, fld.ToString ());
									fieldNode.SetValue (sourceField, fld.GetExtension<SourceIdentifier> ()?.SourceUri);
									fieldNode.SetValue (bindingField, fld.GetExtension<PropertyDefinition> ()?.Name ?? fld.GetExtension<FieldDefinition> ()?.Name);
									fieldNode.MoveToParent ();
								}
								foreach (var ctor in type.Members.OfType<JavaConstructor> ()) {
									var ctorNode = typeNode.AddChild ();
									ctorNode.SetValue (nameField, ctor.ToString ());
									ctorNode.SetValue (sourceField, ctor.GetExtension<SourceIdentifier> ()?.SourceUri);
									ctorNode.SetValue (bindingField, ctor.GetExtension<MethodDefinition> ()?.ToString ());
									ctorNode.MoveToParent ();
								}
								foreach (var method in type.Members.OfType<JavaMethod> ()) {
									var methodNode = typeNode.AddChild ();
									methodNode.SetValue (nameField, method.ToString ());
									methodNode.SetValue (sourceField, method.GetExtension<SourceIdentifier> ()?.SourceUri);
									methodNode.SetValue (bindingField, method.GetExtension<MethodDefinition> ()?.ToString ());
									methodNode.MoveToParent ();
								}
								typeNode.MoveToParent ();
							}
						});
					}
				});
			};

			vpaned.Panel2.Content = tree;

			vbox.PackStart (vpaned, true, true);

			Content = vbox;

			this.Closed += (sender, e) => Application.Exit ();
		}

		void CloseApplicationWindow ()
		{
			Close ();
		}

		void ClearJavaLibraries ()
		{
			model.ClearApi ();
		}

		void OpenJavaLibraries ()
		{
			string [] results = null;
			using (var dlg = new OpenFileDialog () {
				Title = "Select .jar file to load",
				Multiselect = true,
			}) {
				dlg.Filters.Add (new FileDialogFilter ("Any file", "*"));
				dlg.Filters.Add (new FileDialogFilter ("Jar files", "*.jar"));
				dlg.Filters.Add (new FileDialogFilter ("Aar files", "*.aar"));
				dlg.Filters.Add (new FileDialogFilter ("DLL files", "*.dll"));
				dlg.Filters.Add (new FileDialogFilter ("XML files", "*.xml"));

				results = dlg.Run () ? dlg.FileNames : null;
			}
			ThreadPool.QueueUserWorkItem ((state) => {
				if (results != null)
					model.LoadApiFromFiles (results);
			}, null);
		}
	}
}