using System;
using System.Collections.Generic;
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
			Width = 600;
			Height = 500;
			var vbox = new VBox ();
			var menu = new Menu ();
			var commands = new List<KeyValuePair<string,List<KeyValuePair<Command, Action>>>> ();
			var fileCommands = new List<KeyValuePair<Command, Action>> ();
			fileCommands.Add (new KeyValuePair<Command, Action> (new Command ("_Open"), () => OpenJavaLibraries ()));
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
			this.MainMenu = menu;

			var tree = new TreeView ();
			var nameField = new DataField<string> ();
			var sourceField = new DataField<string> ();
			var treeModel = new TreeStore (nameField, sourceField);
			tree.DataSource = treeModel;
			tree.Columns.Add ("Name", nameField);
			tree.Columns.Add ("Source", sourceField);
			model.ApiSetUpdated += (sender, e) => {
				//treeModel.Clear ();
				foreach (var pkg in model.Api.Packages.OrderBy (p => p.Name)) {
					Application.InvokeAsync (() => {
						var pkgNode = treeModel.AddNode ();
						pkgNode.SetValue (nameField, pkg.Name);
						foreach (var type in pkg.Types) {
							var typeNode = pkgNode.AddChild ();
							typeNode.SetValue (nameField, (type is JavaInterface ? "[IF]" : "[CLS]") + type.Name);
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
			};

			vbox.PackStart (tree, true, true);
			Content = vbox;
		}

		void CloseApplicationWindow ()
		{
			Close ();
		}

		Dictionary<string, string> fileIds = new Dictionary<string, string> ();

		string GetFileId (string file)
		{
			string id;
			if (!fileIds.TryGetValue (file, out id)) {
				id = fileIds.Count.ToString ();
				fileIds [file] = id;
			}
			return id;
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

				dlg.Run ();
				results = dlg.FileNames;
			}
			ThreadPool.QueueUserWorkItem ((state) => {
				if (results != null)
					model.LoadFiles (results, f => GetFileId (f));
			}, null);
		}
	}
}