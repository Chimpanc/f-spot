/*
 * Filters/JpegFilter.cs
 *
 * Author(s)
 *   Stephane Delcroix <stephane@delcroix.org>
 *
 * This is free software. See COPYING for details.
 *
 */
using System;

namespace FSpot.Filters {
	public class JpegFilter : IFilter {
		private uint quality = 95;
		public uint Quality {
			get { return quality; }
			set { quality = value; }
		}
		
		public JpegFilter (uint quality)
		{
			this.quality = quality;
		}

		public JpegFilter()
		{
		}
			
		public bool Convert (string source, string dest)
		{
			// FIXME this should copy metadata from the original
			// even when the source is not a jpeg
			ImageFile img = ImageFile.Create (source);
			if (img is JpegFile)
				return false;

			Exif.ExifData exif_data;
			try {
				exif_data = new Exif.ExifData (source);
			} catch (Exception) {
				exif_data = new Exif.ExifData();
			}

			PixbufUtils.SaveJpeg (img.Load(), dest, (int) quality, exif_data);

			return true;
		}
	}
}
