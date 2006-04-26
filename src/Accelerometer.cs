//
// Support for the ThinkPad accelerometer.
//

using System;
using System.IO;

namespace FSpot {

	public delegate void OrientationChangedHandler (object sender);

	public class Accelerometer {

		public static event OrientationChangedHandler OrientationChanged;

		public enum Orient {
			Normal,
			TiltClockwise,
			TiltCounterclockwise,
		}

		private static Orient current_orientation;

		public Orient CurrentOrientation {
			get {
				return current_orientation;
			}
		}

		public Accelerometer ()
		{
		}

		public static PixbufOrientation GetViewOrientation (PixbufOrientation po)
		{
			if (timer == 0)
				SetupAccelerometer ();
				
			if (current_orientation == Orient.TiltCounterclockwise)
				return PixbufUtils.Rotate90 (po);

			if (current_orientation == Orient.TiltClockwise)
				return PixbufUtils.Rotate270 (po);

			return po;
		}
		
		static uint timer = 0;

		public static void SetupAccelerometer ()
		{
			int x, y;

			// Call once to set a baseline value.
			// Hopefully the laptop is flat when this is
			// called.
			GetHDAPSCoords (out x, out y);

			timer = GLib.Timeout.Add (500, new GLib.TimeoutHandler (CheckOrientation));
		}

		private static bool CheckOrientation ()
		{
			Orient new_orient = GetScreenOrientation ();

			if (new_orient != current_orientation) {
				current_orientation = new_orient;

				if (OrientationChanged != null)
					OrientationChanged (null);

				Console.WriteLine ("Laptop orientation changed...");
			}

			return true;
		}

		public static Orient GetScreenOrientation ()
		{
			int x, y;

			GetHDAPSCoords (out x, out y);

			if (x > 100)
				return Orient.TiltClockwise;

			if (x < -100)
				return Orient.TiltCounterclockwise;

			return Orient.Normal;
		}

		static int base_x = -1000; // initial nonsense values
		static int base_y = -1000;

		private static void GetHDAPSCoords (out int x, out int y)
		{
			try {
				using (Stream file = File.OpenRead ("/sys/devices/platform/hdaps/position")) {
					StreamReader sr = new StreamReader (file);

					string s = sr.ReadLine ();
					string [] ss = s.Substring (1, s.Length - 2).Split (',');
					x = int.Parse (ss [0]);
					y = int.Parse (ss [1]);

					if (base_x == -1000)
						base_x = x;
					
					if (base_y == -1000)
						base_y = y;

					x -= base_x;
					y -= base_y;

					return;
				}
			} catch (Exception e) {
				x = 0;
				y = 0;
			}
		}
	}
}
