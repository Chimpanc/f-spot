/*
 * MenuNode.cs
 *
 * Author(s):
 * 	Stephane Delcroix  <stephane@delcroix.org>
 *
 * This is free software. See COPYING for details
 *
 */

using System;
using Mono.Addins;
using Mono.Unix;

namespace FSpot.Extensions
{
	[ExtensionNode ("Menu")]
	[ExtensionNodeChild (typeof (MenuItemNode))]
	[ExtensionNodeChild (typeof (ExportMenuItemNode))]
	[ExtensionNodeChild (typeof (CommandMenuItemNode))]
	[ExtensionNodeChild (typeof (MenuSeparatorNode))]
	[ExtensionNodeChild (typeof (SubmenuNode))]
	[ExtensionNodeChild (typeof (MenuGeneratorNode))]
	public class SubmenuNode : MenuNode
	{
		public override Gtk.MenuItem GetMenuItem ()
		{
			Gtk.MenuItem item = base.GetMenuItem ();;

			Gtk.Menu submenu = new Gtk.Menu ();

			foreach (MenuNode node in ChildNodes)
				submenu.Insert (node.GetMenuItem (), -1);

			item.Submenu = submenu;
			return item;
		}
	}

	[ExtensionNode ("MenuGenerator")]
	public class MenuGeneratorNode : MenuNode
	{
		[NodeAttribute ("generator_type", true)]
		string command_type;

		private IMenuGenerator menu_generator;

		public override Gtk.MenuItem GetMenuItem ()
		{
			Gtk.MenuItem item = base.GetMenuItem ();
			menu_generator = (IMenuGenerator) Addin.CreateInstance (command_type); 
			item.Submenu = menu_generator.GetMenu ();
			item.Activated += menu_generator.OnActivated;
			return item;
		}
	}

	[ExtensionNode ("MenuItem")]
	public class MenuItemNode : MenuNode
	{
		public override Gtk.MenuItem GetMenuItem ()
		{
			Gtk.MenuItem item = base.GetMenuItem ();
			item.Activated += OnActivated;
			return item;
		}

		protected virtual void OnActivated (object o, EventArgs e)
		{
			Console.WriteLine ("Item {0} activated", Id);
		}
	}

	[ExtensionNode ("MenuSeparator")]
	public class MenuSeparatorNode : MenuNode
	{
		public override Gtk.MenuItem GetMenuItem ()
		{
			return new Gtk.SeparatorMenuItem ();
		}
	}

	public abstract class MenuNode : ExtensionNode
	{
		[NodeAttribute]
		string _label;

		[NodeAttribute]
		string icon;

		public virtual Gtk.MenuItem GetMenuItem ()
		{
			Gtk.MenuItem item;
			if (icon == null)
				item = new Gtk.MenuItem (_label != null ? Catalog.GetString (_label) : Id);
			else {
				item = new Gtk.ImageMenuItem (_label != null ? Catalog.GetString (_label) : Id);
				(item as Gtk.ImageMenuItem).Image = Gtk.Image.NewFromIconName (icon, Gtk.IconSize.Menu);
			}
			return item;
		}
	}
}
