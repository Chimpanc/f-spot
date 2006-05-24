/*
 * PhotoPopup.cs
 *
 * Authors:
 *   Larry Ewing <lewing@novell.com>
 *   Vladimir Vukicevic <vladimir@pobox.com>
 *   Miguel de Icaza <miguel@ximian.com>
 *
 * Copyright (C) 2002 Vladimir Vukicevic
 * Copyright (C) 2003 Novell, Inc.
 *
 */

using System;
using Gtk;
using Gdk;
using Mono.Posix;

public class PhotoPopup : Gtk.Menu {
	Widget creator;

	public Widget Creator {
		get {
			return creator;
		}
	}

	public void Activate (Widget toplevel)
	{
		Activate (toplevel, null);
	}

	public void Activate (Widget toplevel, Gdk.EventButton eb) 
	{
		// FIXME this is a hack to handle the --view case for the time being.
		creator = toplevel;

		if (MainWindow.Toplevel == null)
			return;

		int count = MainWindow.Toplevel.SelectedIds ().Length;
		
		Gtk.Menu popup_menu = this;
		bool have_selection = count > 0;
		
		GtkUtil.MakeMenuItem (popup_menu, Mono.Posix.Catalog.GetString ("Copy Photo Location"), 
				      delegate { MainWindow.Toplevel.HandleCopyLocation (creator, null); }, have_selection);
		
		GtkUtil.MakeMenuSeparator (popup_menu);

		GtkUtil.MakeMenuItem (popup_menu, "f-spot-rotate-270",
				      delegate { MainWindow.Toplevel.HandleRotate270Command(creator, null); }, have_selection);
		GtkUtil.MakeMenuItem (popup_menu, "f-spot-rotate-90", 
				      delegate { MainWindow.Toplevel.HandleRotate90Command (creator, null); }, have_selection);

		GtkUtil.MakeMenuSeparator (popup_menu);

		OpenWithMenu owm = OpenWithMenu.AppendMenuTo (popup_menu, MainWindow.Toplevel.SelectedMimeTypes);
		owm.IgnoreApp = "f-spot";
		owm.ApplicationActivated += MainWindow.Toplevel.HandleOpenWith;

		GtkUtil.MakeMenuItem (popup_menu, Mono.Posix.Catalog.GetString ("Remove From Catalog"), 
				      delegate { MainWindow.Toplevel.HandleRemoveCommand (creator, null); }, have_selection);
		GtkUtil.MakeMenuItem (popup_menu, Mono.Posix.Catalog.GetString ("Delete From Drive"),
				      delegate { MainWindow.Toplevel.HandleDeleteCommand (creator, null); }, have_selection);

		GtkUtil.MakeMenuSeparator (popup_menu);
		
		//
		// FIXME TagMenu is ugly.
		//
		MenuItem attach_item = new MenuItem (Mono.Posix.Catalog.GetString ("Attach Tag"));
		TagMenu attach_menu = new TagMenu (attach_item, MainWindow.Toplevel.Database.Tags);
		attach_menu.NewTagHandler = MainWindow.Toplevel.HandleCreateTagAndAttach;
		attach_menu.TagSelected += MainWindow.Toplevel.HandleAttachTagMenuSelected;
		attach_item.ShowAll ();
		popup_menu.Append (attach_item);

		//
		// FIXME finish the IPhotoSelection stuff and move the activate handler into the class
		// this current method is way too complicated.
		//
		MenuItem remove_item = new MenuItem (Mono.Posix.Catalog.GetString ("Remove Tag"));
		PhotoTagMenu remove_menu = new PhotoTagMenu ();
		remove_menu.TagSelected += MainWindow.Toplevel.HandleRemoveTagMenuSelected;
		remove_item.Submenu = remove_menu;
		remove_item.Activated += MainWindow.Toplevel.HandleTagMenuActivate;
		remove_item.ShowAll ();
		popup_menu.Append (remove_item);

		if (eb != null)
			popup_menu.Popup (null, null, null, eb.Button, eb.Time);
		else 
			popup_menu.Popup (null, null, null, 0, Gtk.Global.CurrentEventTime);
	}

	
}
