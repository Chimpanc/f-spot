using Mono.Data.SqliteClient;
using Mono.Unix;
using Gtk;
using System;
using System.Collections;

namespace FSpot.Database {
	public static class Updater {
		private static ProgressDialog dialog;
		private static Hashtable updates = new Hashtable ();
		private static MetaItem db_version;
		private static Db db;


		public static Version LatestVersion {
			get {
				if (updates == null || updates.Count == 0)
					return new Version (0, 0);
				ArrayList keys = new ArrayList (updates.Keys);
				keys.Sort ();
				return keys[keys.Count - 1] as Version;
			}
		}
		
		static Updater () {
			// Update from version 0 to 1: Remove empty Other tags
			AddUpdate (new Version ("1"), delegate (SqliteConnection connection) {
				string other_id = SelectSingleString ("SELECT id FROM tags WHERE name = 'Other'");

				if (other_id == null)
					return;

				// Don't do anything if there are subtags
				string tag_count = SelectSingleString (
					String.Format ("SELECT COUNT(*) FROM tags WHERE category_id = {0}", other_id));
				
				if (tag_count == null || System.Int32.Parse (tag_count) != 0)
					return;
				
				// Don't do anything if there are photos tagged with this
				string photo_count = SelectSingleString (
					String.Format ("SELECT COUNT(*) FROM photo_tags WHERE tag_id = {0}", other_id));

				if (photo_count == null || System.Int32.Parse (photo_count) != 0)
					return;

				// Finally, we know that the Other tag exists and has no children, so remove it
				ExecuteNonQuery ("DELETE FROM tags WHERE name = 'Other'");
			});

			// Update from version 1 to 2: Restore Other tags that were removed leaving dangling child tags
			AddUpdate (new Version ("2"), delegate (SqliteConnection connection) {
				string tag_count = SelectSingleString ("SELECT COUNT(*) FROM tags WHERE category_id != 0 AND category_id NOT IN (SELECT id FROM tags)");

				// If there are no dangling tags, then don't do anything
				if (tag_count == null || System.Int32.Parse (tag_count) == 0)
					return;

				ExecuteScalar ("INSERT INTO tags (name, category_id, is_category, icon) VALUES ('Other', 0, 1, 'stock_icon:f-spot-other.png')");

				ExecuteNonQuery (String.Format (
					@"UPDATE tags SET category_id = {0} WHERE id IN 
					(SELECT id FROM tags WHERE category_id != 0 AND category_id 
					NOT IN (SELECT id FROM tags))",
					connection.LastInsertRowId));

				System.Console.WriteLine ("Other tag restored.  Sorry about that!");
			});
			
			// Update from version 2 to 3: ensure that Hidden is the only tag left which is a real tag (not category)
			AddUpdate (new Version ("3"), delegate (SqliteConnection connection) {
				ExecuteNonQuery ("UPDATE tags SET is_category = 1 WHERE name != 'Hidden'");
			});

//WARNING and FIXME: please add this update to the next major update of the db !!! 
//At that time, remove all the code (in FolderExport and InfoDisplay) related to 
//the handling of 'fspot:Folder' items. They're obsolete and replaced by fspot:FolderUri
//ExecuteNonQuery ("DELETE FROM exports WHERE export_type='fspot:Folder'");
//
//TODO: please consider fixing bgo 324425 on the next major update of the db.
			// Update from version 3 to 4
			//AddUpdate (new Version (4,0),delegate (SqliteConnection connection) {
			//	do update here
			//});

			
		}

		public static void Run (Db database)
		{
			db = database;
			db_version = db.Meta.DatabaseVersion;

			if (updates.Count == 0)
				return;

			Version current_version = new Version (db_version.Value);

			if (current_version == LatestVersion)
				return;
			else if (current_version > LatestVersion) {
				Console.WriteLine ("The existing database version is more recent than this version of F-Spot expects.");
				return;
			}

			Console.WriteLine ("Updating F-Spot Database");

			// Only create and show the dialog if one or more of the updates to be done is
			// marked as being slow
			bool slow = false;
			foreach (Version version in updates.Keys) {
				if (version > current_version && (updates[version] as Update).IsSlow)
					slow = true;
					break;
			}

			if (slow) {
				dialog = new ProgressDialog (Catalog.GetString ("Updating F-Spot Database"), ProgressDialog.CancelButtonType.None, 0, null);
				dialog.Message.Text = Catalog.GetString ("Please wait while your F-Spot gallery's database is updated. This may take some time.");
				dialog.Bar.Fraction = 0.0;
				dialog.Modal = false;
				dialog.SkipTaskbarHint = true;
				dialog.WindowPosition = WindowPosition.Center;
				dialog.ShowAll ();
				dialog.Present ();
				dialog.QueueDraw ();
			}

			db.BeginTransaction ();
			try {
				ArrayList keys = new ArrayList (updates.Keys);
				keys.Sort ();
				foreach (Version version in keys) {
					if (version <= current_version)
						continue;

					Pulse ();
					(updates[version] as Update).Execute (db, db_version);
				}

				db.CommitTransaction ();
			} catch (Exception e) {
				Console.WriteLine ("Rolling back database changes because of Exception");
				// There was an error, roll back the database
				db.RollbackTransaction ();

				// Pass the exception on, this is fatal
				throw e;
			}
			
			if (dialog != null)
				dialog.Destroy ();
			
			if (new Version(db_version.Value) == LatestVersion)
				Console.WriteLine ("Database updates completed successfully.");
		}
		
		private static void AddUpdate (Version version, UpdateCode code)
		{
			AddUpdate (version, code, false);
		}
		
		private static void AddUpdate (Version version, UpdateCode code, bool is_slow)
		{
			updates[version] = new Update (version, code, is_slow);
		}

		public static void Pulse ()
		{
			if (dialog != null) {
				dialog.Bar.Pulse ();
				dialog.ShowAll ();
			}
		}

		private static void ExecuteNonQuery (string statement)
		{
			SqliteCommand command = new SqliteCommand ();
			command.Connection = db.Connection;

			command.CommandText = statement;

			command.ExecuteNonQuery ();
			command.Dispose ();
		}
		
		private static void ExecuteScalar (string statement)
		{
			SqliteCommand command = new SqliteCommand ();
			command.Connection = db.Connection;

			command.CommandText = statement;

			command.ExecuteScalar ();
			command.Dispose ();
		}
		
		private static string SelectSingleString (string statement)
		{
			string result = null;

			try {
				SqliteCommand command = new SqliteCommand ();
				command.Connection = db.Connection;

				command.CommandText = statement;

				SqliteDataReader reader = command.ExecuteReader ();
				
				if (reader.Read ())
					result = reader [0].ToString ();

				reader.Close ();
				command.Dispose ();
			} catch (Exception) {}

			return result;
		}

		private static string MoveTableToTemp (string table_name)
		{
			string temp_name = table_name + "_temp";
			
			// Get the table definition for the table we are copying
			string sql = SelectSingleString (String.Format ("SELECT sql FROM sqlite_master WHERE tbl_name = '{0}' ORDER BY type DESC", table_name));

			// Change the SQL to create the temp table
			ExecuteNonQuery (sql.Replace ("CREATE TABLE " + table_name, "CREATE TEMPORARY TABLE " + temp_name));

			// Copy the data
			ExecuteScalar (String.Format ("INSERT INTO {0} SELECT * FROM {1}", temp_name, table_name));
				
			// Delete the original table
			ExecuteNonQuery ("DROP TABLE " + table_name);

			return temp_name;
		}

		private delegate void UpdateCode (SqliteConnection connection);

		private class Update {
			public Version Version;
			private UpdateCode code;
			public bool IsSlow = false;
			
			public Update (Version to_version, UpdateCode code, bool slow)
			{
				this.Version = to_version;
				this.code = code;
				IsSlow = slow;
			}

			public Update (Version to_version, UpdateCode code)
			{
				this.Version = to_version;
				this.code = code;
			}

			public void Execute (Db db, MetaItem db_version)
			{
				code (db.Connection);
				
				Console.WriteLine ("Updated database from version {0} to {1}",
						db_version.Value,
						Version.ToString ());


				db_version.Value = Version.ToString ();
				db.Meta.Commit (db_version);
			}
		}

		public class Version : IComparable {
			int maj = 0;
			int min = 0;

			public Version (int maj, int min)
			{
				this.maj = maj;
				this.min = min;
			}

			public Version (string version)
			{
				string [] parts = version.Split (new char [] {'.'}, 2);
				try {
					this.maj = Convert.ToInt32 (parts [0]);
				}
				catch (Exception) {
					this.maj = 0;
				}
				try {
					this.min = Convert.ToInt32 (parts [1]);
				}
				catch (Exception) {
					this.min = 0;
				}
			}

			//IComparable
			public int CompareTo (object obj) {
				if (this.GetType () == obj.GetType ())
					return Compare (this, (Version)obj);
				else
					throw new Exception ("Object must be of type Version");
			}

			public int CompareTo (Version version)
			{
				return Compare (this, version);
			}
	
			public static int Compare (Version v1, Version v2)
			{
				if (v1.maj == v2.maj)
					return v1.min.CompareTo (v2.min);
				return v1.maj.CompareTo (v2.maj);
			}

			public override string ToString ()
			{
				return maj + "." + min;
			}

			public override bool Equals (object obj)
			{
				return obj is Version && this == (Version)obj;
			}

			public override int GetHashCode ()
			{
				return maj ^ min;
			}

			public static bool operator == (Version v1, Version v2)
			{
				return v1.maj == v2.maj && v1.min == v2.min;
			}

			public static bool operator != (Version v1, Version v2)
			{
				return !(v1 == v2);
			}

			public static bool operator < (Version v1, Version v2)
			{
				return Compare (v1,v2) < 0;
			}

			public static bool operator > (Version v1, Version v2)
			{
				return Compare (v1,v2) > 0;
			}

			public static bool operator <= (Version v1, Version v2)
			{
				return !(v1 > v2);
			}

			public static bool operator >= (Version v1, Version v2)
			{
				return !(v1 < v2);
			}
		}
	} 
}
