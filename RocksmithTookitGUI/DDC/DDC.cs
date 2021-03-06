﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Ookii.Dialogs;
using System.Diagnostics;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.DLCPackage;
using RocksmithToolkitLib.DLCPackage.Manifest;
using RocksmithToolkitLib.Xml;

namespace RocksmithToolkitGUI.DDC
{
    public partial class DDC : UserControl
    {
        private const string MESSAGEBOX_CAPTION = "Dynamic Difficulty Creator";

        internal BackgroundWorker bw = new BackgroundWorker();
        // 0 - fpath 1 - name
        internal Dictionary<string, string> DLCdb = new Dictionary<string,string>();
        internal Dictionary<string, string> RampMdlsDb = new Dictionary<string,string>();
        internal static string AppWD = Application.StartupPath;
        internal Color EnabledColor = System.Drawing.Color.Green;
        internal Color DisabledColor = Color.Tomato;

        internal bool isNDD { get; set; }

        internal bool CleanProcess {
            get {
                return cleanCheckbox.Checked;
            }
            set {
                cleanCheckbox.Checked = value;
            }
        }

        public bool KeepLog {
            get {
                return keepLogfile.Checked;
            }
            set {
                keepLogfile.Checked = value;
            }
        }

        internal string processOutput { get; set; }

        public DDC()
        {
            InitializeComponent();
            this.bw.DoWork += new DoWorkEventHandler(bw_DoWork);
            this.bw.ProgressChanged += new ProgressChangedEventHandler(bw_ProgressChanged);
            this.bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_Completed);
            this.bw.WorkerReportsProgress = true;
        }

        private void DDC_Load(object sender, EventArgs e)
        {
            if (!MainForm.IsInDesignMode)
            {
                FileVersionInfo vi = FileVersionInfo.GetVersionInfo(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ddc", "ddc.exe"));
                ddcVersion.Text = String.Format("v{0}", vi.ProductVersion);
            }

            try {
                PopMDLs();
                SetDefaultFromConfig();
            } catch { /*For mono compatibility*/ }
        }

        private void SetDefaultFromConfig() {
            ramUpMdlsCbox.SelectedItem = ConfigRepository.Instance()["ddc_rampup"];
            phaseLenNum.Value = ConfigRepository.Instance().GetDecimal("ddc_phraselength");
            delsustainsBT.Checked = ConfigRepository.Instance().GetBoolean("ddc_removesustain");
        }

        private void bw_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            DDprogress.Value = 100;
            if (e.Result.Equals(0))
            {
                foreach (var file in DLCdb)
                {
                    switch (Path.GetExtension(file.Value))
                    {
                        case ".xml":   // Arrangement
                            {
                                string filePath = Path.GetDirectoryName(file.Value),
                                ddcArrXML = Path.Combine(filePath, String.Format("DDC_{0}.xml", file.Key)),
                                srcShowlights = Path.Combine(filePath, String.Format("{0}_showlights.xml", file.Key)),
                                destShowlights = Path.Combine(filePath, String.Format("DDC_{0}_showlights.xml", file.Key));

                                if (!CleanProcess && !File.Exists(destShowlights) && File.Exists(srcShowlights) && File.Exists(ddcArrXML))
                                    File.Copy(srcShowlights, destShowlights, true);
                            }
                            break;
                        case ".psarc": // PC / Mac (RS2014)
                        case ".dat":   // PC (RS1)
                        case ".edat":  // PS3
                        case "":       // XBox 360
                            {
                                string filePath = Path.GetDirectoryName(file.Value),
                                newName = String.Format("{0}_{1}{2}", 
                                file.Key.StripPlatformEndName().GetValidName(false).Replace("_DD", "").Replace("_NDD", ""), isNDD ? "_NDD" : "DD", file.Value.GetPlatform().GetPathName()[2]);
                                if (CleanProcess && File.Exists(file.Value) && !Path.GetFileNameWithoutExtension(file.Value).GetValidName(false).Equals(newName))
                                    File.Delete(file.Value);
                            }
                            break;
                    } 

                    Invoke(new MethodInvoker(() => { DelEntry(file.Value); }));
                }

                DLCdb.Clear();
                MessageBox.Show(String.Format("Dynamic difficulty {0}!", isNDD ? "removed" : "generated"), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (e.Result.Equals(1))
                MessageBox.Show("DDC error! System Error. See below: " + Environment.NewLine + processOutput, MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (e.Result.Equals(2)) {
                MessageBox.Show(String.Format("Dynamic difficulty {0} with errors! See below:{1}{2}", Environment.NewLine, isNDD ? "removed" : "generated", processOutput), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            } else
                MessageBox.Show("DDC error! See ddc.log", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);

            ProduceDDbt.Enabled = true;
            DDprogress.Visible = false;
            this.Focus();
        }

        private void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            DDprogress.Value = e.ProgressPercentage;
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            processOutput = String.Empty;

            var step = (int)Math.Round(1.0 / DLCdb.Count() * 100, 0);
            int result = -1, progress = 0;
            string remSUS = String.Empty, rampPath = String.Empty;

            this.Invoke(new MethodInvoker(() => {
                    remSUS = IsREMsus();
                    rampPath = GetRampUpMdl();
            }));

            bw.ReportProgress(progress);
            
            StringBuilder errorsFound = new StringBuilder();
            foreach (var file in DLCdb)
            {
                string consoleOutput = String.Empty;
                switch (Path.GetExtension(file.Value)) {
                    case ".xml":   // Arrangement
                        result = ApplyDD(file.Value, remSUS, rampPath, out consoleOutput, CleanProcess, KeepLog);
                        errorsFound.AppendLine(consoleOutput);
                        break;
                    case ".psarc": // PC / Mac (RS2014)
                    case ".dat":   // PC (RS1)
                    case ".edat":  // PS3
                    case "":       // XBox 360
                        result = ApplyPackageDD(file.Value, remSUS, rampPath, out consoleOutput);
                        errorsFound.AppendLine(consoleOutput);
                        break;
                }                
                if (!String.IsNullOrEmpty(errorsFound.ToString())) {
                    processOutput = errorsFound.ToString();
                }

                progress += step;
                bw.ReportProgress(progress);
            }

            e.Result = result;
        }

        private int ApplyDD(string file, string remSUS, string rampPath, out string consoleOutput, bool cleanProcess = false, bool keepLog = false)
        {
            var startInfo = new ProcessStartInfo();

            startInfo.FileName = Path.Combine(AppWD, "ddc", "ddc.exe");
            startInfo.WorkingDirectory = Path.GetDirectoryName(file);
            startInfo.Arguments = String.Format("\"{0}\" -l {1} -s {2}{3}{4}{5}", 
                                                Path.GetFileName(file),
                                                (UInt16)phaseLenNum.Value, 
                                                remSUS, rampPath, 
                                                cleanProcess ? " -p Y" : " -p N",
                                                keepLog ? " -t Y" : " -t N" 
            );
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;

            using (var DDC = new Process()) {
                DDC.StartInfo = startInfo;
                DDC.Start();
                DDC.WaitForExit(30000000);
                consoleOutput = DDC.StandardOutput.ReadToEnd();

                return DDC.ExitCode;
            }
        }

        private int ApplyPackageDD(string file, string remSUS, string rampPath, out string consoleOutputPkg)
        {
            int singleResult = -1;
            bool exitedByError = false;
            consoleOutputPkg = String.Empty;
            var tmpDir = Path.GetTempPath();
            var platform = file.GetPlatform();
            var unpackedDir = Packer.Unpack(file, tmpDir, false, true, false);

            var xmlFiles = Directory.GetFiles(unpackedDir, "*.xml", SearchOption.AllDirectories);
            foreach (var xml in xmlFiles)
            {
                if (Path.GetFileNameWithoutExtension(xml).ToLower().Contains("vocal"))
                    continue;

                if (Path.GetFileNameWithoutExtension(xml).ToLower().Contains("showlight"))
                    continue;

                singleResult = ApplyDD(xml, remSUS, rampPath, out consoleOutputPkg, true, false);

                // UPDATE MANIFEST (RS2014) for update
                if (platform.version == RocksmithToolkitLib.GameVersion.RS2014) {
                    var json = Directory.GetFiles(unpackedDir, String.Format("*{0}.json", Path.GetFileNameWithoutExtension(xml)), SearchOption.AllDirectories);
                    if (json.Length > 0) {
                        Attributes2014 attr = Manifest2014<Attributes2014>.LoadFromFile(json[0]).Entries.ToArray()[0].Value.ToArray()[0].Value;
                        Song2014 xmlContent = Song2014.LoadFromFile(xml);

                        var manifestFunctions = new ManifestFunctions(platform.version);

                        attr.PhraseIterations = new List<PhraseIteration>();
                        manifestFunctions.GeneratePhraseIterationsData(attr, xmlContent, platform.version);

                        attr.Phrases = new List<Phrase>();
                        manifestFunctions.GeneratePhraseData(attr, xmlContent);

                        attr.Sections = new List<Section>();
                        manifestFunctions.GenerateSectionData(attr, xmlContent);

                        attr.MaxPhraseDifficulty = manifestFunctions.GetMaxDifficulty(xmlContent);

                        var manifest = new Manifest2014<Attributes2014>();
                        var attributeDictionary = new Dictionary<string, Attributes2014> { { "Attributes", attr } };
                        manifest.Entries.Add(attr.PersistentID, attributeDictionary);
                        manifest.SaveToFile(json[0]);                            
                    }
                }

                if (singleResult == 1)
                {
                    exitedByError = true;
                    break;
                }
                else if (singleResult == 2)
                    consoleOutputPkg = String.Format("Arrangement file '{0}' => {1}", Path.GetFileNameWithoutExtension(xml), consoleOutputPkg);
            }
            
            if (!exitedByError)
            {
                var newName = Path.Combine(Path.GetDirectoryName(file), String.Format("{0}_{1}{2}", 
                    Path.GetFileNameWithoutExtension(file).StripPlatformEndName().GetValidName(false).Replace("_DD", "").Replace("_NDD",""), isNDD ? "NDD" :  "DD", platform.GetPathName()[2]));
                Packer.Pack(unpackedDir, newName, true, platform);
                DirectoryExtension.SafeDelete(unpackedDir);
            }
            return singleResult;
        }

        internal void FillDB()
        {
            int i = 0;
            DDCfilesDgw.Rows.Clear();
            foreach (var rowFile in DLCdb)
            {
                if (DDCfilesDgw.Rows.Count <= i && i < DLCdb.Count) DDCfilesDgw.Rows.Add();
                DDCfilesDgw.Rows[i].Cells["PathColnm"].Value = rowFile.Value;
                DDCfilesDgw.Rows[i].Cells["TypeColnm"].Value = Path.GetExtension(rowFile.Value);
                i++;
            }
            DDCfilesDgw.Update();
        }

        private string GetRampUpMdl()
        {
            if (ramUpMdlsCbox.Text.Trim().Length > 0)
                return String.Format(" -m \"{0}\"", Path.GetFullPath(RampMdlsDb[ramUpMdlsCbox.Text]));
            else
                return "";
        }

        private string IsREMsus()
        {
            if (delsustainsBT.Checked)
                return "Y";
            else return "N";
        }

        private void ProduceDDbt_Click(object sender, EventArgs e)
        {
            if (!this.bw.IsBusy && DLCdb.Count > 0)
            {
                if(DLCdb.Count > 1) DDprogress.Visible = true;
                ProduceDDbt.Enabled = false;
                this.bw.RunWorkerAsync(); 
            }
        }

        private void AddArrBT_Click(object sender, EventArgs e)
        {
            using (var ofd = new VistaOpenFileDialog())
            using (var sfd = new VistaFolderBrowserDialog())
            {
                ofd.Filter = "Select Package or Arrangement (*.psarc;*.dat;*.edat;*.xml)|*.psarc;*.dat;*.edat;*.xml|" + "All files|*.*";
                ofd.FilterIndex = 0;
                ofd.Multiselect = true;
                ofd.ReadOnlyChecked = true;
                ofd.CheckFileExists = true;
                ofd.CheckPathExists = true;
                ofd.RestoreDirectory = true;

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                foreach (var file in ofd.FileNames)
                {
                    if (file.EndsWith("_showlights.xml") ||
                        file.EndsWith(".dlc.xml") ||
                        file.StartsWith("DDC_"))
                        continue;

                    if (!DLCdb.ContainsValue(file))
                        DLCdb.Add(Path.GetFileNameWithoutExtension(file), file);
                }
            }

            FillDB();
        }

        private void rampUpBT_Click(object sender, EventArgs e)
        {
            using (var ofd = new VistaOpenFileDialog())
            {
                ofd.Filter = "DDC Ramp-Up model (*.xml)|*.xml";
                ofd.CheckFileExists = true;
                ofd.CheckPathExists = true;
                ofd.Multiselect = true;
                ofd.ReadOnlyChecked = true;
                ofd.RestoreDirectory = true;
                
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                foreach (var file in ofd.FileNames)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    Directory.CreateDirectory(@".\ddc\umdls\");
                    var path = String.Format(@".\ddc\umdls\user_{0}.xml", name);
                    if (!ramUpMdlsCbox.Items.Contains(name))
                    {
                        try { File.Copy(file, path, true); }
                        catch { }
                        ramUpMdlsCbox.Items.Add(name);
                    }
                    ramUpMdlsCbox.SelectedIndex = ramUpMdlsCbox.FindStringExact(name);
                }
            }
        }

        /// <summary>
        /// Optimized for Opera, Google Chrome and Firefox.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DescriptionDDC_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            bool done = false;
            string link = "http://ddcreator.wordpress.com";
            string arg0 = "";
            Process[] processlist = Process.GetProcesses();
            foreach (Process browser in processlist)
            {                
                string[] Browsers = new string[]{ "chrome", "opera", "firefox" };

                foreach (var browserID in Browsers)
                {
                    if (browser.ProcessName.Equals(browserID))
                    {
                        if (browserID.IndexOf("opera") > 0)
                            arg0 = "-newwindow ";

                        browser.StartInfo.FileName = browser.MainModule.FileName;
                        browser.StartInfo.Arguments = String.Format("{0}{1}", arg0, link);
                        browser.Start();
                        done = true;
                        break;
                    }
                }

                if (done)
                    break;
            }

            if (!done)
                Process.Start(link);

            this.DescriptionDDC.Links[DescriptionDDC.Links.IndexOf(e.Link)].Visited = true;
        }

        private void PopMDLs()
        {
            if (Directory.Exists(@".\ddc\"))
            {
                ramUpMdlsCbox.Items.Clear();
                RampMdlsDb.Clear();
                foreach (var mdl in Directory.EnumerateFiles(@".\ddc\", "*.xml", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(mdl);
                    if (name.StartsWith("user_")) name = name.Substring(5, name.Length - 5);
                    ramUpMdlsCbox.Items.Add(name);
                    ramUpMdlsCbox.SelectedIndex = ramUpMdlsCbox.FindStringExact("ddc_default");
                    RampMdlsDb.Add(name, Path.GetFullPath(mdl));
                }
                ramUpMdlsCbox.Refresh();
            }
        }

        private void DelEntry(string path)
        {
            for (int i = DDCfilesDgw.RowCount - 1; i >= 0; i--)
            {
                if (DDCfilesDgw.Rows[i].Cells["PathColnm"].Value.Equals(path))
                { DDCfilesDgw.Rows.RemoveAt(i); return; }
            }
        }

        private void DDCfilesDgw_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
        {
            if (DDCfilesDgw.SelectedRows.Count > 0)
            {
                if (MessageBox.Show("Are you sure to delete the selected file?", MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                    return;

                string file = e.Row.Cells["PathColnm"].Value.ToString();
                string value = Path.GetFileNameWithoutExtension(file);
                if (DLCdb != null)
                    DLCdb.Remove(value);
            }
        }

        private void ramUpMdlsCbox_DropDown(object sender, EventArgs e)
        {
            PopMDLs();
        }

        private void deleteArrBT_Click(object sender, EventArgs e)
        {
            if (DDCfilesDgw.SelectedRows.Count > 0)
            {
                if (MessageBox.Show("Are you sure to delete the selected file?", MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                    return;

                foreach (DataGridViewRow row in DDCfilesDgw.SelectedRows)
                {
                    string file = row.Cells["PathColnm"].Value.ToString();
                    string value = Path.GetFileNameWithoutExtension(file);

                    if (DLCdb != null)
                        DLCdb.Remove(value);
                }

                FillDB();
            }
        }

        private void colorHiglight_CheckStateChanged(object sender, EventArgs e)
        {
            if (cleanCheckbox.Checked) cleanCheckbox.ForeColor = EnabledColor;
            else cleanCheckbox.ForeColor = DisabledColor;

            if (keepLogfile.Checked) keepLogfile.ForeColor = EnabledColor;
            else keepLogfile.ForeColor = DisabledColor;
        }

        private void ramUpMdlsCbox_SelectedIndexChanged(object sender, EventArgs e) {
            isNDD = ((ComboBox)sender).Text.Equals("ddc_dd_remover");
            ProduceDDbt.Text = (isNDD) ? "Remove DD" : "Generate DD";
        }

        private void DDCfilesDgw_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (!(Path.GetExtension(file) == ".xml" ||
                          Path.GetExtension(file) == ".dat" ||
                          Path.GetExtension(file) == ".psarc" ||
                          Path.GetExtension(file) == "" ||
                          Path.GetExtension(file) == ".edat"))
                        continue;

                    if (file.EndsWith("_showlights.xml") ||
                        file.EndsWith(".dlc.xml") ||
                        file.StartsWith("DDC_"))
                        continue;

                    if (!DLCdb.ContainsValue(file))
                        DLCdb.Add(Path.GetFileNameWithoutExtension(file), file);
                }

                FillDB();
            }
        }

        private void DDCfilesDgw_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }
    }
}