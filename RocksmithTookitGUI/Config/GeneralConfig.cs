﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Ookii.Dialogs;
using RocksmithToolkitLib;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.DLCPackage;

namespace RocksmithToolkitGUI.Config
{
    public partial class GeneralConfig : UserControl
    {
        private const string MESSAGEBOX_CAPTION = "General Config";
        private bool loading = false;

        public GeneralConfig()
        {
            InitializeComponent();
            loading = true;
            try {
                SetupStoredConfigValues();
                PopulateAppIdCombo(general_defaultappid_RS2012, GameVersion.RS2012);
                PopulateAppIdCombo(general_defaultappid_RS2014, GameVersion.RS2014);
                PopulateEnumCombo(general_defaultgameversion, typeof(GameVersion));
                PopulateEnumCombo(converter_source, typeof(GamePlatform));
                PopulateEnumCombo(converter_target, typeof(GamePlatform));
                PopulateRampUp();
            } catch { /*For mono compatibility*/ }
            loading = false;
        }

        private void SetupStoredConfigValues() {
            general_usebeta.Checked = ConfigRepository.Instance().GetBoolean(general_usebeta.Name);
            general_defaultauthor.Text = ConfigRepository.Instance()[general_defaultauthor.Name];
            general_rs1path.Text = ConfigRepository.Instance()[general_rs1path.Name];
            general_rs2014path.Text = ConfigRepository.Instance()[general_rs2014path.Name];
            creator_structured.Checked = ConfigRepository.Instance().GetBoolean(creator_structured.Name);
            creator_scrollspeed.Value = ConfigRepository.Instance().GetDecimal(creator_scrollspeed.Name);
            ddc_phraselength.Value = ConfigRepository.Instance().GetDecimal(ddc_phraselength.Name);
            ddc_removesustain.Checked = ConfigRepository.Instance().GetBoolean(ddc_removesustain.Name);
            creator_useacronyms.Checked = ConfigRepository.Instance().GetBoolean(creator_useacronyms.Name);
        }

        private void PopulateAppIdCombo(ComboBox combo, GameVersion gameVersion) {
            var appIdList = SongAppIdRepository.Instance().Select(gameVersion).ToArray();
            combo.DataSource = appIdList;
            combo.DisplayMember = "DisplayName";
            combo.ValueMember = "AppId";
            combo.SelectedValue = ConfigRepository.Instance()[combo.Name];
        }

        private void PopulateEnumCombo(ComboBox combo, Type typeEnum) {
            var enumList = Enum.GetNames(typeEnum).ToList<string>();
            enumList.Remove("None");
            combo.DataSource = enumList;
            combo.SelectedItem = ConfigRepository.Instance()[combo.Name];
        }

        private void PopulateRampUp() {
            if (Directory.Exists(@".\ddc\")) {
                ddc_rampup.Items.Clear();
                foreach (var xml in Directory.EnumerateFiles(@".\ddc\", "*.xml", SearchOption.AllDirectories)) {
                    var name = Path.GetFileNameWithoutExtension(xml);
                    if (name.StartsWith("user_"))
                        name = name.Substring(5, name.Length - 5);
                    ddc_rampup.Items.Add(name);

                    var storedRampup = ConfigRepository.Instance()[ddc_rampup.Name];
                    ddc_rampup.SelectedItem = storedRampup;
                }
            }
        }

        private void ConfigurationChanged(object sender, EventArgs e) {
            if (!loading) {
                Control control = (Control)sender;
                var key = control.Name;
                var value = control.Text;

                if (control is ComboBox) {
                    var combo = ((ComboBox)control);
                    if (combo.SelectedValue != null)
                        value = combo.SelectedValue.ToString();
                } else if (control is CheckBox) {
                    value = ((CheckBox)control).Checked.ToString();
                } else if (control is NumericUpDown) {
                    value = ((NumericUpDown)control).Value.ToString();
                }

                if (ConfigRepository.Instance().ValueChanged(key, value) && !String.IsNullOrEmpty(value))
                    ConfigRepository.Instance()[key] = value;
            }
        }

        private void closeConfigButton_Click(object sender, EventArgs e) {
            ((MainForm)ParentForm).EnableConfig = false;
        }

        private void rs1PathButton_Click(object sender, EventArgs e)
        {
            using (var fbd = new VistaFolderBrowserDialog())
            {
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;
                var rs1Path = fbd.SelectedPath;
                general_rs1path.Text = rs1Path;
                ConfigRepository.Instance()[general_rs1path.Name] = rs1Path;
            }
        }

        private void rs2014PathButton_Click(object sender, EventArgs e)
        {
            using (var fbd = new VistaFolderBrowserDialog())
            {
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;
                var rs2014Path = fbd.SelectedPath;
                general_rs2014path.Text = rs2014Path;
                ConfigRepository.Instance()[general_rs2014path.Name] = rs2014Path;
            }
        }
    }
}
