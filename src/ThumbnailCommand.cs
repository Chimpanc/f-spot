using System;
using Gtk;

public class ThumbnailCommand {
	
	private Gtk.Window parent_window;

	public ThumbnailCommand (Gtk.Window parent_window)
	{
		this.parent_window = parent_window;
	}

	public bool Execute (Photo [] photos)
	{
		ProgressDialog progress_dialog = null;

		if (photos.Length > 1) {
			progress_dialog = new ProgressDialog ("Updating Thumbnails",
							      ProgressDialog.CancelButtonType.Stop,
							      photos.Length, parent_window);
		}

		int count = 0;
		foreach (Photo p in photos) {
			if (progress_dialog != null
			    && progress_dialog.Update (String.Format ("Updating picture \"{0}\"", p.Name)))
				break;

			foreach (uint version_id in p.VersionIds) {
				FSpot.ThumbnailGenerator.Create (p.GetVersionPath (version_id)).Dispose ();
			}
			
			count++;
		}

		if (progress_dialog != null)
			progress_dialog.Destroy ();

		return true;
	}
}
