//
// IconView.cs:
//
// Authors:
//    Ettore Perazzoli
//    Larry Ewing <lewing@novell.com>
//
// (C) 2003 Novell, Inc.
//
using Gtk;
using Gdk;
using Gnome;
using GtkSharp;
using System;
using System.Reflection;
using System.Collections;
using System.IO;

public class IconView : Gtk.Layout {

	// Public properties.

	/* Width of the thumbnails. */
	private int thumbnail_width = 128;
	public int ThumbnailWidth {
		get {
			return thumbnail_width;
		}

		set {
			if (thumbnail_width != value) {
				thumbnail_width = value;
				QueueResize ();
			}
		}
	}

	private double thumbnail_ratio = 4.0 / 3.0;
	public double ThumbnailRatio {
		get {
			return thumbnail_ratio;
		}
		set {
			thumbnail_ratio = value;
			QueueResize ();
		}
	}

	public int ThumbnailHeight {
		get {
			return (int) Math.Round ((double) thumbnail_width / ThumbnailRatio);
		}
	}

	private bool display_tags = true;
	public bool DisplayTags {
		get {
			return display_tags;
		}

		set {
			display_tags = value;
			QueueResize ();
		}
	}

	private bool display_dates = true;
	public bool DisplayDates {
		get {
			return display_dates;
		}

		set {
			display_dates = value;
			QueueResize ();
		}
	}

	// Size of the frame around the thumbnail.
	private const int CELL_BORDER_WIDTH = 10;

	// Border around the scrolled area.
	private const int BORDER_SIZE = 6;

	// Thickness of the outline used to indicate selected items. 
	private const int SELECTION_THICKNESS = 5;

	// Size of the tag icon in the view.
	private const int TAG_ICON_SIZE = 16;

	// Horizontal spacing between the tag icons
	private const int TAG_ICON_HSPACING = 2;

	// Vertical spacing between the thumbnail and the row of tag icons.
	private const int TAG_ICON_VSPACING = 6;

	// The loader.
	private PixbufLoader pixbuf_loader;

	// Various other layout values.
	private int cells_per_row;
	private int cell_width;
	private int cell_height;

	// The first pixel line that is currently on the screen (i.e. in the current
	// scroll region).  Used to compute the area that went offscreen in the "changed"
	// signal handler for the vertical GtkAdjustment.
	private int y_offset;

	// Hash of all the order number of the items that are selected.
	private Hashtable selected_cells;

	// Drag and drop bookkeeping. 
	private int click_x, click_y;

	// Focus Handling
	private int real_focus_cell;
	public int FocusCell {
		set {
			if (value != real_focus_cell) {
				InvalidateCell (value);
				InvalidateCell (real_focus_cell);
				real_focus_cell = value;
			}
		}
		get {
			return real_focus_cell;
		}
	}
	// Number of consecutive GDK_BUTTON_PRESS on the same cell, to
	// distinguish the GDK_2BUTTON_PRESS events that we actually care
	// about.
	private int click_count;

	// The pixbuf we use when we can't load a thumbnail.
	static Pixbuf error_pixbuf;

	// Public events.
	public delegate void DoubleClickedHandler (IconView view, int clicked_item);
	public event DoubleClickedHandler DoubleClicked;

	public delegate void SelectionChangedHandler (IconView view);
	public event SelectionChangedHandler SelectionChanged;


	// Public API.

	public IconView () : base (null, null)
	{
		pixbuf_loader = new PixbufLoader ();
		pixbuf_loader.OnPixbufLoaded += HandlePixbufLoaded;

		selected_cells = new Hashtable ();

		ScrollAdjustmentsSet += new ScrollAdjustmentsSetHandler (HandleScrollAdjustmentsSet);
		
		ButtonPressEvent += new ButtonPressEventHandler (HandleButtonPressEvent);
		ButtonReleaseEvent += new ButtonReleaseEventHandler (HandleButtonReleaseEvent);
		KeyPressEvent += new KeyPressEventHandler (HandleKeyPressEvent);

		DestroyEvent += new DestroyEventHandler (HandleDestroyEvent);

		AddEvents ((int) EventMask.KeyPressMask
			   | (int) EventMask.KeyReleaseMask 
			   | (int) EventMask.PointerMotionMask);
		
		CanFocus = true;

		FSpot.Global.ModifyColors (this);
	}
	
	public IconView (FSpot.IBrowsableCollection collection) : this () 
	{
		this.collection = collection;
	}
	

	protected IconView (IntPtr raw) : base (raw) {}

	//
	// IPhotoSelection
	//

	protected FSpot.IBrowsableCollection collection;
	public FSpot.IBrowsableCollection Collection {
		get {
			return collection;
		}
	}

	public int [] SelectedIdxs {
		get {
			int [] selection = new int [selected_cells.Count];

			int i = 0;
			foreach (int cell in selected_cells.Keys)
				selection [i ++] = cell;

			Array.Sort (selection);
			return selection;
		}
	}

	public int SelectedIdxCount 
	{
		get {
			return selected_cells.Count;
		}
	}
	
	public bool IdxIsSelected (int num)
	{
		return CellIsSelected (num);
	}

	public int CurrentIdx {
		get {
			if (selected_cells.Count == 1 && IdxIsSelected (FocusCell))
				return FocusCell;
			else 
				return -1;
		}
	}

	// Updating.
	public void UpdateThumbnail (int thumbnail_num)
	{
		FSpot.IBrowsableItem photo = collection.Items [thumbnail_num];
		string thumbnail_path = Thumbnail.PathForUri (photo.DefaultVersionUri.ToString (), ThumbnailSize.Large);

		//Console.WriteLine ("remove {0}", thumbnail_path);
		ThumbnailCache.Default.RemoveThumbnailForPath (thumbnail_path);
		InvalidateCell (thumbnail_num);
	}


	// Cell Geometry

	public int CellAtPosition (int x, int y)
	{
		if (collection == null)
			return -1;

		if (x < BORDER_SIZE || x >= BORDER_SIZE + cells_per_row * cell_width)
			return -1;
		if (y < BORDER_SIZE || y >= BORDER_SIZE + (collection.Items.Length / cells_per_row + 1) * cell_height)
			return -1;

		int column = (int) ((x - BORDER_SIZE) / cell_width);
		int row = (int) ((y - BORDER_SIZE) / cell_height);
		int cell_num = column + row * cells_per_row;

		if (cell_num < collection.Items.Length)
			return (int) cell_num;
		else
			return -1;
	}

	public void GetCellCenter (int cell_num, out int x, out int y)
	{
		if (cell_num == -1) {
			x = -1;
			y = -1;
		}

		x = BORDER_SIZE + (cell_num % cells_per_row) * cell_width + cell_width / 2;
		y = BORDER_SIZE + (cell_num / cells_per_row) * cell_height + cell_height / 2;
	}

	public void GetCellSize (int cell_num, out int w, out int h)
	{
		// Trivial for now.
		w = cell_width;
		h = cell_height;
	}


	// Private utility methods.
	public void UnselectAllCells ()
	{
		if (selected_cells.Count == 0)
			return;

		if (selected_cells.Count < 100) {
			foreach (int cell in selected_cells.Keys) {
				InvalidateCell (cell);
			}
		} else {
			QueueDraw ();
		}
		selected_cells.Clear ();		


		if (SelectionChanged != null)
			SelectionChanged (this);
	}

	public bool CellIsSelected (int cell_num)
	{
		return selected_cells.ContainsKey (cell_num);
	}

	private void SelectCellNoNotify (int cell_num)
	{
		if (CellIsSelected (cell_num))
			return;

		selected_cells.Add (cell_num, cell_num);

		InvalidateCell (cell_num);
	}

	private void SelectCell (int cell_num)
	{
		SelectCellNoNotify (cell_num);

		if (SelectionChanged != null)
			SelectionChanged (this);
	}

	public void SelectAllCells ()
	{
		SelectCellRange (0, collection.Items.Length - 1);
	}

	private void SelectCellRange (int start, int end)
	{
		if (start == -1 || end == -1)
			return;

		int current = Math.Min (start, end);
		int final = Math.Max (start, end);				
	
		while (current <= final) {
			SelectCellNoNotify (current);
			current++;
		}

		if (SelectionChanged != null)
			SelectionChanged (this);
	}

	private void UnselectCell (int cell_num)
	{
		if (! CellIsSelected (cell_num))
			return;

		selected_cells.Remove (cell_num);

		InvalidateCell (cell_num);

		if (SelectionChanged != null)
			SelectionChanged (this);
	}

	private void ToggleCell (int cell_num)
	{
		if (CellIsSelected (cell_num))
			UnselectCell (cell_num);
		else
			SelectCell (cell_num);
	}


	// Layout and drawing.
	
	// FIXME I can't find a c# wrapper for the C PANGO_PIXELS () macro
	// So this Function is for that.
	static int PangoPixels (int val)
	{
		return val >= 0 ? (val + 1024 / 2) / 1024 :
			(val - 1024 / 2) / 1024;
	}
	
	private void UpdateLayout ()
	{
		int available_width = Allocation.Width - 2 * BORDER_SIZE;

		cell_width = ThumbnailWidth + 2 * CELL_BORDER_WIDTH;
		cell_height = ThumbnailHeight + 2 * CELL_BORDER_WIDTH;

		if (DisplayTags)
			cell_height += TAG_ICON_SIZE + TAG_ICON_VSPACING;
		
		if (DisplayDates && this.Style != null) {
			Pango.FontMetrics metrics = this.PangoContext.GetMetrics (this.Style.FontDescription, 
										  Pango.Language.FromString ("en_US"));
			cell_height += PangoPixels (metrics.Ascent + metrics.Descent);
		}

		cells_per_row = Math.Max ((int) (available_width / cell_width), 1);

		int num_thumbnails;
		if (collection != null)
			num_thumbnails = collection.Items.Length;
		else
			num_thumbnails = 0;

		int num_rows = num_thumbnails / cells_per_row;
		if (num_thumbnails % cells_per_row != 0)
			num_rows ++;

		SetSize ((uint) Allocation.Width, (uint) (num_rows * cell_height + 2 * BORDER_SIZE));

		Vadjustment.StepIncrement = cell_height;
		Vadjustment.Change ();
	}

	Gdk.Rectangle Expand (Gdk.Rectangle src, int width)
	{
		src.X -= width;
		src.Y -= width;
		src.Width += width * 2;
		src.Height += width * 2;
		return src;
	}

	int ThrobExpansion (int cell, bool selected)
	{
		int expansion = 0;
		if (cell == throb_cell) {
			double t = throb_state / (double) (throb_state_max - 1);
			double s;
			if (selected)
				s = Math.Cos (-2 * Math.PI * t);
			else
				s = 1 - Math.Cos (-2 * Math.PI * t);
			
			expansion = (int) (SELECTION_THICKNESS * s);
		} else if (selected) {
			expansion = SELECTION_THICKNESS;
		}

		return expansion;
	}

	System.Collections.Hashtable date_layouts = new Hashtable ();
	// FIXME Cache the GCs?
	private void DrawCell (int thumbnail_num, Gdk.Rectangle area)
	{
		Gdk.Rectangle bounds = CellBounds (thumbnail_num);
		
		if (!bounds.Intersect (area, out area))
			return;
		
		FSpot.IBrowsableItem photo = collection.Items [thumbnail_num];
		string thumbnail_path = Thumbnail.PathForUri (photo.DefaultVersionUri.ToString (), 
							      ThumbnailSize.Large);

		Pixbuf thumbnail = ThumbnailCache.Default.GetThumbnailForPath (thumbnail_path);			
		if (thumbnail == null) {
			// FIXME instead of making a request to load at a particular size here we
			// request to load at the the full size because Gdk.Pixbuf loses the Option
			// data when you load at a different size (I hate that).  See HandlePixbufLoaded
			// for where we do the scaling to correct for this problem.
			pixbuf_loader.Request (thumbnail_path, thumbnail_num);
		}
			
		Gdk.GC gc = new Gdk.GC (BinWindow);
		gc.Copy (Style.ForegroundGC (StateType.Normal));
		gc.SetLineAttributes (1, LineStyle.Solid, CapStyle.NotLast, JoinStyle.Round);
		bool selected = CellIsSelected (thumbnail_num);
		

		StateType cell_state = selected ? (HasFocus ? StateType.Selected :StateType.Active) : StateType.Normal;
		
		
		Style.PaintFlatBox (Style, BinWindow, cell_state, 
				    ShadowType.Out, area, this, "IconView", 
				    bounds.X, bounds.Y,
				    bounds.Width - 1, bounds.Height - 1);
		
		Gdk.Rectangle focus = Expand (bounds, -3);
		if (HasFocus && thumbnail_num == FocusCell) {
			Style.PaintFocus(Style, BinWindow, 
					 cell_state, area, 
					 this, null, 
					 focus.X, focus.Y, 
					 focus.Width, focus.Height);
		}

		Gdk.Rectangle region = Gdk.Rectangle.Zero;
		Gdk.Rectangle image_bounds = Expand (bounds, - CELL_BORDER_WIDTH);
		if (image_bounds.Intersect (area, out image_bounds) && thumbnail != null) {
			
			PixbufUtils.Fit (thumbnail, ThumbnailWidth, ThumbnailHeight, 
					 true, out region.Width, out region.Height);
			
			
			region.X = (int) (bounds.X + (bounds.Width - region.Width) / 2);
			region.Y = (int) bounds.Y + ThumbnailHeight - region.Height + CELL_BORDER_WIDTH;
			
			int expansion = ThrobExpansion (thumbnail_num, selected);

			if (region.Width != thumbnail.Width || region.Height != thumbnail.Height)
				pixbuf_loader.Request (thumbnail_path, thumbnail_num);

			region = Expand (region, expansion);
			
			Pixbuf temp_thumbnail;
			

			if (region.Width != thumbnail.Width && region.Height != thumbnail.Height) {
				if (region.Width < thumbnail.Width && region.Height < thumbnail.Height)
					temp_thumbnail = PixbufUtils.ScaleDown (thumbnail, 
										region.Width, region.Height);
				else
					temp_thumbnail = thumbnail.ScaleSimple (region.Width, region.Height, 
										InterpType.Bilinear);
				
				PixbufUtils.CopyThumbnailOptions (thumbnail, temp_thumbnail);
			} else
				temp_thumbnail = thumbnail;

			
			// FIXME There seems to be a rounding issue between the
			// scaled thumbnail sizes, we avoid this for now by using
			// the actual thumnail sizes here.
			region.Width = temp_thumbnail.Width;
			region.Height = temp_thumbnail.Height;
			
			Gdk.Rectangle draw = Expand (region, 1);
			
			Style.PaintShadow (Style, BinWindow, cell_state,
					   ShadowType.Out, area, this, 
					   "IconView", 
					   draw.X, draw.Y, 
					   draw.Width, draw.Height);			
			
			if (region.Intersect (area, out draw)) {
				temp_thumbnail.RenderToDrawable (BinWindow, Style.WhiteGC,
								 draw.X - region.X, 
								 draw.Y - region.Y, 
								 draw.X, draw.Y, 
								 draw.Width, draw.Height, 
								 RgbDither.None, 
								 draw.X, draw.Y);
			}
			
			if (temp_thumbnail != thumbnail)
				temp_thumbnail.Dispose ();
			
			thumbnail.Dispose ();
		}
			
		Gdk.Rectangle layout_bounds = Gdk.Rectangle.Zero;
		if (DisplayDates) {
			string date;
			if (cell_width > 200) {
				date = photo.Time.ToString ();
			} else {
				date = photo.Time.ToShortDateString ();
			}

			Pango.Layout layout = (Pango.Layout)date_layouts [date];
			if (layout == null) {
				layout = new Pango.Layout (this.PangoContext);
				layout.SetText (date);
				date_layouts [date] = layout;
			}
			
			layout.GetPixelSize (out layout_bounds.Width, out layout_bounds.Height);

			layout_bounds.Y = bounds.Y + bounds.Height - CELL_BORDER_WIDTH - layout_bounds.Height;
			layout_bounds.X = bounds.X + (bounds.Width - layout_bounds.Width) / 2;
			
			if (DisplayTags)
				layout_bounds.Y -= TAG_ICON_SIZE;

			if (layout_bounds.Intersect (area, out region)) {
				Style.PaintLayout (Style, BinWindow, cell_state,
						   true, area, this, "IconView", 
						   layout_bounds.X, layout_bounds.Y, 
						   layout);
			}
		}

		if (DisplayTags) {
			Tag [] tags = photo.Tags;
			Gdk.Rectangle tag_bounds;

			tag_bounds.X = bounds.X + (bounds.Width - tags.Length * TAG_ICON_SIZE) / 2;
			tag_bounds.Y = bounds.Y + bounds.Height - CELL_BORDER_WIDTH - TAG_ICON_SIZE;
			tag_bounds.Width = TAG_ICON_SIZE;
			tag_bounds.Height = TAG_ICON_SIZE;

			foreach (Tag t in tags) {
				Pixbuf icon = null;
				if (t.Category.Icon == null) {
					if (t.Icon == null)
						continue;
					icon = t.Icon;
				} else {
					Category category = t.Category;
					while (category.Category.Icon != null)
						category = category.Category;
					icon = category.Icon;
				}

				if (tag_bounds.Intersect (area, out region)) {
					Pixbuf scaled_icon;
					if (icon.Width == tag_bounds.Width) {
						scaled_icon = icon;
					} else {
						scaled_icon = icon.ScaleSimple (tag_bounds.Width, 
										tag_bounds.Height, 
										InterpType.Bilinear);
					}
					
					scaled_icon.RenderToDrawable (BinWindow, Style.WhiteGC,
								      region.X - tag_bounds.X, 
								      region.Y - tag_bounds.Y, 
								      region.X, region.Y, 
								      region.Width, region.Height,
								      RgbDither.None, region.X, region.Y);
					if (scaled_icon != icon)
						scaled_icon.Dispose ();
				}
				tag_bounds.X += tag_bounds.Width + TAG_ICON_VSPACING;
			}
		}
	}

	private void DrawAllCells (Gdk.Rectangle area)
	{
		int start_cell_column = Math.Max ((area.X - BORDER_SIZE) / cell_width, 0);
		int start_cell_row = Math.Max ((area.Y - BORDER_SIZE) / cell_height, 0);
		int start_cell_num = start_cell_column + start_cell_row * cells_per_row;

		int start_cell_x, cell_y;
		GetCellPosition (start_cell_num, out start_cell_x, out cell_y);

		int end_cell_column = Math.Max ((area.X + area.Width - BORDER_SIZE) / cell_width, 0);
		int end_cell_row = Math.Max ((area.Y + area.Height - BORDER_SIZE) / cell_height, 0);

		int num_rows = end_cell_row - start_cell_row + 1;
		int num_cols = Math.Min (end_cell_column - start_cell_column + 1,
					 cells_per_row - start_cell_column);

		int i, cell_num;
		for (i = 0, cell_num = start_cell_num;
		     i < num_rows && cell_num < collection.Items.Length;
		     i ++) {
			int cell_x = start_cell_x;

			//Console.WriteLine ("Drawing row {0}", start_cell_row + i);
			for (int j = 0; j < num_cols && cell_num + j < collection.Items.Length; j ++) {
				DrawCell (cell_num + j, area);
				cell_x += cell_width;
			}

			cell_y += cell_height;
			cell_num += cells_per_row;
		}

	}


	private void GetCellPosition (int cell_num, out int x, out int y)
	{
		int row = cell_num / cells_per_row;
		int col = cell_num % cells_per_row;

		x = col * cell_width + BORDER_SIZE;
		y = row * cell_height + BORDER_SIZE;
	}


	// Scrolling.  We do this in an idle loop so we can catch up if the user scrolls quickly.

	private uint scroll_on_idle_id;

	private int idle_count;	// FIXME

	private bool HandleScrollOnIdle ()
	{
		Adjustment adjustment = Vadjustment;

		if (y_offset == adjustment.Value)
			return false;

		int num_thumbnails = collection.Items.Length;
		int num_rows, start;

		if (y_offset < adjustment.Value) {
			int old_first_row = y_offset / cell_height;
			int new_first_row = (int) (adjustment.Value / cell_height);

			num_rows = new_first_row - old_first_row;
			start = old_first_row * cells_per_row;
		} else {
			int old_last_row = (y_offset + Allocation.Height) / cell_height;
			int new_last_row = ((int) adjustment.Value + Allocation.Height) / cell_height;

			num_rows = old_last_row - new_last_row;
			start = (new_last_row + 1) * cells_per_row;
		}

		for (int i = 0; i < cells_per_row * num_rows; i ++) {
			if (start + i >= num_thumbnails)
				break;

			FSpot.IBrowsableItem photo = collection.Items [start + i];
			string thumbnail_path = Thumbnail.PathForUri (photo.DefaultVersionUri.ToString (), ThumbnailSize.Large);
			pixbuf_loader.Cancel (thumbnail_path);
		}

		y_offset = (int) adjustment.Value;

		scroll_on_idle_id = 0;
		return false;
	}

	private void Scroll ()
	{
		if (scroll_on_idle_id == 0)
			scroll_on_idle_id = GLib.Idle.Add (new GLib.IdleHandler (HandleScrollOnIdle));
	}

	private void CancelScroll ()
	{
		if (scroll_on_idle_id != 0)
			GLib.Source.Remove (scroll_on_idle_id);
	}


	//
	// The throb interface
	//
	private uint throb_timer_id;
	private int throb_cell = -1;
	private int throb_state;
	private const int throb_state_max = 40;
	public void Throb (int cell_num)
	{
		throb_state = 0;
		throb_cell = cell_num;
		if (throb_timer_id == 0)
			throb_timer_id = GLib.Timeout.Add ((39000/throb_state_max)/100,
							   new GLib.TimeoutHandler (HandleThrobTimer));

		InvalidateCell (cell_num);
	}
	
	private void CancelThrob ()
	{
		if (throb_timer_id != 0)
			GLib.Source.Remove (throb_timer_id);
	}

	private bool HandleThrobTimer () 
	{
		//Console.WriteLine ("throb out {1} {0}", throb_cell, 1 - Math.Cos (throb_state));

		InvalidateCell (throb_cell);
		if (throb_state++ < throb_state_max) {
			return true;
		} else {
			throb_cell = -1;
			throb_timer_id = 0;
			return false;
		}
	}

	public void ScrollTo (int cell_num)
	{
		Adjustment adjustment = Vadjustment;
		int x;
		int y;

		GetCellPosition (cell_num, out x, out y);

		if (y < adjustment.Value)
			adjustment.Value = y; 
		else if (y + cell_height > adjustment.Value + adjustment.PageSize)
			adjustment.Value = y + cell_height - adjustment.PageSize;
		
#if USEIDLE
		Scroll ();
#else
		adjustment.Change ();
#endif		
	}


	// Event handlers.

	private void HandleAdjustmentValueChanged (object sender, EventArgs args)
	{
		Scroll ();
	}

	private void HandlePixbufLoaded (PixbufLoader loader, string path, int order, Pixbuf result)
	{
		if (result == null) {
			result = PixbufUtils.ErrorPixbuf;
			//
			// ThumbnailCache Takes Ownership and calls Dispose
			// so we need to copy the ErrorPixbuf
			//
			result = new Pixbuf (result, 0, 0, result.Width, result.Height);
		} 

		if (order > 0 && order < collection.Items.Length) {
			System.Uri uri = collection.Items [order].DefaultVersionUri;
			
			if (!FSpot.PhotoLoader.ThumbnailIsValid (collection.Items [order].DefaultVersionUri, result)) {
				System.Console.WriteLine ("regnerating thumbnail");
				FSpot.ThumbnailGenerator.Default.Request (uri.LocalPath, 0, 256, 256);
			}
		}
		       

		// We have to do the scaling here rather than on load because we need to preserve the 
		// Pixbuf option iformation to verify the thumbnail validity later
		int width, height;
		PixbufUtils.Fit (result, ThumbnailWidth, ThumbnailHeight, true, out width, out height);


		if (result.Width != width && result.Height != height) {
			Gdk.Pixbuf temp = PixbufUtils.ScaleToMaxSize (result, ThumbnailWidth, ThumbnailHeight);
			PixbufUtils.CopyThumbnailOptions (result, temp);
			result.Dispose ();
			result = temp;
		}

		ThumbnailCache.Default.AddThumbnail (path, result);
		InvalidateCell (order);
	}
	
	public Gdk.Rectangle CellBounds (int cell) 
	{
		Rectangle bounds;
		GetCellPosition (cell, out bounds.X, out bounds.Y);
		bounds.Width = cell_width;
		bounds.Height = cell_height;
		return bounds;
	}

	public void InvalidateCell (int order) 
	{
		Rectangle cell_area = CellBounds (order);
		// FIXME where are we computing the bounds incorrectly
		cell_area.Width -= 1;
		cell_area.Height -= 1;
		BinWindow.InvalidateRect (cell_area, false);
	}
			
	private void HandleScrollAdjustmentsSet (object sender, ScrollAdjustmentsSetArgs args)
	{
		if (args.Vadjustment != null)
			args.Vadjustment.ValueChanged += new EventHandler (HandleAdjustmentValueChanged);
	}

	protected override void OnSizeAllocated (Gdk.Rectangle allocation)
	{
		base.OnSizeAllocated (allocation);
		UpdateLayout ();
	}

	protected override bool OnExposeEvent (Gdk.EventExpose args)
	{
		foreach (Rectangle area in args.Region.GetRectangles ()) {
			DrawAllCells (area);
		}
		return base.OnExposeEvent (args);
	}

 	private void HandleButtonPressEvent (object obj, ButtonPressEventArgs args)
 	{
		int cell_num = CellAtPosition ((int) args.Event.X, (int) args.Event.Y);

		args.RetVal = true;

		if (cell_num < 0) {
			args.RetVal = false;
			UnselectAllCells ();
			return;
		}

		switch (args.Event.Type) {
		case EventType.TwoButtonPress:
			if (args.Event.Button != 1
			    || (args.Event.State &  (ModifierType.ControlMask
						     | ModifierType.ShiftMask)) != 0)
				return;
			if (DoubleClicked != null)
				DoubleClicked (this, cell_num);
			return;

		case EventType.ButtonPress:
			GrabFocus ();
			if ((args.Event.State & ModifierType.ControlMask) != 0) {
				ToggleCell (cell_num);
			} else if ((args.Event.State & ModifierType.ShiftMask) != 0) {
				SelectCellRange (FocusCell, cell_num);
			} else if (!CellIsSelected (cell_num)) {
				UnselectAllCells ();
				SelectCell (cell_num);
			}

			if (args.Event.Button == 3){
				ContextMenu (args, cell_num);
				return;
			}
			
			if (args.Event.Button != 1)
				return;

			click_x = (int) args.Event.X;
			click_y = (int) args.Event.Y;

			FocusCell = cell_num;
			return;

		default:
			args.RetVal = false;
			return;
		}
	}

	void ContextMenu (ButtonPressEventArgs args, int cell_num)
	{
		PhotoPopup popup = new PhotoPopup ();
		popup.Activate (args.Event);
	}
	
	private void HandleButtonReleaseEvent (object sender, ButtonReleaseEventArgs args)
	{
		int cell_num = CellAtPosition ((int) args.Event.X, (int) args.Event.Y);

		args.RetVal = true;

		if (cell_num < 0) {
			args.RetVal = false;
			return;
		}
		
		switch (args.Event.Type) {
			case EventType.ButtonRelease:
				if ((args.Event.State & ModifierType.ControlMask) == 0 &&
				    (args.Event.State & ModifierType.ShiftMask  ) == 0 &&
					(selected_cells.Count>1)) {
					UnselectAllCells ();
					SelectCell (cell_num);
				}
	
				break;

			default:
				args.RetVal = false;
				break;
		}
					
	}

	private void HandleKeyPressEvent (object sender, KeyPressEventArgs args)
	{
		int focus_old;
		args.RetVal = true;
		bool shift = ModifierType.ShiftMask == (args.Event.State & ModifierType.ShiftMask);
		bool control = ModifierType.ControlMask == (args.Event.State & ModifierType.ControlMask);

		focus_old = FocusCell;
		switch (args.Event.Key) {
			case Gdk.Key.Down:
				FocusCell += cells_per_row;
				break;
			case Gdk.Key.Left:
				FocusCell--;
				break;
			case Gdk.Key.Right:
				FocusCell++;
				break;
			case Gdk.Key.Up:
				FocusCell -= cells_per_row;
				break;
			case Gdk.Key.Home:
				FocusCell = 0;
				break;
			case Gdk.Key.End:
				FocusCell = collection.Items.Length - 1; 
				break;
			case Gdk.Key.space:
				ToggleCell (FocusCell);
				break;
			default:	
				args.RetVal = false;
				return;		
		}
		
		if (FocusCell < 0 || FocusCell > collection.Items.Length - 1) {
			FocusCell = focus_old;
			args.RetVal = false;
		}	

		if (shift) {
			SelectCellRange (focus_old, FocusCell);
		} else if (!control) {
			UnselectAllCells ();
			SelectCell (FocusCell);
		} 
	
		ScrollTo (FocusCell);
	}

	private void HandleDestroyEvent (object sender, DestroyEventArgs args)
	{
		CancelScroll ();
		CancelThrob ();
	}
}
