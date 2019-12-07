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
using System.Windows.Forms;
using Megamind.IO.FileFormat;


namespace Hex_File_Analyzer
{
    public partial class MainForm : Form
    {
        #region Data

        bool _verifyChesksum;
        int _dataBytesPerRecord;
        string _currentfile = "";
        IntelHex _ihex = new IntelHex();

        #endregion

        #region Internal Methods

        private static bool IsFileFormatSupported(string filename)
        {
            var fileExt = Path.GetExtension(filename);
            var supportedExt = new List<string> { ".hex", ".eep" };
            foreach (var ext in supportedExt)
            {
                if (string.Compare(fileExt, ext, true) == 0) return true;
            }
            return false;
        }

        private static string ToFormatedBytes(long bytes)
        {
            string str;
            if (bytes >= (1024 * 1024)) str = string.Format("{0} [{1:0.00} MB]", bytes, bytes / (1024f * 1024f));
            else if (bytes >= 1024) str = string.Format("{0} [{1:0.00} KB]", bytes, bytes / 1024f);
            else str = string.Format("{0:0000} bytes", bytes); ;
            return str;
        }

        private void TryShowHexInfo(string filename)
        {
            this.UseWaitCursor = true;
            Application.DoEvents();

            try
            {
                if (!File.Exists(filename)) throw new Exception("File not found!");
                if (!IsFileFormatSupported(filename)) throw new Exception("File format not supported!");
                ShowHexInfoGrid(filename);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.UseWaitCursor = false;
            Cursor.Current = Cursors.Default;
            Application.DoEvents();
        }

        private void ShowHexInfoGrid(string filename)
        {
            this.Text = Path.GetFileName(filename) + " - " + Assembly.GetEntryAssembly().GetName().Name;
            Application.DoEvents();

            _ihex = new IntelHex();
            _ihex.Read(filename, _verifyChesksum);
            Application.DoEvents();

            long totalmemused = 0;
            long totalmemblank = 0;
            var blankspaces = new long[_ihex.MemBlocks.Count];
            var sortedBlocks = _ihex.MemBlocks.OrderBy(p => p.Start).ToList();

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
            dataGridViewSummary.Rows.Clear();
            Application.DoEvents();
            foreach (var b in _ihex.MemBlocks)
            {
                count++;
                dataGridViewSummary.Rows.Add(string.Format("{0:00}", count), string.Format("0x{0:X4}", b.Start),
                    string.Format("0x{0:X4}", b.End), ToFormatedBytes(b.Size), ToFormatedBytes(blankspaces[count - 1]));
            }
            dataGridViewSummary.Rows.Add("", "", "Total:", ToFormatedBytes(totalmemused), ToFormatedBytes(totalmemblank));
            dataGridViewSummary.ClearSelection();

            // show records
            count = 0;
            dataGridViewRecord.Rows.Clear();
            Application.DoEvents();
            foreach (var r in _ihex.Records)
            {
                count++;
                dataGridViewRecord.Rows.Add(string.Format("{0:0000}", count), string.Format("{0} [{1}]", r.RecordType, (RecordType)r.RecordType),
                    string.Format("0x{0:X4}", r.Address), string.Format("{0:00}", r.DataCount), BitConverter.ToString(r.DataBlock).Replace("-", ""));
            }
            dataGridViewRecord.ClearSelection();
            toolStrip1.Focus();
            Application.DoEvents();
        }

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
                dataGridViewSummary.Font = new Font("Consolas", 9);
                dataGridViewSummary.ForeColor = Color.Blue;

                dataGridViewRecord.Font = new Font("Consolas", 9);
                dataGridViewRecord.ForeColor = Color.DarkMagenta;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            try
            {
                _verifyChesksum = Convert.ToInt32(ConfigurationManager.AppSettings["VerifyChecksum"]) > 0;
                _dataBytesPerRecord = Convert.ToInt32(ConfigurationManager.AppSettings["DataBytesPerRecord"]);

                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    if (IsFileFormatSupported(args[1]))
                    {
                        _currentfile = args[1];
                        TryShowHexInfo(_currentfile);
                    }
                    else MessageBox.Show("File format not supported.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Menu Strip Events

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
                ofd.Filter = "Hex Files (*.hex)|*.hex|Eep files (*.eep)|*.eep|All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _currentfile = ofd.FileName;
                    TryShowHexInfo(_currentfile);
                }
            }
        }

        private void SaveToolStripButton_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Hex files (*.hex)|*.hex|Eep files (*.eep)|*.eep|Binary files (*.bin)|*.bin|All files (*.*)|*.*";
                sfd.FileName = Path.GetFileName(_currentfile);
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
                        MessageBox.Show(ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ReloadToolStripButton_Click(object sender, EventArgs e)
        {
            TryShowHexInfo(_currentfile);
        }

        private void HelpToolStripButton_Click(object sender, EventArgs e)
        {
            var info = string.Format("{0} {1}\r\nDeveloped by: \r\nGSM Rana \r\ngithub.com/gsmrana", 
                Assembly.GetEntryAssembly().GetName().Name, 
                Assembly.GetEntryAssembly().GetName().Version);
            MessageBox.Show(info, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void GetChksmToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                toolStripTextBoxChecksum.Clear();
                var data = toolStripTextBoxData.Text;
                data = data.Replace("-", "");
                data = data.Replace("0x", "");
                toolStripTextBoxData.Text = data;

                var chksm = IntelHex.CalculateChecksum(data);
                toolStripTextBoxChecksum.Text = chksm.ToString("X2");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region File drag n drop

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (IsFileFormatSupported(files[0]))
                {
                    _currentfile = files[0];
                    TryShowHexInfo(_currentfile);
                }
                else MessageBox.Show("File format not supported.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #endregion
    }
}
