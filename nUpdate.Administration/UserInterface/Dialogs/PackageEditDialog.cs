﻿// Author: Dominic Beger (Trade/ProgTrade)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using nUpdate.Administration.Operations.Panels;
using nUpdate.Administration.UserInterface.Controls;
using nUpdate.Administration.UserInterface.Popups;
using nUpdate.Operations;
using Newtonsoft.Json.Linq;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace nUpdate.Administration.UserInterface.Dialogs
{
    internal partial class PackageEditDialog : BaseDialog
    {
        private bool _allowCancel = true;
        private bool _commandsExecuted;
        private bool _configurationUploaded;
        private bool _requirementEditMode;
        private string _newPackageDirectory;
        private string _oldPackageDirectoryPath;
        private UpdatePackage _packageConfiguration;
        private UpdateVersion _newVersion;

        private readonly TreeNode _createRegistrySubKeyNode = new TreeNode("Create registry subkey", 14, 14)
        {
            Tag = "CreateRegistrySubKey"
        };

        private readonly List<CultureInfo> _cultures = new List<CultureInfo>();
        private readonly TreeNode _deleteNode = new TreeNode("Delete file", 9, 9) { Tag = "DeleteFile" };
        private readonly TreeNode _deleteRegistrySubKeyNode = new TreeNode("Delete registry subkey", 12, 12)
        {
            Tag = "DeleteRegistrySubKey"
        };

        private readonly TreeNode _deleteRegistryValueNode = new TreeNode("Delete registry value", 12, 12)
        {
            Tag = "DeleteRegistryValue"
        };

        private TransferManager _ftp;
        private readonly TreeNode _renameNode = new TreeNode("Rename file", 10, 10) { Tag = "RenameFile" };
        private readonly TreeNode _setRegistryValueNode = new TreeNode("Set registry value", 13, 13)
        {
            Tag = "SetRegistryValue"
        };

        private readonly TreeNode _startProcessNode = new TreeNode("Start process", 8, 8) { Tag = "StartProcess" };
        private readonly TreeNode _startServiceNode = new TreeNode("Start service", 5, 5) { Tag = "StartService" };
        private readonly TreeNode _stopServiceNode = new TreeNode("Stop service", 6, 6) { Tag = "StopService" };
        private readonly TreeNode _terminateProcessNode = new TreeNode("Terminate process", 7, 7)
        { Tag = "StopProcess" };
        private readonly TreeNode _executeScriptNode = new TreeNode("Execute Script", 15, 15) { Tag = "ExecuteScript" };

        private readonly BindingList<string> _unsupportedVersionLiteralsBindingList = new BindingList<string>();

        private List<UpdateRequirement> _updateRequirements;
        private Dictionary<string, Version> _osVersions;

        public PackageEditDialog()
        {
            InitializeComponent();
        }

        public List<UpdatePackage> PackageData { get; set; }

        public bool IsReleased { get; set; }

        public UpdateVersion PackageVersion { get; set; }
        
        private void PackageEditDialog_Load(object sender, EventArgs e)
        {
            Text = string.Format(Text, PackageVersion, Program.VersionString);
            if (PackageData == null)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while loading the configuration.",
                    "There are no entries available in the configuration.",
                    PopupButtons.Ok);
                Close();
                return;
            }

            // TODO: Rewrite
            /*
            if (PackageData.Any(item => item.Version == PackageVersion.ToString()))
                _packageConfiguration =
                    PackageData.First(item => item.Version == PackageVersion.ToString()).DeepCopy();
            else
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while loading the configuration.",
                "There are no entries available for the current package in the configuration.",
                PopupButtons.Ok);
                Close();
                return;
            }*/

            majorNumericUpDown.Maximum = decimal.MaxValue;
            minorNumericUpDown.Maximum = decimal.MaxValue;
            buildNumericUpDown.Maximum = decimal.MaxValue;
            revisionNumericUpDown.Maximum = decimal.MaxValue;

            majorNumericUpDown.Value = PackageVersion.Major;
            minorNumericUpDown.Value = PackageVersion.Minor;
            buildNumericUpDown.Value = PackageVersion.Build;
            revisionNumericUpDown.Value = PackageVersion.Revision;

            var devStages = Enum.GetValues(typeof(DevelopmentalStage));
            Array.Reverse(devStages);
            developmentalStageComboBox.DataSource = devStages;
            /*developmentalStageComboBox.SelectedIndex =
                developmentalStageComboBox.FindStringExact(PackageVersion.DevelopmentalStage.ToString());
            developmentBuildNumericUpDown.Value = PackageVersion.DevelopmentBuild;
            developmentBuildNumericUpDown.Enabled = (PackageVersion.DevelopmentalStage != DevelopmentalStage.Release);*/
            architectureComboBox.SelectedIndex = (int)_packageConfiguration.Architecture;
            necessaryUpdateCheckBox.Checked = _packageConfiguration.NecessaryUpdate;
            includeIntoStatisticsCheckBox.Enabled = Session.ActiveProject.UseStatistics;
            includeIntoStatisticsCheckBox.Checked = _packageConfiguration.UseStatistics;
            /*foreach (var package in Session.ActiveProject.Packages.Where(package => Equals(new UpdateVersion(package.Version), PackageVersion)))
            {
                descriptionTextBox.Text = package.Description;
            }*/

            unsupportedVersionsListBox.DataSource = _unsupportedVersionLiteralsBindingList;
            var cultureInfos = CultureInfo.GetCultures(CultureTypes.AllCultures).ToList();
            foreach (var info in cultureInfos)
            {
                changelogLanguageComboBox.Items.Add($"{info.EnglishName} - {info.Name}");
                _cultures.Add(info);
            }

            changelogContentTabControl.TabPages[0].Tag = _cultures.Where(x => x.Name == "en");
            changelogLanguageComboBox.SelectedIndex = changelogLanguageComboBox.FindStringExact("English - en");

            foreach (var changelogDictionaryEntry in _packageConfiguration.Changelog)
            {
                var culture = changelogDictionaryEntry.Key;
                if (culture.Name != "en")
                {
                    var page = new TabPage("Changelog")
                    {
                        BackColor = SystemColors.Window,
                        Tag = culture
                    };
                    page.Controls.Add(new ChangelogPanel { Changelog = changelogDictionaryEntry.Value });
                    changelogContentTabControl.TabPages.Add(page);
                }
                else
                {
                    englishChangelogTextBox.Text = changelogDictionaryEntry.Value;
                }
            }

            categoryTreeView.SelectedNode = categoryTreeView.Nodes[0];
            if (_packageConfiguration.UnsupportedVersions != null &&
                _packageConfiguration.UnsupportedVersions.Length != 0)
            {
                someVersionsRadioButton.Checked = true;
                unsupportedVersionsPanel.Enabled = true;
                foreach (var unsupportedVersionLiteral in _packageConfiguration.UnsupportedVersions)
                {
                    _unsupportedVersionLiteralsBindingList.Add(unsupportedVersionLiteral);
                }
            }
            else
            {
                unsupportedVersionsPanel.Enabled = false;
            }

            foreach (var operation in _packageConfiguration.Operations)
            {
                switch (Operation.GetOperationTag(operation))
                {
                    case "DeleteFile":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_deleteNode.Clone());

                        var deletePage = new TabPage("Delete file") { BackColor = SystemColors.Window };
                        deletePage.Controls.Add(new FileDeleteOperationPanel
                        {
                            Path = operation.Value,
                            ItemList =
                                new BindingList<string>(((JArray)operation.Value2).ToObject<BindingList<string>>())
                        });
                        categoryTabControl.TabPages.Add(deletePage);
                        break;

                    case "RenameFile":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_renameNode.Clone());

                        var renamePage = new TabPage("Rename file") { BackColor = SystemColors.Window };
                        renamePage.Controls.Add(new FileRenameOperationPanel
                        {
                            Path = operation.Value,
                            NewName = operation.Value2.ToString()
                        });
                        categoryTabControl.TabPages.Add(renamePage);
                        break;

                    case "CreateRegistrySubKey":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_createRegistrySubKeyNode.Clone());

                        var createRegistrySubKeyPage = new TabPage("Create registry subkey")
                        {
                            BackColor = SystemColors.Window
                        };
                        createRegistrySubKeyPage.Controls.Add(new RegistrySubKeyCreateOperationPanel
                        {
                            KeyPath = operation.Value,
                            ItemList =
                                new BindingList<string>(((JArray)operation.Value2).ToObject<BindingList<string>>())
                        });
                        categoryTabControl.TabPages.Add(createRegistrySubKeyPage);
                        break;

                    case "DeleteRegistrySubKey":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_deleteRegistrySubKeyNode.Clone());

                        var deleteRegistrySubKeyPage = new TabPage("Delete registry subkey")
                        {
                            BackColor = SystemColors.Window
                        };
                        deleteRegistrySubKeyPage.Controls.Add(new RegistrySubKeyDeleteOperationPanel
                        {
                            KeyPath = operation.Value,
                            ItemList =
                                new BindingList<string>(((JArray)operation.Value2).ToObject<BindingList<string>>())
                        });
                        categoryTabControl.TabPages.Add(deleteRegistrySubKeyPage);
                        break;

                    case "SetRegistryValue":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_setRegistryValueNode.Clone());

                        var setRegistryValuePage = new TabPage("Set registry value")
                        {
                            BackColor = SystemColors.Window
                        };
                        setRegistryValuePage.Controls.Add(new RegistrySetValueOperationPanel
                        {
                            KeyPath = operation.Value,
                            NameValuePairs =
                                ((JArray)operation.Value2).ToObject<List<Tuple<string, object, RegistryValueKind>>>()
                        });
                        categoryTabControl.TabPages.Add(setRegistryValuePage);
                        break;

                    case "DeleteRegistryValue":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_deleteRegistryValueNode.Clone());

                        var deleteRegistryValuePage = new TabPage("Delete registry value")
                        {
                            BackColor = SystemColors.Window
                        };
                        deleteRegistryValuePage.Controls.Add(new RegistryDeleteValueOperationPanel
                        {
                            KeyPath = operation.Value,
                            ItemList = (operation.Value2 as JObject).ToObject<BindingList<string>>()
                        });
                        categoryTabControl.TabPages.Add(deleteRegistryValuePage);
                        break;

                    case "StartProcess":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_startProcessNode.Clone());

                        var startProcessPage = new TabPage("Start process") { BackColor = SystemColors.Window };
                        startProcessPage.Controls.Add(new ProcessStartOperationPanel
                        {
                            Path = operation.Value,
                            Arguments = ((JArray)operation.Value2).ToObject<BindingList<string>>()
                        });
                        categoryTabControl.TabPages.Add(startProcessPage);
                        break;

                    case "TerminateProcess":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_terminateProcessNode.Clone());

                        var terminateProcessPage = new TabPage("Terminate process") { BackColor = SystemColors.Window };
                        terminateProcessPage.Controls.Add(new ProcessStopOperationPanel
                        {
                            ProcessName = operation.Value
                        });
                        categoryTabControl.TabPages.Add(terminateProcessPage);
                        break;

                    case "StartService":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_startServiceNode.Clone());

                        var startServicePage = new TabPage("Start service") { BackColor = SystemColors.Window };
                        startServicePage.Controls.Add(new ServiceStartOperationPanel
                        {
                            ServiceName = operation.Value
                        });
                        categoryTabControl.TabPages.Add(startServicePage);
                        break;

                    case "StopService":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_stopServiceNode.Clone());

                        var stopServicePage = new TabPage("Stop service") { BackColor = SystemColors.Window };
                        stopServicePage.Controls.Add(new ServiceStopOperationPanel
                        {
                            ServiceName = operation.Value
                        });
                        categoryTabControl.TabPages.Add(stopServicePage);
                        break;

                    case "ExecuteScript":
                        categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_executeScriptNode.Clone());

                        var executeScriptPage = new TabPage("Execute script") { BackColor = SystemColors.Window };
                        executeScriptPage.Controls.Add(new ScriptExecuteOperationPanel
                        {
                            Code = operation.Value
                        });
                        categoryTabControl.TabPages.Add(executeScriptPage);
                        break;
                }
            }

            _updateRequirements = new List<UpdateRequirement>(_packageConfiguration.UpdateRequirements);
            requirementsListBox.Items.AddRange(_updateRequirements.ToArray());

            _osVersions = new Dictionary<string, Version>();
            _osVersions.Add("Windows Vista", new Version("6.0.6000.0"));
            _osVersions.Add("Windows Vista Service Pack 1", new Version("6.0.6001.0"));
            _osVersions.Add("Windows Vista Service Pack 2", new Version("6.0.6002.0"));
            _osVersions.Add("Windows 7", new Version("6.1.7600.0"));
            _osVersions.Add("Windows 7 Service Pack 1", new Version("6.1.7601.0"));
            _osVersions.Add("Windows 8", new Version("6.2.9200.0"));
            _osVersions.Add("Windows 8.1", new Version("6.3.9600.0"));
            _osVersions.Add("Windows 10", new Version("10.0.10240.0"));

            requirementsTypeComboBox.SelectedIndex = 0;
        }

        private void PackageEditDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowCancel)
                e.Cancel = true;
        }

        private void categoryTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (categoryTreeView.SelectedNode.Parent == null) // Check whether the selected node is an operation or not
            {
                switch (categoryTreeView.SelectedNode.Index)
                {
                    case 0:
                        categoryTabControl.SelectedTab = generalTabPage;
                        break;
                    case 1:
                        categoryTabControl.SelectedTab = changelogTabPage;
                        break;
                    case 2:
                        categoryTabControl.SelectedTab = availabilityTabPage;
                        break;
                    case 3:
                        categoryTabControl.SelectedTab = requirementsTabPage;
                        break;
                    case 4:
                        categoryTabControl.SelectedTab = operationsTabPage;
                        break;
                }
            }
            else
            {
                categoryTabControl.SelectedTab =
                    categoryTabControl.TabPages[5 + categoryTreeView.SelectedNode.Index];
            }
        }

        private void someVersionsRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            unsupportedVersionsPanel.Enabled = true;
        }

        private void allVersionsRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            unsupportedVersionsPanel.Enabled = false;
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            /*_newVersion = new UpdateVersion((int)majorNumericUpDown.Value, (int)minorNumericUpDown.Value,
                (int)buildNumericUpDown.Value, (int)revisionNumericUpDown.Value, (DevelopmentalStage)
                    Enum.Parse(typeof(DevelopmentalStage),
                        developmentalStageComboBox.GetItemText(developmentalStageComboBox.SelectedItem)),
                (int)developmentBuildNumericUpDown.Value);
            if (_newVersion.BasicVersion == "0.0.0.0")
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Invalid version set.",
                    "Version \"0.0.0.0\" is not a valid version.", PopupButtons.Ok);
                generalPanel.BringToFront();
                categoryTreeView.SelectedNode = categoryTreeView.Nodes[0];
                return;
            }

            if (Session.ActiveProject.Packages != null && Session.ActiveProject.Packages.Count != 0)
            {
                if (PackageVersion != _newVersion && Session.ActiveProject.Packages.Any(item => new UpdateVersion(item.Version) == _newVersion))
                {
                    Popup.ShowPopup(this, SystemIcons.Error, "Invalid version set.",
                        $"Version \"{_newVersion.Description}\" is already existing.", PopupButtons.Ok);
                    generalPanel.BringToFront();
                    categoryTreeView.SelectedNode = categoryTreeView.Nodes[0];
                    return;
                }
            }*/

            if (string.IsNullOrEmpty(englishChangelogTextBox.Text))
            {
                Popup.ShowPopup(this, SystemIcons.Error, "No changelog set.",
                    "Please specify a changelog for the package. If you have already set a changelog in another language, you still need to specify one for \"English - en\" to support client's that don't use your specified culture on their computer.",
                    PopupButtons.Ok);
                changelogPanel.BringToFront();
                categoryTreeView.SelectedNode = categoryTreeView.Nodes[1];
                return;
            }

            foreach (
                var tabPage in
                    from tabPage in categoryTabControl.TabPages.Cast<TabPage>().Where(item => item.TabIndex > 3)
                    let operationPanel = tabPage.Controls[0] as IOperationPanel
                    where operationPanel != null && !operationPanel.IsValid
                    select tabPage)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "An added operation isn't valid.",
                    "Please make sure to fill out all required fields correctly.",
                    PopupButtons.Ok);
                categoryTreeView.SelectedNode =
                    categoryTreeView.Nodes[4].Nodes.Cast<TreeNode>()
                        .First(item => item.Index == tabPage.TabIndex - 5);
                return;
            }

            var changelog = new Dictionary<CultureInfo, string>
            {
                {new CultureInfo("en"), englishChangelogTextBox.Text}
            };
            foreach (
                var tabPage in
                    changelogContentTabControl.TabPages.Cast<TabPage>().Where(tabPage => tabPage.Text != "English"))
            {
                var panel = (ChangelogPanel)tabPage.Controls[0];
                if (string.IsNullOrEmpty(panel.Changelog))
                    continue;
                changelog.Add((CultureInfo)tabPage.Tag, panel.Changelog);
            }

            _packageConfiguration.NecessaryUpdate = necessaryUpdateCheckBox.Checked;
            _packageConfiguration.Architecture = (Architecture)architectureComboBox.SelectedIndex;
            _packageConfiguration.Changelog = changelog;
            _packageConfiguration.UpdateRequirements = _updateRequirements;

            if (unsupportedVersionsListBox.Items.Count == 0)
                allVersionsRadioButton.Checked = true;
            else if (unsupportedVersionsListBox.Items.Count > 0 && someVersionsRadioButton.Checked)
            {
                _packageConfiguration.UnsupportedVersions =
                    unsupportedVersionsListBox.Items.Cast<string>().ToArray();
            }

            _packageConfiguration.Operations.Clear();
            foreach (var operationPanel in from TreeNode node in categoryTreeView.Nodes[4].Nodes
                                           select (IOperationPanel)categoryTabControl.TabPages[5 + node.Index].Controls[0])
            {
                _packageConfiguration.Operations.Add(operationPanel.Operation);
            }

            _packageConfiguration.UseStatistics = includeIntoStatisticsCheckBox.Checked;

            string[] unsupportedVersionLiterals = null;

            if (unsupportedVersionsListBox.Items.Count == 0)
                allVersionsRadioButton.Checked = true;
            else if (unsupportedVersionsListBox.Items.Count > 0 && someVersionsRadioButton.Checked)
            {
                unsupportedVersionLiterals = _unsupportedVersionLiteralsBindingList.ToArray();
            }

            _packageConfiguration.UnsupportedVersions = unsupportedVersionLiterals;
            /*_packageConfiguration.Version = _newVersion.ToString();
            _packageConfiguration.UpdatePackageUri = new Uri(
                $"{new Uri(Session.ActiveProject.UpdateDirectoryUri, _packageConfiguration.Version)}/{Session.ActiveProject.Guid}.zip");*/

            _newPackageDirectory = Path.Combine(FilePathProvider.Path, "Projects", Session.ActiveProject.Name,
                _newVersion.ToString());

            if (PackageVersion != _newVersion)
            {
                _oldPackageDirectoryPath = Path.Combine(FilePathProvider.Path, "Projects", Session.ActiveProject.Name,
                    PackageVersion.ToString());
                try
                {
                    Directory.Move(_oldPackageDirectoryPath, _newPackageDirectory);
                }
                catch (Exception ex)
                {
                    Popup.ShowPopup(this, SystemIcons.Error,
                        "Error while changing the version of the package directory.", ex,
                        PopupButtons.Ok);
                    return;
                }
            }

            /*PackageData[
                PackageData.IndexOf(
                    PackageData.First(item => item.Version == PackageVersion.ToString()))] =
                _packageConfiguration;*/
            var configurationFilePath = Path.Combine(_newPackageDirectory, "updates.json");
            try
            {
                File.WriteAllText(configurationFilePath, Serializer.Serialize(PackageData));
            }
            catch (Exception ex)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while saving the new configuration.", ex,
                    PopupButtons.Ok);
                return;
            }

            loadingPanel.Location = new Point(180, 91);
            loadingPanel.BringToFront();

            if (IsReleased)
                InitializePackage();
            else
                DialogResult = DialogResult.OK;
        }

        private async void InitializePackage()
        {
            await Task.Factory.StartNew(() =>
            {
                DisableControls(true);
                Invoke(new Action(() => loadingLabel.Text = "Uploading new configuration..."));

                try
                {
                    //_ftp.UploadFile(Path.Combine(_newPackageDirectory, "updates.json"));
                    //if (_newVersion != PackageVersion)
                        //_ftp.RenameDirectory(PackageVersion.ToString(), _packageConfiguration.LiteralVersion);
                    _configurationUploaded = true;
                }
                catch (Exception ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "Error while uploading the new configuration.",
                                    ex, PopupButtons.Ok)));
                    return;
                }

                try
                {
                    string description = null;
                    /*Invoke(new Action(() => description = descriptionTextBox.Text));
                        Session.ActiveProject.Packages.First(item => Equals(new UpdateVersion(item.Version), PackageVersion)).
                        Description = description;
                    if (_newVersion != PackageVersion)
                    {
                            Session.ActiveProject.Packages.First(item => item.Version == PackageVersion.ToString())
                            .Version = _packageConfiguration.Version;
                    }*/

                        Session.ActiveProject.Save();
                }
                catch (Exception ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "Error while saving the project.",
                                    ex, PopupButtons.Ok)));
                    return;
                }

                EnableControls();
                DialogResult = DialogResult.OK;
            });
        }

        private void categoryTreeView_DragDrop(object sender, DragEventArgs e)
        {
            var nodeToDropIn = categoryTreeView.GetNodeAt(categoryTreeView.PointToClient(new Point(e.X, e.Y)));
            if (nodeToDropIn == null || nodeToDropIn.Index != 4) // Operations-node
                return;

            var data = e.Data.GetData(typeof(string));
            if (data == null)
                return;

            switch (data.ToString())
            {
                case "DeleteFile":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_deleteNode.Clone());

                    var deletePage = new TabPage("Delete file") { BackColor = SystemColors.Window };
                    deletePage.Controls.Add(new FileDeleteOperationPanel());
                    categoryTabControl.TabPages.Add(deletePage);
                    break;

                case "RenameFile":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_renameNode.Clone());

                    var renamePage = new TabPage("Rename file") { BackColor = SystemColors.Window };
                    renamePage.Controls.Add(new FileRenameOperationPanel());
                    categoryTabControl.TabPages.Add(renamePage);
                    break;

                case "CreateRegistrySubKey":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_createRegistrySubKeyNode.Clone());

                    var createRegistryEntryPage = new TabPage("Create registry entry") { BackColor = SystemColors.Window };
                    createRegistryEntryPage.Controls.Add(new RegistrySubKeyCreateOperationPanel());
                    categoryTabControl.TabPages.Add(createRegistryEntryPage);
                    break;

                case "DeleteRegistrySubKey":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_deleteRegistrySubKeyNode.Clone());

                    var deleteRegistryEntryPage = new TabPage("Delete registry entry") { BackColor = SystemColors.Window };
                    deleteRegistryEntryPage.Controls.Add(new RegistrySubKeyDeleteOperationPanel());
                    categoryTabControl.TabPages.Add(deleteRegistryEntryPage);
                    break;

                case "SetRegistryValue":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_setRegistryValueNode.Clone());

                    var setRegistryEntryValuePage = new TabPage("Set registry entry value")
                    {
                        BackColor = SystemColors.Window
                    };
                    setRegistryEntryValuePage.Controls.Add(new RegistrySetValueOperationPanel());
                    categoryTabControl.TabPages.Add(setRegistryEntryValuePage);
                    break;
                case "DeleteRegistryValue":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_deleteRegistryValueNode.Clone());

                    var deleteRegistryEntryValuePage = new TabPage("Delete registry entry value")
                    {
                        BackColor = SystemColors.Window
                    };
                    deleteRegistryEntryValuePage.Controls.Add(new RegistryDeleteValueOperationPanel());
                    categoryTabControl.TabPages.Add(deleteRegistryEntryValuePage);
                    break;
                case "StartProcess":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_startProcessNode.Clone());
                
                    var startProcessPage = new TabPage("Start process") { BackColor = SystemColors.Window };
                    startProcessPage.Controls.Add(new ProcessStartOperationPanel());
                    categoryTabControl.TabPages.Add(startProcessPage);
                    break;
                case "TerminateProcess":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_terminateProcessNode.Clone());

                    var terminateProcessPage = new TabPage("Terminate process") { BackColor = SystemColors.Window };
                    terminateProcessPage.Controls.Add(new ProcessStopOperationPanel());
                    categoryTabControl.TabPages.Add(terminateProcessPage);
                    break;
                case "StartService":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_startServiceNode.Clone());

                    var startServicePage = new TabPage("Start service") { BackColor = SystemColors.Window };
                    startServicePage.Controls.Add(new ServiceStartOperationPanel());
                    categoryTabControl.TabPages.Add(startServicePage);
                    break;
                case "StopService":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_stopServiceNode.Clone());

                    var stopServicePage = new TabPage("Stop service") { BackColor = SystemColors.Window };
                    stopServicePage.Controls.Add(new ServiceStopOperationPanel());
                    categoryTabControl.TabPages.Add(stopServicePage);
                    break;
                case "ExecuteScript":
                    categoryTreeView.Nodes[4].Nodes.Add((TreeNode)_executeScriptNode.Clone());

                    var executeScriptPage = new TabPage("Execute script") { BackColor = SystemColors.Window };
                    executeScriptPage.Controls.Add(new ScriptExecuteOperationPanel());
                    categoryTabControl.TabPages.Add(executeScriptPage);
                    break;
            }

            categoryTreeView.Nodes[0].Toggle();
        }

        private void categoryTreeView_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void operationsListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (operationsListView.SelectedItems.Count > 0)
                operationsListView.DoDragDrop(operationsListView.SelectedItems[0].Tag, DragDropEffects.Move);
        }

        private void operationsListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void developmentalStageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            developmentBuildNumericUpDown.Enabled = developmentalStageComboBox.SelectedIndex != 3;
        }

        private void changelogLanguageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (changelogLanguageComboBox.SelectedIndex == changelogLanguageComboBox.FindStringExact("English - en"))
            {
                changelogContentTabControl.SelectTab(changelogContentTabControl.TabPages[0]);
                return;
            }

            if (
                changelogContentTabControl.TabPages.Cast<TabPage>()
                    .Any(item => item.Tag.Equals(_cultures[changelogLanguageComboBox.SelectedIndex])))
            {
                var aimPage = changelogContentTabControl.TabPages.Cast<TabPage>()
                    .First(item => item.Tag.Equals(_cultures[changelogLanguageComboBox.SelectedIndex]));
                changelogContentTabControl.SelectTab(aimPage);
            }
            else
            {
                var page = new TabPage("Changelog")
                {
                    BackColor = SystemColors.Window,
                    Tag = _cultures[changelogLanguageComboBox.SelectedIndex]
                };
                page.Controls.Add(new ChangelogPanel());
                changelogContentTabControl.TabPages.Add(page);
                changelogContentTabControl.SelectTab(page);
            }
        }

        private void changelogLoadButton_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.SupportMultiDottedExtensions = false;
                ofd.Multiselect = false;

                ofd.Filter = "Textdocument (*.txt)|*.txt|RTF-Document (*.rtf)|*.rtf";

                if (ofd.ShowDialog() == DialogResult.OK)
                    englishChangelogTextBox.Text = File.ReadAllText(ofd.FileName, Encoding.Default);
            }
        }

        private void changelogClearButton_Click(object sender, EventArgs e)
        {
            if (changelogLanguageComboBox.SelectedIndex == changelogLanguageComboBox.FindStringExact("English - en"))
            {
                ((TextBox)changelogContentTabControl.SelectedTab.Controls[0]).Clear();
            }
            else
            {
                var currentChangelogPanel = (ChangelogPanel)changelogContentTabControl.SelectedTab.Controls[0];
                ((TextBox)currentChangelogPanel.Controls[0]).Clear();
            }
        }

        private void addVersionButton_Click(object sender, EventArgs e)
        {
            if (
                unsupportedMajorNumericUpDown.Value == 0 && unsupportedMinorNumericUpDown.Value == 0 &&
                unsupportedBuildNumericUpDown.Value == 0 && unsupportedRevisionNumericUpDown.Value == 0)
            {
                Popup.ShowPopup(this, SystemIcons.Warning, "Invalid version.",
                    "You can't add version \"0.0.0.0\" to the unsupported versions. Please specify a minimum version of \"0.1.0.0\"",
                    PopupButtons.Ok);
                return;
            }

            /*var version = new UpdateVersion((int)unsupportedMajorNumericUpDown.Value,
                (int)unsupportedMinorNumericUpDown.Value, (int)unsupportedBuildNumericUpDown.Value,
                (int)unsupportedRevisionNumericUpDown.Value);
            _unsupportedVersionLiteralsBindingList.Add(version.ToString());*/
        }

        private void removeVersionButton_Click(object sender, EventArgs e)
        {
            _unsupportedVersionLiteralsBindingList.RemoveAt(unsupportedVersionsListBox.SelectedIndex);
        }

        private void categoryTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (categoryTreeView.SelectedNode == null)
                return;

            if (e.Control && e.KeyCode == Keys.Up)
                categoryTreeView.SelectedNode.MoveUp();
            else if (e.Control && e.KeyCode == Keys.Down)
                categoryTreeView.SelectedNode.MoveDown();

            if ((e.KeyCode != Keys.Delete && e.KeyCode != Keys.Back) || categoryTreeView.SelectedNode.Parent == null)
                return;

            categoryTabControl.TabPages.Remove(
                categoryTabControl.TabPages[5 + categoryTreeView.SelectedNode.Index]);
            categoryTreeView.SelectedNode.Remove();
        }

        private void bulletToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("•");
            }
            else
            {
                englishChangelogTextBox.Paste("•");
            }
        }

        private void insideQuotationMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("» «");
            }
            else
            {
                englishChangelogTextBox.Paste("» «");
            }
        }

        private void classicQuotationMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("„ “");
            }
            else
            {
                englishChangelogTextBox.Paste("„  “");
            }
        }

        private void outsideQuotationMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("« »");
            }
            else
            {
                englishChangelogTextBox.Paste("« »");
            }
        }

        private void apostropheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("'");
            }
            else
            {
                englishChangelogTextBox.Paste("'");
            }
        }

        private void copyrightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("©");
            }
            else
            {
                englishChangelogTextBox.Paste("©");
            }
        }

        private void allRightsReservedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("®");
            }
            else
            {
                englishChangelogTextBox.Paste("®");
            }
        }

        private void soundRecordingCopyrightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("℗");
            }
            else
            {
                englishChangelogTextBox.Paste("℗");
            }
        }

        private void unregisteredTrademarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("™");
            }
            else
            {
                englishChangelogTextBox.Paste("™");
            }
        }

        private void serviceMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel)page.Controls[0];
                panel.Paste("℠");
            }
            else
            {
                englishChangelogTextBox.Paste("℠");
            }
        }

        private void englishChangelogTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control & e.KeyCode == Keys.A)
                englishChangelogTextBox.SelectAll();
            else if (e.Control & e.KeyCode == Keys.Back)
                SendKeys.SendWait("^+{LEFT}{BACKSPACE}");
        }

        private void requirementsTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            requirementsTypeTabControl.SelectedIndex = requirementsTypeComboBox.SelectedIndex;
        }

        private void addRequirementButton_Click(object sender, EventArgs e)
        {
            UpdateRequirement _updateRequirement = null;
            switch (requirementsTypeComboBox.SelectedIndex)
            {
                case 0:
                    _updateRequirement = new UpdateRequirement(
                        UpdateRequirementType.OSVersion,
                        _osVersions[requiredOSComboBox.Text]);
                    break;

                case 1:
                    _updateRequirement = new UpdateRequirement(
                            UpdateRequirementType.DotNetFramework,
                            Version.Parse(requiredFrameworkComboBox.Text.Replace(".NET Framework ", "")));
                    break;
                default:
                    _updateRequirement = null;
                    break;
            }
            if (_updateRequirement != null)
            {
                _updateRequirements.Add(_updateRequirement);
                requirementsListBox.Items.Remove((UpdateRequirement)requirementsListBox.SelectedItem);
                requirementsListBox.Items.Add(_updateRequirement);
            }

            if (_requirementEditMode)
            {
                _requirementEditMode = false;
                requiredFrameworkComboBox.SelectedIndex = 0;
                requiredOSComboBox.SelectedIndex = 0;
                addRequirementButton.Text = "Add Requirement";
            }
            
        }

        private void requirementsListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (requirementsListBox.SelectedItem == null)
                return;
            UpdateRequirement requirement = (UpdateRequirement)requirementsListBox.SelectedItem;
            

            if (requirement.Type == UpdateRequirementType.DotNetFramework)
            {
                requirementsTypeComboBox.SelectedIndex = 1;
                requiredFrameworkComboBox.SelectedIndex = requiredFrameworkComboBox.Items.IndexOf(".NET Framework " + requirement.Version.ToString(3));
            }
            else
            {
                requirementsTypeComboBox.SelectedIndex = 0;
                requiredOSComboBox.SelectedIndex = requiredOSComboBox.Items.IndexOf(_osVersions.First(s => s.Value == requirement.Version).Key);
            }

            
            _requirementEditMode = true;
            addRequirementButton.Text = "Edit Requirement";
        }

        private void requirementRemoveButton_Click(object sender, EventArgs e)
        {
            _updateRequirements.Remove((UpdateRequirement)requirementsListBox.SelectedItem);
            requirementsListBox.Items.Remove((UpdateRequirement)requirementsListBox.SelectedItem);
        }
    }
}