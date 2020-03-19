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


namespace Elf_File_Analyzer
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

        bool _operationInProgress;
        bool _verifyChesksum;
        string _currentfilename = "";
        FileFormat _currntfileformat;
        IntelHex _ihex = new IntelHex();
        ElfManager _elfManager = new ElfManager();

        readonly string SourceDirKeyword = "$SRCDIR";
        readonly string SourceFileKeyword = "$SRCFILE";
        readonly string CmdlineToolBuildinpath = "Tools";
        static readonly Dictionary<FileFormat, string[]> SupportedFileList = new Dictionary<FileFormat, string[]>
        {
            { FileFormat.Binary,   new[] { ".bin" } },
            { FileFormat.IntelHex, new[] { ".hex", ".eep" } },
            { FileFormat.ElfFile,  new[] { ".elf", ".out", ".axf", ".a", ".o" } },
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
                richTextBoxExEventLog.WordWrap = false;
                richTextBoxExEventLog.Autoscroll = false;
                richTextBoxExEventLog.ForeColor = Color.Blue;
                richTextBoxExEventLog.Font = new Font("Consolas", 9);

                ElfManager.CmdlineToolPath = Path.Combine(Application.StartupPath, CmdlineToolBuildinpath);
                toolStripComboBoxCmdline.Items.AddRange(ElfManager.CmdLineTools.Keys.ToArray());
                if (toolStripComboBoxCmdline.Items.Count > 0)
                    toolStripComboBoxCmdline.SelectedIndex = 0;
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
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    TryShowFileContent(args[1], GetFileFormatFromExt(args[1]));
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

        int _prevpercent = 0;
        private void UpdateProgress(int percent)
        {
            if (percent == _prevpercent)
                return;
            _prevpercent = percent;
            Invoke(new MethodInvoker(() =>
            {
                toolStripProgressBar1.Value = percent;
            }));
        }

        private static FileFormat GetFileFormatFromExt(string filename)
        {
            var ext = Path.GetExtension(filename).ToLower();
            var filetype = SupportedFileList.FirstOrDefault(p => p.Value.Contains(ext));
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

        private void TryShowFileContent(string filename, FileFormat decodeas)
        {
            if (string.IsNullOrEmpty(filename))
                return;

            if (_operationInProgress)
                return;

            try
            {
                _currentfilename = filename;
                _currntfileformat = decodeas;
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
                _operationInProgress = true;
                Invoke(new MethodInvoker(() =>
                {
                    UseWaitCursor = true;
                    Cursor.Current = Cursors.AppStarting;
                    Text = string.Format("{0} - {1}", Path.GetFileName(_currentfilename),
                        Assembly.GetEntryAssembly().GetName().Name);
                }));

                try
                {
                    UpdateProgress(0);
                    ViewerClearText();
                    ViewerAppendText(string.Format("File Size: {0}", GetSizeString(new FileInfo(_currentfilename).Length)), Color.DarkMagenta);
                    UpdateProgress(10);

                    switch (_currntfileformat)
                    {
                        case FileFormat.Binary:
                            ShowBinaryFile(_currentfilename);
                            break;
                        case FileFormat.IntelHex:
                            ShowHexFile(_currentfilename);
                            break;
                        case FileFormat.ElfFile:
                        default:
                            _elfManager = new ElfManager(_currentfilename);
                            ViewerAppendText(_elfManager.GetSizeInfo());
                            UpdateProgress(20);
                            ViewerAppendText(_elfManager.GetAllHeadersInfo());
                            UpdateProgress(100);
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
                _operationInProgress = false;
            });
        }

        private void ShowBinaryFile(string filename)
        {
            var bytes = File.ReadAllBytes(filename);
            var offset = 0;
            var blockSize = 32;
            var remaining_bytes = bytes.Length;
            ViewerAppendText("Offset(h): " + BitConverter.ToString(Enumerable.Range(0, blockSize).Select(p => (byte)p).ToArray()).Replace("-", ""));
            ViewerAppendText("---------------------------------------------------------------------------");
            UpdateProgress(0);
            var sb = new StringBuilder();
            while (remaining_bytes > 0)
            {
                if (remaining_bytes < blockSize) blockSize = remaining_bytes;
                sb.AppendFormat("{0:X8} : {1}\r", offset, BitConverter.ToString(bytes, offset, blockSize).Replace("-", ""));
                offset += blockSize;
                remaining_bytes -= blockSize;
                UpdateProgress((offset * 100) / bytes.Length);
            }
            ViewerAppendText(sb.ToString());
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
            ViewerAppendText(" # | Start      | End        |  Size");
            ViewerAppendText("---------------------------------------------------");
            UpdateProgress(5);
            foreach (var b in _ihex.MemBlocks)
            {
                count++;
                ViewerAppendText(string.Format("{0:00} | 0x{1:X8} | 0x{2:X8} | {3}", count, b.Start, b.End, GetSizeString(b.Size)));
            }
            ViewerAppendText("---------------------------------------------------");
            ViewerAppendText(string.Format("\t\t\t   Total: {0} \t", GetSizeString(totalmemused)));

            // show records
            count = 0;
            ViewerAppendText("\rMemory Block Data:");
            ViewerAppendText(" #   | Record | Offset | Data");
            ViewerAppendText("---------------------------------------------------------------------------");
            UpdateProgress(10);
            var sb = new StringBuilder();
            foreach (var r in _ihex.Records)
            {
                count++;
                sb.AppendFormat("{0:0000} | {1}[{2}] | 0x{3:X4} | {4:00}\r", count, r.RecordType,
                    ((RecordType)r.RecordType).ToString().Substring(0, 3), r.Address, BitConverter.ToString(r.DataBlock).Replace("-", ""));
                UpdateProgress((count * 100) / _ihex.Records.Count);
            }
            ViewerAppendText(sb.ToString());
            UpdateProgress(100);
        }

        #endregion

        #region Menu Strip Events

        private void NewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Assembly.GetExecutingAssembly().Location);
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenToolStripButton_Click(sender, e);
        }

        private void SaveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveToolStripButton_Click(sender, e);
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ClearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            richTextBoxExEventLog.Clear();
        }

        private void AutoscrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            richTextBoxExEventLog.Autoscroll = AutoscrollToolStripMenuItem.Checked;
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutToolStripButton_Click(this, e);
        }

        #endregion

        #region Operation Menu Strip 

        private void GetSizeInfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentfilename))
                return;

            Task.Run(() =>
            {
                try
                {
                    UpdateProgress(0);
                    ViewerClearText();
                    ViewerAppendText(string.Format("File Size: {0}", GetSizeString(new FileInfo(_currentfilename).Length)), Color.DarkMagenta);
                    _elfManager = new ElfManager(_currentfilename);
                    UpdateProgress(10);
                    ViewerAppendText(_elfManager.GetSizeInfo());
                    UpdateProgress(100);
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }
            });
        }

        private void GetElfHeadersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TryShowFileContent(_currentfilename, FileFormat.ElfFile);
        }

        private void GetDisassemblyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentfilename))
                return;

            Task.Run(() =>
            {
                try
                {
                    UpdateProgress(0);
                    ViewerClearText();
                    ViewerAppendText(string.Format("File Size: {0}", GetSizeString(new FileInfo(_currentfilename).Length)), Color.DarkMagenta);
                    ViewerAppendText("Getting disassembly...", Color.DarkMagenta);
                    _elfManager = new ElfManager(_currentfilename);
                    UpdateProgress(10);
                    ViewerAppendText(_elfManager.GetDisassemblyText());
                    UpdateProgress(100);
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }
            });
        }

        private void OpenAsTextFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentfilename))
                return;

            Task.Run(() =>
            {
                try
                {
                    UpdateProgress(0);
                    ViewerClearText();
                    ViewerAppendText(string.Format("File Size: {0}", GetSizeString(new FileInfo(_currentfilename).Length)), Color.DarkMagenta);
                    UpdateProgress(10);
                    ViewerAppendText(File.ReadAllText(_currentfilename));
                    UpdateProgress(100);
                }
                catch (Exception ex)
                {
                    PopupException(ex.Message);
                }
            });
        }


        private void OpenAsBinaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TryShowFileContent(_currentfilename, FileFormat.Binary);
        }

        #endregion

        #region ToolStrip Events

        private void OpenToolStripButton_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter =
                    "Firmware (all types)|*.hex;*.eep;*.bin;*.elf;*.out;*.axf;*.o|" +
                    "Bin files (*.bin)|*.bin|" +
                    "Hex Files (*.hex)|*.hex|" +
                    "Eep files (*.eep)|*.eep|" +
                    "Elf files (*.elf)|*.elf|" +
                    "Out files (*.out)|*.out|" +
                    "Axf files (*.axf)|*.axf|" +
                    "Lib files (*.a)|*.a|" +
                    "Obj files (*.o)|*.o|" +
                    "All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _currentfilename = ofd.FileName;
                    TryShowFileContent(_currentfilename, GetFileFormatFromExt(_currentfilename));
                }
            }
        }

        private void SaveToolStripButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentfilename))
                return;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter =
                    "Hex files (*.hex)|*.hex|" +
                    "Bin files (*.bin)|*.bin|" +
                    "Srec files (*.srec)|*.srec|" +
                    "All files (*.*)|*.*";
                sfd.FileName = string.Concat(Path.GetFileNameWithoutExtension(_currentfilename), ".hex");
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        UpdateProgress(0);
                        ViewerClearText();
                        ViewerAppendText("Converting file format...", Color.DarkMagenta);
                        ViewerAppendText("Input file: " + Path.GetFileName(_currentfilename), Color.DarkMagenta);
                        Application.DoEvents();
                        _elfManager = new ElfManager(_currentfilename);
                        UpdateProgress(10);
                        var response = _elfManager.SaveOutputFile(sfd.FileName);
                        UpdateProgress(80);
                        ViewerAppendText(response);
                        ViewerAppendText("Saved as  : " + Path.GetFileName(sfd.FileName), Color.DarkMagenta);
                        UpdateProgress(100);
                    }
                    catch (Exception ex)
                    {
                        PopupException(ex.Message);
                    }
                }
            }
        }

        private void DisassemblyToolStripButton_Click(object sender, EventArgs e)
        {
            GetDisassemblyToolStripMenuItem_Click(sender, e);
        }

        private void AboutToolStripButton_Click(object sender, EventArgs e)
        {
            var info = string.Format("{0} {1}\r\nDeveloped by: \r\nGSM Rana \r\ngithub.com/gsmrana",
                Assembly.GetEntryAssembly().GetName().Name,
                Assembly.GetEntryAssembly().GetName().Version);
            MessageBox.Show(info, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ToolStripButtonCmdExecute_Click(object sender, EventArgs e)
        {
            try
            {
                var cli = toolStripComboBoxCmdline.Text.Trim();

                Task.Run(() =>
                {
                    try
                    {
                        var idx = cli.IndexOf(' ');
                        var toolname = cli;
                        var cmdline = "";
                        if (idx > 0)
                        {
                            toolname = cli.Substring(0, idx);
                            cmdline = cli.Substring(idx + 1, cli.Length - idx - 1);
                            if (cmdline.Contains(SourceFileKeyword))
                                cmdline = cmdline.Replace(SourceFileKeyword, string.Format("\"{0}\"", _currentfilename));
                            if (cmdline.Contains(SourceDirKeyword))
                                cmdline = cmdline.Replace(SourceDirKeyword, string.Format("\"{0}\"", Path.GetDirectoryName(_currentfilename)));
                        }

                        UpdateProgress(0);
                        ViewerClearText();
                        ViewerAppendText(string.Format("Executing CLI: {0} {1}", toolname, cmdline), Color.DarkMagenta);
                        ViewerAppendText("---------------------------------------------------------------------------", Color.DarkMagenta);
                        UpdateProgress(10);

                        var response = _elfManager.ExecuteCommandline(toolname, cmdline);
                        UpdateProgress(80);
                        ViewerAppendText(response);
                        UpdateProgress(100);
                    }
                    catch (Exception ex)
                    {
                        PopupException(ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolStripButtonCopyText_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(richTextBoxExEventLog.Text);
        }

        #endregion

        #region RichTextBoxEx Events

        private void RichTextBoxExEventLog_SelectionChanged(object sender, EventArgs e)
        {
            toolStripStatusLabelStart.Text = string.Format("Start: {0}", richTextBoxExEventLog.SelectionStart);
            toolStripStatusLabelSel.Text = string.Format("Sel: {0}", richTextBoxExEventLog.SelectionLength);
            toolStripStatusLabelLength.Text = string.Format("Length: {0}", richTextBoxExEventLog.TextLength);
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
                TryShowFileContent(files[0], GetFileFormatFromExt(files[0]));
            }
        }

        #endregion


    }
}
