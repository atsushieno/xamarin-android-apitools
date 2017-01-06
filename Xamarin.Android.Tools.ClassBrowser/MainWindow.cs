using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xamarin.Android.Tools.ApiXmlAdjuster;
using Xwt;

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

			var quickLoads = new List<KeyValuePair<Command, Action>> ();
			Func<string, KeyValuePair<Command, Action>> gen = (s) =>
				new KeyValuePair<Command, Action> (new Command (Path.GetFileName (Path.GetDirectoryName (s)) + '/' + Path.GetFileName (s)), () => model.LoadApiFromFiles (new string [] { s }));
			foreach (var s in model.PredefinedLibraries.AndroidLibraries)
				quickLoads.Add (gen (s));
			foreach (var s in model.PredefinedLibraries.XamarinAndroidLibraries)
				quickLoads.Add (gen (s));
			commands.Add (new KeyValuePair<string, List<KeyValuePair<Command, Action>>> ("_Quick Load", quickLoads));

			foreach (var cl in commands) {
				var submenu = new Menu ();
				foreach (var item in cl.Value) {
					var mi = new MenuItem (item.Key);
					mi.Clicked += (sender, e) => item.Value ();
					submenu.Items.Add (mi);
				}
				menu.Items.Add (new MenuItem () { Label = cl.Key, SubMenu = submenu });
			}
			this.MainMenu = menu;

			var vpaned = new VPaned ();

			var idList = new ListView () { ExpandHorizontal = true, HeightRequest = 50 };
			var selectedField = new DataField<bool> ();
			var idField = new DataField<string> ();
			var fileField = new DataField<string> ();
			var listModel = new ListStore (selectedField, idField, fileField);
			idList.DataSource = listModel;
			idList.Columns.Add (" ", selectedField);
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
			var treeModel = new TreeStore (nameField, sourceField);
			tree.DataSource = treeModel;
			tree.Columns.Add ("Name", nameField);
			tree.Columns.Add ("Source", sourceField);
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
								foreach (var fld in type.Members.OfType<JavaField> ()) {
									var fieldNode = typeNode.AddChild ();
									fieldNode.SetValue (nameField, "[F]" + fld.Name);
									fieldNode.SetValue (sourceField, fld.GetExtension<SourceIdentifier> ()?.SourceUri);
									fieldNode.MoveToParent ();
								}
								foreach (var ctor in type.Members.OfType<JavaConstructor> ()) {
									var ctorNode = typeNode.AddChild ();
									ctorNode.SetValue (nameField, "[C]" + ctor.ToString ());
									ctorNode.SetValue (sourceField, ctor.GetExtension<SourceIdentifier> ()?.SourceUri);
									ctorNode.MoveToParent ();
								}
								foreach (var method in type.Members.OfType<JavaMethod> ()) {
									var methodNode = typeNode.AddChild ();
									methodNode.SetValue (nameField, "[M]" + method.ToString ());
									methodNode.SetValue (sourceField, method.GetExtension<SourceIdentifier> ()?.SourceUri);
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