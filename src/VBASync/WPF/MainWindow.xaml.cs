﻿using Ookii.Dialogs.Wpf;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;
using VBASync.Localization;
using VBASync.Model;

namespace VBASync.WPF {
    internal partial class MainWindow {
        internal const int CopyrightYear = 2017;
        internal const string SupportUrl = "https://github.com/chelh/VBASync";

        internal static readonly Version Version = new Version(1, 3, 1);

        private readonly MainViewModel _vm;

        private bool _doUpdateIncludeAll = true;
        private VbaFolder _evf;

        public MainWindow(MainViewModel vm) {
            InitializeComponent();
            DataContext = _vm = vm;
            DataContextChanged += (s, e) => QuietRefreshIfInputsOk();
            _vm.Session.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Action" || e.PropertyName == "FilePath" || e.PropertyName == "FolderPath")
                {
                    QuietRefreshIfInputsOk();
                }
            };
            QuietRefreshIfInputsOk();
        }

        private ISession Session => _vm.Session;

        internal void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(_vm.Settings, s => _vm.Settings = s).ShowDialog();
        }

        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow().ShowDialog();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            FixQuotesEnclosingPaths();
            var vm = ChangesGrid.DataContext as ChangesViewModel;
            if (vm == null) {
                return;
            }
            if (Session.Action == ActionType.Extract) {
                foreach (var p in vm.Where(p => p.Commit).ToArray()) {
                    var fileName = p.ModuleName + ModuleProcessing.ExtensionFromType(p.ModuleType);
                    switch (p.ChangeType) {
                    case ChangeType.DeleteFile:
                        File.Delete(Path.Combine(Session.FolderPath, fileName));
                        if (p.ModuleType == ModuleType.Form) {
                            File.Delete(Path.Combine(Session.FolderPath, p.ModuleName + ".frx"));
                        }
                        break;
                    case ChangeType.ChangeFormControls:
                        File.Copy(Path.Combine(_evf.FolderPath, p.ModuleName + ".frx"), Path.Combine(Session.FolderPath, p.ModuleName + ".frx"), true);
                        break;
                    default:
                        File.Copy(Path.Combine(_evf.FolderPath, fileName), Path.Combine(Session.FolderPath, fileName), true);
                        if (p.ChangeType == ChangeType.AddFile && p.ModuleType == ModuleType.Form) {
                            File.Copy(Path.Combine(_evf.FolderPath, p.ModuleName + ".frx"), Path.Combine(Session.FolderPath, p.ModuleName + ".frx"), true);
                        }
                        break;
                    }
                    vm.Remove(p);
                }
            } else {
                foreach (var p in vm.Where(p => p.Commit).ToArray()) {
                    var fileName = p.ModuleName + ModuleProcessing.ExtensionFromType(p.ModuleType);
                    switch (p.ChangeType) {
                    case ChangeType.DeleteFile:
                        File.Delete(Path.Combine(_evf.FolderPath, fileName));
                        if (p.ModuleType == ModuleType.Form) {
                            File.Delete(Path.Combine(_evf.FolderPath, p.ModuleName + ".frx"));
                        }
                        break;
                    case ChangeType.ChangeFormControls:
                        File.Copy(Path.Combine(Session.FolderPath, p.ModuleName + ".frx"), Path.Combine(_evf.FolderPath, p.ModuleName + ".frx"), true);
                        break;
                    default:
                        File.Copy(Path.Combine(Session.FolderPath, fileName), Path.Combine(_evf.FolderPath, fileName), true);
                        if (p.ChangeType == ChangeType.AddFile && p.ModuleType == ModuleType.Form) {
                            File.Copy(Path.Combine(Session.FolderPath, p.ModuleName + ".frx"), Path.Combine(_evf.FolderPath, p.ModuleName + ".frx"), true);
                        }
                        break;
                    }
                    vm.Remove(p);
                }
                _evf.Write(Session.FilePath);
            }
            UpdateIncludeAllBox();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            Application.Current.Shutdown();
        }

        private void ChangesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var sel = (Patch)ChangesGrid.SelectedItem;
            var fileName = sel.ModuleName + (sel.ChangeType != ChangeType.ChangeFormControls ? ModuleProcessing.ExtensionFromType(sel.ModuleType) : ".frx");
            string oldPath;
            string newPath;
            if (Session.Action == ActionType.Extract) {
                oldPath = Path.Combine(Session.FolderPath, fileName);
                newPath = Path.Combine(_evf.FolderPath, fileName);
            } else {
                oldPath = Path.Combine(_evf.FolderPath, fileName);
                newPath = Path.Combine(Session.FolderPath, fileName);
            }
            if (sel.ChangeType == ChangeType.ChangeFormControls) {
                Lib.FrxFilesAreDifferent(oldPath, newPath, out var explain);
                MessageBox.Show(explain, "FRX file difference", MessageBoxButton.OK, MessageBoxImage.Information);
            } else {
                var diffExePath = Environment.ExpandEnvironmentVariables(_vm.Settings.DiffTool);
                if (!File.Exists(oldPath) || !File.Exists(newPath) || !File.Exists(diffExePath)) {
                    return;
                }
                var p = new Process {
                    StartInfo = new ProcessStartInfo(diffExePath, _vm.Settings.DiffToolParameters.Replace("{OldFile}", oldPath).Replace("{NewFile}", newPath)) {
                        UseShellExecute = false
                    }
                };
                p.Start();
            }
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e) {
            CancelButton_Click(null, null);
        }

        private void FixQuotesEnclosingPaths()
        {
            if (!string.IsNullOrEmpty(_vm.Session.FilePath) && _vm.Session.FilePath.Length > 2 && !File.Exists(_vm.Session.FilePath)
                && _vm.Session.FilePath.StartsWith("\"") && _vm.Session.FilePath.EndsWith("\"")
                && File.Exists(_vm.Session.FilePath.Substring(1, _vm.Session.FilePath.Length - 2)))
            {
                _vm.Session.FilePath = _vm.Session.FilePath.Substring(1, _vm.Session.FilePath.Length - 2);
            }
            if (!string.IsNullOrEmpty(_vm.Session.FolderPath) && _vm.Session.FolderPath.Length > 2 && !Directory.Exists(_vm.Session.FolderPath)
                && _vm.Session.FolderPath.StartsWith("\"") && _vm.Session.FolderPath.EndsWith("\"")
                && (Directory.Exists(_vm.Session.FolderPath.Substring(1, _vm.Session.FolderPath.Length - 2))
                || Path.IsPathRooted(_vm.Session.FolderPath.Substring(1, _vm.Session.FolderPath.Length - 2))))
            {
                _vm.Session.FolderPath = _vm.Session.FolderPath.Substring(1, _vm.Session.FolderPath.Length - 2);
            }
        }

        private void IncludeAllBox_Click(object sender, RoutedEventArgs e)
        {
            var vm = ChangesGrid.DataContext as ChangesViewModel;
            if (vm == null || IncludeAllBox.IsChecked == null) {
                return;
            }
            try {
                _doUpdateIncludeAll = false;
                foreach (var ch in vm) {
                    ch.Commit = IncludeAllBox.IsChecked.Value;
                }
            } finally {
                _doUpdateIncludeAll = true;
            }
            ChangesGrid.Items.Refresh();
        }

        private void LoadLastMenu_Click(object sender, RoutedEventArgs e) {
            _vm.LoadIni(new AppIniFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VBA Sync Tool", "LastSession.ini")));
        }

        private void LoadSessionMenu_Click(object sender, RoutedEventArgs e) {
            var dlg = new VistaOpenFileDialog {
                Filter = $"{VBASyncResources.MWOpenAllFiles}|*.*|"
                    + $"{VBASyncResources.MWOpenSession}|*.ini",
                FilterIndex = 2
            };
            if (dlg.ShowDialog() == true) {
                _vm.LoadIni(new AppIniFile(dlg.FileName, Encoding.UTF8));
                _vm.AddRecentFile(dlg.FileName);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            ApplyButton_Click(null, null);
            Application.Current.Shutdown();
        }

        private void QuietRefreshIfInputsOk() {
            FixQuotesEnclosingPaths();
            if (!File.Exists(Session.FilePath) || !Directory.Exists(Session.FolderPath)) {
                return;
            }
            try {
                RefreshButton_Click(null, null);
            } catch {
                ChangesGrid.DataContext = null;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(Session.FolderPath) || string.IsNullOrEmpty(Session.FilePath)) {
                return;
            }
            FixQuotesEnclosingPaths();
            var folderModules = Lib.GetFolderModules(Session.FolderPath);
            _evf?.Dispose();
            _evf = new VbaFolder();
            _evf.Read(Session.FilePath, folderModules.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Item1));
            var changes = Lib.GetModulePatches(Session, _evf.FolderPath, folderModules, _evf.ModuleTexts.ToList()).ToList();
            var projChange = Lib.GetProjectPatch(Session, _evf.FolderPath);
            if (projChange != null) {
                changes.Add(projChange);
            }
            var cvm = new ChangesViewModel(changes);
            ChangesGrid.DataContext = cvm;
            foreach (var ch in cvm) {
                ch.CommitChanged += (s2, e2) => UpdateIncludeAllBox();
            }
            UpdateIncludeAllBox();
        }

        private void SaveSession(Stream st, bool saveSettings) {
            var sb = new StringBuilder();
            sb.AppendLine("ActionType=" + (Session.Action == ActionType.Extract ? "Extract" : "Publish"));
            sb.AppendLine($"FolderPath=\"{Session.FolderPath}\"");
            sb.AppendLine($"FilePath=\"{Session.FilePath}\"");
            if (saveSettings)
            {
                sb.AppendLine($"Language=\"{_vm.Settings.Language}\"");
                sb.AppendLine("");
                sb.AppendLine("[DiffTool]");
                sb.AppendLine($"Path =\"{_vm.Settings.DiffTool}\"");
                sb.AppendLine($"Parameters=\"{_vm.Settings.DiffToolParameters}\"");
                if (_vm.RecentFiles.Count > 0)
                {
                    sb.AppendLine("");
                    sb.AppendLine("[RecentFiles]");
                }
                var i = 0;
                while (_vm.RecentFiles.Count > i)
                {
                    sb.AppendLine($"{(i+1).ToString(CultureInfo.InvariantCulture)}=\"{_vm.RecentFiles[i]}\"");
                    ++i;
                }
            }

            var buf = Encoding.UTF8.GetBytes(sb.ToString());
            st.Write(buf, 0, buf.Length);
        }

        private void SaveSessionMenu_Click(object sender, RoutedEventArgs e) {
            var dlg = new VistaSaveFileDialog {
                Filter = $"{VBASyncResources.MWOpenAllFiles}|*.*|"
                    + $"{VBASyncResources.MWOpenSession}|*.ini",
                FilterIndex = 2
            };
            if (dlg.ShowDialog() == true) {
                var path = dlg.FileName;
                if (!Path.HasExtension(path)) {
                    path += ".ini";
                }
                using (var fs = new FileStream(path, FileMode.Create)) {
                    SaveSession(fs, false);
                }
                _vm.AddRecentFile(path);
            }
        }

        private void UpdateIncludeAllBox() {
            if (!_doUpdateIncludeAll) {
                return;
            }
            var vm = (ChangesViewModel)ChangesGrid.DataContext;
            if (vm.All(p => p.Commit)) {
                IncludeAllBox.IsChecked = true;
            } else if (vm.All(p => !p.Commit)) {
                IncludeAllBox.IsChecked = false;
            } else {
                IncludeAllBox.IsChecked = null;
            }
        }

        private void Window_Closed(object sender, EventArgs e) {
            _evf?.Dispose();

            string lastSessionPath;
            if (_vm.Settings.Portable)
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                lastSessionPath = Path.Combine(exeDir, "LastSession.ini");
            }
            else
            {
                lastSessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VBA Sync Tool", "LastSession.ini");
                Directory.CreateDirectory(Path.GetDirectoryName(lastSessionPath));
            }
            using (var st = new FileStream(lastSessionPath, FileMode.Create))
            {
                SaveSession(st, true);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            QuietRefreshIfInputsOk();

            if (Session.AutoRun) {
                OkButton_Click(null, null);
            }
        }
    }
}
