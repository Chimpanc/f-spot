using Gtk;
using Gnome;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections;
using Mono.Unix;
using Mono.GetOptions;

namespace FSpot 
{
	public class FSpotOptions : Options
	{
		//--import, -i
		[Option ("import from the given uri", 'i', "import")]
		public string import;

		//--view, -v
		[Option ("view file(s) or directory(ies)", 'v', "view")]
		public bool view;

		//--basedir, -b
		[Option ("path to the photo database folder", 'b', "basedir")]
		public string basedir;

		//--photodir, -p
		[Option ("default import folder", 'p', "photodir")]
		public string photodir;

		//--shutdown
		[Option ("shutdown a running f-spot instance", "shutdown")]
		public bool shutdown;

		//--slideshow
		[Option ("display a slideshow", "slideshow")]
		public bool slideshow;

		//--debug, -g
		[Option ("run f-spot with mono on debug mode", 'g', "debug")]
		public bool debug;

		public FSpotOptions ()
		{
			base.ParsingMode = OptionsParsingMode.Both;
		}

		public bool Validate ()
		{
			if ( (import != null && (view || shutdown || slideshow)) || 
			     (view && (shutdown || slideshow)) ||
			     (shutdown && slideshow) ) {
				Console.WriteLine ("Can't mix --import, --view, --shutdown or --slideshow");
				return false;
			}
			if (view && RemainingArguments.Length == 0) {
				Console.WriteLine ("Need to specify at least one uri for --view");
				return false;
			}

			foreach (string s in RemainingArguments)
				if (s.StartsWith("-") && s != "--uninstalled") {
					Console.WriteLine ("Unknown option {0}", s);
					return false;
				}
			return true;
		}
	}

public class Driver {
	public static void Main (string [] args)
	{
		bool empty = false;
		Program program = null;
		ICore control = null;

		SetProcessName (Defines.PACKAGE);
		
		NDesk.DBus.BusG.Init();
		try {
			FSpotOptions options = new FSpotOptions ();
			options.ProcessArgs (args);

			if (!options.Validate ()) {
				options.DoHelp ();
				return;
			}

			if (options.basedir != null) {
				FSpot.Global.BaseDirectory = options.basedir;
				System.Console.WriteLine("BaseDirectory is now {0}", FSpot.Global.BaseDirectory);
			} 

			if (options.photodir != null) {
				FSpot.Global.PhotoDirectory = System.IO.Path.GetFullPath(options.photodir);
				System.Console.WriteLine("PhotoDirectory is now {0}", FSpot.Global.PhotoDirectory);
			}

			if (options.slideshow) {
				Catalog.Init ("f-spot", Defines.LOCALE_DIR);
					
				program = new Program (Defines.PACKAGE, 
						       Defines.VERSION, 
					       Modules.UI, args);
				Core core = new Core ();
				core.ShowSlides (null);
				program.Run ();
				System.Console.WriteLine ("done");
				return;
			}

			/* 
			 * FIXME we need to inialize gobject before making the dbus calls, we'll go 
			 * ahead and do it like this for now.
			 */ 
			program = new Program (Defines.PACKAGE, 
					       Defines.VERSION, 
					       Modules.UI, args);		
			
			bool create = true;
			while (control == null) {
				try {
					control = Core.FindInstance ();
					System.Console.WriteLine ("Found active FSpot server: {0}", control);
					program = null;
				} catch (System.Exception) { 
					if (!options.shutdown)
						System.Console.WriteLine ("Starting new FSpot server");
				}
				
				Core core = null;
				try {
					if (control == null && create) {
						create = false;
						Gnome.Vfs.Vfs.Initialize ();
						StockIcons.Initialize ();
						
						Catalog.Init ("f-spot", Defines.LOCALE_DIR);
						Gtk.Window.DefaultIconList = new Gdk.Pixbuf [] {PixbufUtils.LoadFromAssembly ("f-spot-16.png"),
												PixbufUtils.LoadFromAssembly ("f-spot-22.png"),
												PixbufUtils.LoadFromAssembly ("f-spot-32.png"),
												PixbufUtils.LoadFromAssembly ("f-spot-48.svg")};
						core = new Core ();
						core.RegisterServer ();
						
						// FIXME: Error checking is non-existant here...
						
						empty = Core.Database.Empty;
						control = core;
					}
				} catch (System.Exception e) {
					System.Console.WriteLine ("XXXXX\n{0}\nXXXXX", e);
					control = null;

					if (core != null)
						core.UnregisterServer ();
				}
			}
			
			
			UriList list = new UriList ();

			if (options.shutdown) {
				try {
					control.Shutdown ();
				} catch (System.Exception) {
					// trap errors
				}
				System.Environment.Exit (0);
			}

			if (options.import != null) {
				control.Import (options.import);
			}

			if (options.view) {
				foreach (string s in options.RemainingArguments)
					list.AddUnknown (s);
				if (list.Count > 0)
					control.View (list.ToString ());
			}
			
			if (empty && options.import == null)
				control.Import (null);
			
			if (options.import != null || !options.view) {
				control.Organize ();
				Gdk.Global.NotifyStartupComplete ();
			}			

			if (program != null)
				program.Run ();
			
			System.Console.WriteLine ("exiting");
		} catch (System.Exception e) {
			Console.Error.WriteLine(e);
			Gtk.Application.Init();
			ExceptionDialog dlg = new ExceptionDialog(e);
			dlg.Run();
			dlg.Destroy();
			System.Environment.Exit(1);
		}
	}

	[DllImport("libc")]
	private static extern int prctl(int option, string name, ulong arg3,
					ulong arg4, ulong arg5);
	
	public static void SetProcessName(string name)
	{
		try {
			if(prctl(15 /* PR_SET_NAME */, name,
				 0, 0, 0) != 0) {
				Console.WriteLine ("Error setting process name: " +
						   Mono.Unix.Native.Stdlib.GetLastError());
			}
		} catch (DllNotFoundException) {
			/* noop */
		} catch (EntryPointNotFoundException) {
		    	/* noop */
		}
	}
}

}
