using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Megamind.IO.FileFormat;


namespace Hex_File_Analyzer
{
    public partial class MainForm : Form
    {
        #region enum

        public enum FileFormat
        {
            Unknown,
            Binary,
            IntelHex,
            ElfFile
        }

        #endregion

        #region Data

        bool _verifyChesksum;
        int _dataBytesPerRecord;
        string _currentfilename = "";
        FileFormat _currntfileformat;
        IntelHex _ihex = new IntelHex();
        ElfManager _elfManager;

        string _cmdlineToolpath = "Tools";
        static readonly Dictionary<FileFormat, string[]> SupportedFileList = new Dictionary<FileFormat, string[]>
        {
            { FileFormat.Binary,   new[] { ".bin" } },
            { FileFormat.IntelHex, new[] { ".hex", ".eep" } },
            { FileFormat.ElfFile,  new[] { ".elf", ".out", ".axf", ".o" } },
        };

        #endregion

        #region ctor

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                richTextBoxExEventLog.Autoscroll = false;
                richTextBoxExEventLog.ForeColor = Color.Blue;
                richTextBoxExEventLog.Font = new Font("Consolas", 9);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            try
            {
                _verifyChesksum = Convert.ToInt32(ConfigurationManager.AppSettings["VerifyChecksum"]) > 0;
                _dataBytesPerRecord = Convert.ToInt32(ConfigurationManager.AppSettings["DataBytesPerRecord"]);
                _cmdlineToolpath = Path.Combine(Application.StartupPath, _cmdlineToolpath);
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    TryShowFileContent(args[1]);
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region Internal Methods

        private void ViewerAppendText(string str, Color? color = null, bool appendNewLine = true)
        {
            var clr = color ?? Color.Blue;
            if (appendNewLine) str += Environment.NewLine;
            Invoke(new MethodInvoker(() =>
            {
                richTextBoxExEventLog.AppendText(str, clr);
            }));
        }

        private void ViewerClearText()
        {
            Invoke(new MethodInvoker(() =>
            {
                richTextBoxExEventLog.Clear();
            }));
        }

        private void UpdateProgress(int percent)
        {
            Invoke(new MethodInvoker(() =>
            {
                toolStripStatusLabelPercent.Text = percent + "%";
                toolStripProgressBar1.Value = percent;
            }));
        }

        private static FileFormat GetFileFormat(string filename)
        {
            var ext = Path.GetExtension(filename);
            var filetype = SupportedFileList.FirstOrDefault(p => p.Value.Contains(ext.ToLower()));
            return filetype.Key;
        }

        private static string GetSizeString(long bytes)
        {
            string str;
            if (bytes >= (1024 * 1024)) str = string.Format("{0} [{1:0.00} MB]", bytes, bytes / (1024f * 1024f));
            else if (bytes >= 1024) str = string.Format("{0} [{1:0.00} KB]", bytes, bytes / 1024f);
            else str = string.Format("{0:0000} bytes", bytes); ;
            return str;
        }

        public static string ByteArrayToFormatedString(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (var item in bytes)
            {
                if (item == 10) sb.Append("<LF>");
                else if (item == 13) sb.Append("<CR>");
                else if (item < 32 || item > 126) sb.AppendFormat("<{0:X2}>", item);
                else sb.AppendFormat("{0}", (char)item);
            }
            return sb.ToString();
        }

        public static string ByteArrayToHexString(byte[] bytes, string separator = "")
        {
            return BitConverter.ToString(bytes).Replace("-", separator);
        }

        public static byte[] HexStringToByteArray(string hexstr)
        {
            hexstr.Trim();
            hexstr = hexstr.Replace("-", "");
            hexstr = hexstr.Replace(" ", "");
            return Enumerable.Range(0, hexstr.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hexstr.Substring(x, 2), 16))
                             .ToArray();
        }

        private void PopupException(string message, string caption = "Exception")
        {
            Invoke(new Action(() =>
            {
                MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }

        private void TryShowFileContent(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    throw new Exception("File not found!");
                }

                var fileformat = GetFileFormat(filename);
                if (fileformat == FileFormat.Unknown)
                {
                    throw new Exception("File format not supported!");
                }

                _currentfilename = filename;
                _currntfileformat = fileformat;
                UpdateFileContentToDisplay();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void UpdateFileContentToDisplay()
        {
            Task.Run(() =>
            {
                Invoke(new MethodInvoker(() =>
                {
                    UseWaitCursor = true;
                    Cursor.Current = Cursors.AppStarting;
                    Text = Path.GetFileName(_currentfilename) + " - " + Assembly.GetEntryAssembly().GetName().Name;
                }));

                try
                {
                    ViewerClearText();
                    switch (_currntfileformat)
                    {
                        case FileFormat.IntelHex:
                            ShowHexFile(_currentfilename);
                            break;
                        case FileFormat.Binary:
                            ShowBinaryFile(_currentfilename);
                            break;
                        case FileFormat.ElfFile:
                            _elfManager = new ElfManager(_currentfilename, _cmdlineToolpath);
                            ViewerAppendText(_elfManager.GetAllInfo());
                            break;
                    }
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }

                Invoke(new MethodInvoker(() =>
                {
                    UseWaitCursor = false;
                    Cursor.Current = Cursors.Default;
                }));
            });
        }

        private void ShowBinaryFile(string filename)
        {
            var bytes = File.ReadAllBytes(_currentfilename);
            var offset = 0;
            var blockSize = 32;
            var remaining_bytes = bytes.Length;
            ViewerAppendText(" Offset(h)     | Data (h)");
            ViewerAppendText("------------------");
            UpdateProgress(0);
            while (remaining_bytes > 0)
            {
                if (remaining_bytes < blockSize) blockSize = remaining_bytes;
                ViewerAppendText(string.Format("{0:X4} : {1}", offset,
                    BitConverter.ToString(bytes, offset, blockSize).Replace("-", "")));
                Application.DoEvents();
                offset += blockSize;
                remaining_bytes -= blockSize;
                UpdateProgress((offset * 100) / bytes.Length);
            }
            UpdateProgress(100);
        }

        private void ShowHexFile(string filename)
        {
            _ihex = new IntelHex();
            _ihex.Read(filename, _verifyChesksum);
            long totalmemused = 0;
            long totalmemblank = 0;
            var blankspaces = new long[_ihex.MemBlocks.Count];
            var sortedBlocks = _ihex.MemBlocks.OrderBy(p => p.Start).ToList();
            UpdateProgress(0);

            // calculate total and blank spaces
            for (int i = 0; i < sortedBlocks.Count; i++)
            {
                long blankSpace = 0;
                if (i + 1 < sortedBlocks.Count) blankSpace = sortedBlocks[i + 1].Start - sortedBlocks[i].End;
                totalmemblank += blankspaces[i];
                totalmemused += sortedBlocks[i].Size;

                // add black space in corresponding index
                for (int j = 0; j < _ihex.MemBlocks.Count; j++)
                    if (_ihex.MemBlocks[j].Start == sortedBlocks[i].Start) blankspaces[j] = blankSpace;
            }

            // show summary
            var count = 0;
            ViewerAppendText("Memory Block Summary:");
            ViewerAppendText(" # | Start      | End        |  Size \t\t| Blank Before");
            ViewerAppendText("-------------------------------------------------------------------");
            UpdateProgress(5);
            foreach (var b in _ihex.MemBlocks)
            {
                count++;
                ViewerAppendText(string.Format("{0:00} | 0x{1:X8} | 0x{2:X8} | {3} \t| {4}", count, b.Start, b.End,
                    GetSizeString(b.Size), GetSizeString(blankspaces[count - 1])));
            }
            ViewerAppendText("-------------------------------------------------------------------");
            ViewerAppendText(string.Format("\t\t\t   Total: {0} \t| {1}", GetSizeString(totalmemused), GetSizeString(totalmemblank)));

            // show records
            count = 0;
            ViewerAppendText("\rMemory Block Data:");
            ViewerAppendText(" #   | Record | Offset |  Data");
            ViewerAppendText("-------------------------------------------------------------------");
            UpdateProgress(10);
            foreach (var r in _ihex.Records)
            {
                count++;
                ViewerAppendText(string.Format("{0:0000} | {1}[{2}] | 0x{3:X4} | {4:00}", count, r.RecordType,
                    (RecordType)r.RecordType, r.Address, BitConverter.ToString(r.DataBlock).Replace("-", "")));
                UpdateProgress((count * 100)/ _ihex.Records.Count);
            }
            UpdateProgress(100);
        }

        #endregion

        #region Menu Strip Events

        private void AutoscrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            richTextBoxExEventLog.Autoscroll = AutoscrollToolStripMenuItem.Checked;
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            HelpToolStripButton_Click(this, e);
        }

        #endregion

        #region ToolStrip Events

        private void NewToolStripButton_Click(object sender, EventArgs e)
        {
            Process.Start(Assembly.GetExecutingAssembly().Location);
        }

        private void OpenToolStripButton_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter =
                    "Firmware (all types)|*.hex;*.eep;*.bin;*.elf;*.out;*.axf;*.o|" +
                    "Hex Files (*.hex)|*.hex|" +
                    "Eep files (*.eep)|*.eep|" +
                    "Bin files (*.bin)|*.bin|" +
                    "Elf files (*.elf)|*.elf|" +
                    "Out files (*.out)|*.out|" +
                    "Axf files (*.axf)|*.axf|" +
                    "Obj files (*.o)|*.o|" +
                    "All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _currentfilename = ofd.FileName;
                    TryShowFileContent(_currentfilename);
                }
            }
        }

        private void SaveToolStripButton_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter =
                    "Hex files (*.hex)|*.hex|" +
                    "Eep files (*.eep)|*.eep|" +
                    "Binary files (*.bin)|*.bin|" +
                    "All files (*.*)|*.*";
                sfd.FileName = Path.GetFileName(_currentfilename);
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var ext = Path.GetExtension(sfd.FileName);
                        _ihex.BytesPerRecord = _dataBytesPerRecord;
                        if (string.Compare(ext, ".hex", true) == 0) _ihex.SaveIntelHex(sfd.FileName);
                        else if (string.Compare(ext, ".bin", true) == 0) _ihex.SaveBinaryImage(sfd.FileName);
                    }
                    catch (Exception ex)
                    {
                        PopupException(ex.Message);
                    }
                }
            }
        }

        private void ReloadToolStripButton_Click(object sender, EventArgs e)
        {
            TryShowFileContent(_currentfilename);
        }

        private void HelpToolStripButton_Click(object sender, EventArgs e)
        {
            var info = string.Format("{0} {1}\r\nDeveloped by: \r\nGSM Rana \r\ngithub.com/gsmrana",
                Assembly.GetEntryAssembly().GetName().Name,
                Assembly.GetEntryAssembly().GetName().Version);
            MessageBox.Show(info, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        #endregion

        #region RichTextBoxEx Events

        private void RichTextBoxExEventLog_SelectionChanged(object sender, EventArgs e)
        {
            labelLogSelLine.Text = string.Format("Start: {0}", richTextBoxExEventLog.SelectionStart);
            labelLogSelLength.Text = string.Format("Length: {0}", richTextBoxExEventLog.SelectionLength);
        }

        #endregion

        #region RichTextBoxEx Context Menu

        private void ContextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            CopyToolStripMenuItem.Enabled = richTextBoxExEventLog.SelectionLength > 0;
            CopyAllToolStripMenuItem.Enabled = richTextBoxExEventLog.Text.Length > 0;
        }

        private void CopyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            richTextBoxExEventLog.Copy();
        }

        private void CopyAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(richTextBoxExEventLog.Text);
        }

        #endregion

        #region File Drag and Drop

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                TryShowFileContent(files[0]);
            }
        }

        #endregion

    }
}
