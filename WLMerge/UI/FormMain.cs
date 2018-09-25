﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace WLMerge
{
    public partial class FormMain : Form
    {
        #region Private Instance Variables

        private const string BricklinkCatalogItemLink = "https://www.bricklink.com/v2/catalog/catalogitem.page?P={0}&C={1}";
        private InventoryItemList _itemList;
        private int _fileCount;
        private int _pieceCount;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates and initializes the main form of the app
        /// </summary>
        public FormMain()
        {
            InitializeComponent();

            // Create, initialize and bind the item list that will hold all items in list/table
            _itemList = new InventoryItemList();
            _itemList.ItemAdded += _itemList_ItemAdded;
            _itemList.ItemRemoved += _itemList_ItemRemoved;
            inventoryItemListBindingSource.DataSource = _itemList;

            // Perform a UI reset
            ResetForm();
        }

        #endregion

        #region Private Methods

        // Check for first usage and show help if so
        private void FirstTimeUseStep()
        {
            var versionHistory = Program.VersionHistory;

            if (versionHistory == Program.History.FirstRun)
            {
                // This is the first time the user runs this program, show ReadMe which contain instructions
                new FormAbout(FormAbout.SelectedView.ReadMe).ShowDialog();
            }
            else if (versionHistory == Program.History.NewVersion)
            {
                // This is the first time the user runs this version of the program, show revision history
                new FormAbout(FormAbout.SelectedView.History).ShowDialog();
            }
        }

        // Reset the form to initial state
        private void ResetForm()
        {
            _itemList.Clear();
            _fileCount = 0;
            _pieceCount = 0;
            buttonExport.Enabled = false;
            buttonClear.Enabled = false;
            checkBoxHideEmptyColumns.Enabled = false;
            buttonDownloadImages.Enabled = false;
            UpdateTitle();
        }

        // Update app title to reflect state
        private void UpdateTitle() => 
            Text = _itemList.Count == 0
                ? $"{Program.AppName} - drag or browse file(s) to add Wanted Lists"
                : $"{Program.AppName} - lots: {_itemList.Count}, pieces: {_pieceCount}, files: {_fileCount}";

        // Given an array of files (valid and complete file paths), handle them (ie load them into app)
        private void HandleXmlFiles(string[] files)
        {

            _fileCount += files.Count();

            foreach (var path in files)
            {
                // Load and parse the XML-file
                var items = Inventory.FromXmlFile(path);

                // Add the items of the Wanted List to our main list (ie merge)
                foreach (var item in items.Items)
                {
                    _itemList.Insert(item);
                }
            }
        }

        private int ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty property)
        {
            //var color = (int)dataGridViewItems[(int)InventoryItem.ItemProperty.COLOR, e.RowIndex].Value;
            var columnName = property.ToString();
            var columnIndex = dataGridViewItems.Columns[columnName].Index;

            return columnIndex;
        }

        // Toggle the empty columns visible or invisible
        private void ToggleEmptyColumnsVisible(bool hide)
        {
            // Check all columns
            foreach(DataGridViewColumn column in dataGridViewItems.Columns)
//          foreach(int columnIndex in Enum.GetValues(typeof(InventoryItem.ItemProperty)))
            {
                var columnIndex = column.Index;

                // Assume it's empty
                var columnEmpty = true;

                // Do we want to hide empty?
                if (hide)
                {
                    // Yes, check all rows in this column if they are empty or not
                    for (int rowIndex = 0; rowIndex < dataGridViewItems.Rows.Count; rowIndex++)
                    {
                        // Column empty iff previous cells are empty and this cell is too
                        columnEmpty &= dataGridViewItems[columnIndex, rowIndex].Value == null;
                    }
                }

                // Show column if 'hide' is false OR column is not empty 
                dataGridViewItems.Columns[columnIndex].Visible = !hide || !columnEmpty;
            }
        }

        // Clear all cells in the given column
        private void ClearColumnValues(int columnIndex)
        {
            for(int rowIndex = 0; rowIndex < dataGridViewItems.Rows.Count; rowIndex++)
            {
                dataGridViewItems[columnIndex, rowIndex].Value = null;
            }
        }

        // Set all cells in the given column to a new value
        private void SetColumnValues(int columnIndex, object newValue)
        {
            for (int rowIndex = 0; rowIndex < dataGridViewItems.Rows.Count; rowIndex++)
            {
                dataGridViewItems[columnIndex, rowIndex].Value = newValue;
            }
        }

        // Transform all cells in the given column to a new value, using given 'transformer'
        private void TransformColumnValues(int columnIndex, ValueTransformer transformer)
        {
            for (int rowIndex = 0; rowIndex < dataGridViewItems.Rows.Count; rowIndex++)
            {
                var oldValue = dataGridViewItems[columnIndex, rowIndex].Value;
                var newValue = transformer.Transform(oldValue);
                    
                dataGridViewItems[columnIndex, rowIndex].Value = newValue;
            }
        }

        // Download an image of an item from Bricklink. If useLocalCache is true (default) it will store any downloaded image
        // in local settings directory (cache dir) for faster retrieval next time - ie if image exist locally use it instead of
        // downloading it.
        private Image GetItemImageFromBricklink(string itemId, int color, bool useLocalCache = true)
        {
            Image image = null;

            try
            {
                // Path to local cache is stored in the App data folder, under cache. One dir for each color, one file for each image
                var imageCachePath = $@"{Program.AppDataFolder}\cache\{color}\{itemId}.png";

                // Do we use cache and does cached image exist?
                if (useLocalCache && File.Exists(imageCachePath))
                {
                    // Yes, use it
                    image = Bitmap.FromFile(imageCachePath);
                }
                else
                {
                    // No. Make sure cache dir exist first. Will create both root cache dir and color dir as needed
                    if (useLocalCache && !Directory.Exists(Path.GetDirectoryName(imageCachePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(imageCachePath));
                    }

                    // Download image from Bricklink
                    using (WebClient wc = new WebClient())
                    {
                        // Get image as stream of bytes
                        var url = BricklinkItems.BricklinkItemImageUrl(itemId, color);
                        byte[] data = wc.DownloadData(url);

                        // Store to bitmap
                        using (MemoryStream ms = new MemoryStream(data))
                        {
                            image = new Bitmap(ms);

                            // Save to local cache for future use, if enabled
                            if (useLocalCache)
                            {
                                image.Save(imageCachePath);
                            }
                        }
                    }
                }
            }
            catch // consume any error and consider this image retrieval a failure
            {
                // Was not able to download image (network error, non-existing image etc)
                return null;
            }

            return image;
        }

        #endregion

        #region Event handling

        // Event: rows have been removed, update counter in header accordingly
        private void _itemList_ItemRemoved(object sender, ItemRemovedEventArgs e)
        {
            _pieceCount -= e.OldItem.MinQty;
            UpdateTitle();
        }

        // Event: rows have been added, update counter in header accordingly
        private void _itemList_ItemAdded(object sender, ItemAddedEventArgs e)
        {
            _pieceCount += e.NewItem.MinQty;
            UpdateTitle();
        }

        // Event: determine if content being dragged onto form is of interest
        private void FormMain_DragEnter(object sender, DragEventArgs e)
        { 
            // Has to be files that will be dropped
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var allXml = true;

                // Also has to be files with '.xml' extension, no exceptions
                foreach(var f in files)
                {
                    var ext = Path.GetExtension(f).ToLower();
                    allXml = allXml && ext == ".xml";
                }

                e.Effect = allXml ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        // Event: act when XML-files have been dropped
        private void FormMain_DragDrop(object sender, DragEventArgs e) => HandleXmlFiles((string[])e.Data.GetData(DataFormats.FileDrop));

        // Event: browse for files button clicked
        private void buttonBrowsForFile_Click(object sender, EventArgs e)
        {
            // This instead of FileOk event to avoid dialog to hover while data is loading
            if (openFileDialogXml.ShowDialog() == DialogResult.OK)
            {
                HandleXmlFiles(openFileDialogXml.FileNames);
            }
        }

        // Event: Rows have been added to the table. Make adjustments to form and table as data have been added
        private void dataGridViewItems_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            if (_itemList.Count == 1)
            {
                // Enable buttons 
                buttonExport.Enabled = buttonClear.Enabled = buttonDownloadImages.Enabled = checkBoxHideEmptyColumns.Enabled = true; ;

                // Adjust headers just once, they will be assigned when first row is loaded
                dataGridViewItems.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.ColumnHeader);
            }

            dataGridViewItems.AutoResizeColumn(ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.REMARKS), DataGridViewAutoSizeColumnMode.AllCells);

            UpdateTitle();
        }

        // Event: button to exit application have been clicked
        private void buttonExit_Click(object sender, EventArgs e) => Application.Exit();

        // Event: button to clear contents of table have been clicked
        private void buttonClear_Click(object sender, EventArgs e)
        {
            // Remove all items previously loaded/merged
            _itemList.Clear();

            // Reset form to initial state
            ResetForm();
        }

        // Event: button to export items to a Wanted List have been clicked
        private void buttonExport_Click(object sender, EventArgs e)
        {
            // First convert to Inventory, then serialize it to an XML-string
            var xml =  _itemList.ToInventory().ToXml();

            // Fill clipboard with the XML-string and notify user
            Clipboard.SetText(xml);
            MessageBox.Show("All lots exported to clipboard! To import in Bricklink:\nWant > Upload > Upload BrickLink XML format", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Event: a cell in the table is being formatted. Do some adjustments and add tool tips along the way
        private void dataGridViewItems_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Item type will show a description as tooltip
            if(e.ColumnIndex == ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.ITEMTYPE))
            {
                var itemTypeDescription = BricklinkItems.BricklinkItemTypeDescription((string)e.Value);
                dataGridViewItems[e.ColumnIndex, e.RowIndex].ToolTipText = itemTypeDescription;
            }
            // Paint color column cells and show tooltip with color description
            else if(e.ColumnIndex == ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.COLOR))
            {
                // Colorize cell
                var color = (int)e.Value;
                var cInfo = BricklinkColors.GetInfo(color);
                e.CellStyle.BackColor = BricklinkColors.FromString(cInfo.Bg);
                e.CellStyle.ForeColor = BricklinkColors.FromString(cInfo.Fg);

                // Also add a color description as tool tip for the cell
                dataGridViewItems[e.ColumnIndex, e.RowIndex].ToolTipText = cInfo.Name;
            } 
            // Add a tool tip to item id's with URL that this cell will launch upon click
            else if(e.ColumnIndex == ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.ITEMID))
            {
                var itemId = (string)e.Value;
                var color = (int)dataGridViewItems[ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.COLOR), e.RowIndex].Value;
                var url = string.Format(BricklinkCatalogItemLink, itemId, color);
                dataGridViewItems[e.ColumnIndex, e.RowIndex].ToolTipText = url;
            }
            // All other cells, general instruction as tool tip
            else
            {
                var description = dataGridViewItems[e.ColumnIndex, e.RowIndex].ReadOnly 
                    ? dataGridViewItems.Columns[e.ColumnIndex].Name
                    : "Click to edit cell\nRight click to edit column";
                dataGridViewItems[e.ColumnIndex, e.RowIndex].ToolTipText = description;
            }
        }

        // Event: contents of a cell in the table have been clicked
        private void dataGridViewItems_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // Don't react if it's the header that's been clicked
            if(e.RowIndex < 0)
            {
                return;
            }

            // If it's a cell with a link, launch the link
            if (dataGridViewItems[e.ColumnIndex, e.RowIndex].GetType() == typeof(DataGridViewLinkCell))
            {
                var itemNo = dataGridViewItems[ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.ITEMID), e.RowIndex].Value.ToString();
                var color = dataGridViewItems[ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.COLOR), e.RowIndex].Value.ToString();
                var url = string.Format(BricklinkCatalogItemLink, itemNo, color);
                System.Diagnostics.Process.Start(url);
            }
        }

        // Event: the checkbox to hide/show empty columns have been clicked. React accordingly
        private void checkBoxHideEmptyColumns_CheckedChanged(object sender, EventArgs e) => ToggleEmptyColumnsVisible(((CheckBox)sender).Checked);

        // Event: cell has been right clicked. Show context menu
        private void dataGridViewItems_CellContextMenuStripNeeded(object sender, DataGridViewCellContextMenuStripNeededEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;

            // No context menu if it's headers or if the column is read only
            if (e.RowIndex == -1 || e.ColumnIndex == -1 || dgv.Columns[e.ColumnIndex].ReadOnly)
            {
                return;
            }

            // Valid right click, show context menu
            e.ContextMenuStrip = contextMenuStripDgvRightClick;
        }

        // Event: fix to ease right click menu: make right clicked cell the active one. By doing this, we can get row/column of active cell
        private void dataGridViewItems_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            // Detect right click on cells (not headers) and select that clicked cell (ie reposition selection)
            if (e.ColumnIndex != -1 && e.RowIndex != -1 && e.Button == MouseButtons.Right)
            {
                DataGridViewCell c = (sender as DataGridView)[e.ColumnIndex, e.RowIndex];
                if (!c.Selected)
                {
                    c.DataGridView.ClearSelection();
                    c.DataGridView.CurrentCell = c;
                    c.Selected = true;
                }
            }
        }

        // Event: context menu option for clearing column has been selected
        private void toolStripMenuItemClear_Click(object sender, EventArgs e)
        {
            var columnIndex = dataGridViewItems.SelectedCells[0].ColumnIndex;
            ClearColumnValues(columnIndex);
        }

        // Event: context menu option for setting column values has been selected
        private void toolStripMenuItemSet_Click(object sender, EventArgs e)
        {
            var columnIndex = dataGridViewItems.SelectedCells[0].ColumnIndex;

            var fsv = new FormSetValue((InventoryItem.ItemProperty)columnIndex);
            fsv.NewValue += (snd, ea) => { SetColumnValues(columnIndex, ea.NewValue); };
            fsv.ShowDialog();
        }

        // Event: context menu option for transforming column values has been selected
        private void toolStripMenuItemTransform_Click(object sender, EventArgs e)
        {
            var columnIndex = dataGridViewItems.SelectedCells[0].ColumnIndex;
            var ftv = new FormTransformValue((InventoryItem.ItemProperty)columnIndex);
            ftv.TransformValue += (snd, ea) => { TransformColumnValues(columnIndex, ea.Transformer ); };
            ftv.ShowDialog();
        }

        // Event: add row numbers to row headers
        private void dataGridViewItems_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            var dgv = sender as DataGridView;
            var rowNumber = (e.RowIndex + 1).ToString();

            var centerFormat = new StringFormat() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var headerBounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, dgv.RowHeadersWidth, e.RowBounds.Height);
            e.Graphics.DrawString(rowNumber, Font, SystemBrushes.ControlText, headerBounds, centerFormat);
        }

        // Event: react upon key press (currently to catch delete button)
        private void dataGridViewItems_KeyDown(object sender, KeyEventArgs e)
        {
            // User pressed delete?
            if(e.KeyCode == Keys.Delete)
            {
                var dgv = (DataGridView)sender;
                var rowsSelected = dgv.SelectedRows.Count;

                // Any rows selected (means user wants to delete one or more rows)?
                if (rowsSelected > 0)
                {
                    // Prompt user to confirm
                    var dialogResult = MessageBox.Show($"Delete {rowsSelected} rows?", "Please confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

                    // Cancel deletion depending on answer. If 'No', tell system keypress has been handled and deletion will not be performed
                    e.Handled = dialogResult == DialogResult.No;
                }
            }
        }

        // Event: Handle about button click, ie show Abuot Dialog
        private void buttonAbout_Click(object sender, EventArgs e) => new FormAbout().ShowDialog();

        // Event: main form is shown. Check for first usage
        private void FormMain_Shown(object sender, EventArgs e) => FirstTimeUseStep();

        // Event: cell change; we're interested in specific column only. This is a hack since I can't get notification of binding-
        // list changes to work. I get notification when it's changed but no way of getting value before *and* after the change, only either
        // This makes it impossible to change Piece Count of form correctly. So as for now: recount *all* rows when a cell in the piece column
        // has changed...
        private void dataGridViewItems_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_itemList != null && e.ColumnIndex == ItemPropertyToDatagridColumnId(InventoryItem.ItemProperty.MINQTY))
            {
                _pieceCount = _itemList.Sum(i => i.MinQty);
                UpdateTitle();
            }
        }

        #endregion

        // Event: Button for downloading item images has been clicked 
        private void buttonDownloadImages_Click(object sender, EventArgs e)
        {
            // Fire up general progress form and assign worker event handler to it
            var formProgress = new FormProgress() { Message = "Downloading images..."};
            formProgress.DoWork += new FormProgress.DoWorkEventHandler(ImageDownloadWorker);
            formProgress.Show(this);

        }

        // Event: Worker that handle image download. For each row in dataview grid of items, get item id and color and get 
        // image for it, if it exist. It will use cache for already downloaded images.
        private void ImageDownloadWorker(FormProgress sender, DoWorkEventArgs e)
        {
            // Get datagrid indexes of interest
            var columnIndexImage = dataGridViewItems.Columns["Image"].Index;
            var columnIndexItemId = dataGridViewItems.Columns["ItemId"].Index;
            var columnIndexColor = dataGridViewItems.Columns["Color"].Index;
            var rowsTotal = dataGridViewItems.Rows.Count;

            // No point in doing this if we have no data
            if (rowsTotal <= 0)
            {
                return;
            }

            // Get image for each row in grid
            for (var rowIndex = 0; rowIndex < rowsTotal; rowIndex++)
            {
                // Only update if there isn't an image for this item/row
                if (dataGridViewItems[columnIndexImage, rowIndex].Value == null)
                {
                    var color = (int)dataGridViewItems[columnIndexColor, rowIndex].Value;
                    var itemId = (string)dataGridViewItems[columnIndexItemId, rowIndex].Value;
                    Image image = GetItemImageFromBricklink(itemId, color);

                    dataGridViewItems[columnIndexImage, rowIndex].Value = image;
                }

                // Update progress dialog with current progress
                var message = $"Getting images {rowIndex + 1} of {rowsTotal}...";
                var percent = ((rowIndex + 1) / (double)rowsTotal) * 100d;
                sender.UpdateProgress((int)percent, message);
            }
        }
    }
}