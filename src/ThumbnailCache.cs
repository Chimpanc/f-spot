using System;
using System.Collections;
using Gdk;

public class ThumbnailCache {

	// Types.

	class Thumbnail {
		// Path of the image on the disk.
		public string path;

		// The uncompressed thumbnail.
		public Pixbuf pixbuf;
	}


	// Private members and constants

	private const int DEFAULT_CACHE_SIZE = 200;

	private int max_count;
	private ArrayList pixbuf_mru;
	private Hashtable pixbuf_hash = new Hashtable ();

	static private ThumbnailCache default_thumbnail_cache = null;


	// Public API

	public ThumbnailCache (int max_count)
	{
		this.max_count = max_count;
		pixbuf_mru = new ArrayList (max_count);
	}

	static public ThumbnailCache Default {
		get {
			if (default_thumbnail_cache == null)
				default_thumbnail_cache = new ThumbnailCache (DEFAULT_CACHE_SIZE);

			return default_thumbnail_cache;
		}
	}

	public void AddThumbnail (string path, Pixbuf pixbuf)
	{
		Thumbnail thumbnail = new Thumbnail ();

		thumbnail.path = path;
		thumbnail.pixbuf = pixbuf;

		RemoveThumbnailForPath (path);

		pixbuf_mru.Insert (0, thumbnail);
		pixbuf_hash.Add (path, thumbnail);

		maybeExpunge ();
	}

	public Pixbuf GetThumbnailForPath (string path)
	{
		if (! pixbuf_hash.ContainsKey (path))
			return null;

		Thumbnail item = pixbuf_hash [path] as Thumbnail;

		pixbuf_mru.Remove (item);
		pixbuf_mru.Insert (0, item);

		return item.pixbuf;
	}

	public Pixbuf RemoveThumbnailForPath (string path)
	{
		if (! pixbuf_hash.ContainsKey (path))
			return null;

		Thumbnail item = pixbuf_hash [path] as Thumbnail;
		pixbuf_mru.Remove (item);
		pixbuf_hash.Remove (path);

		return item.pixbuf;
	}


	// Private utility methods.

	private void maybeExpunge ()
	{
		while (pixbuf_mru.Count > max_count) {
			Thumbnail thumbnail = pixbuf_mru [pixbuf_mru.Count - 1] as Thumbnail;

			pixbuf_hash.Remove (thumbnail.path);
			pixbuf_mru.RemoveAt (pixbuf_mru.Count - 1);
		}
	}
}
