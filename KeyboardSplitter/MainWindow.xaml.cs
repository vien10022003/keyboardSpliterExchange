﻿namespace KeyboardSplitter
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media.Animation;
    using System.Windows.Threading;
    using KeyboardSplitter.Commands;
    using KeyboardSplitter.Exceptions;
    using KeyboardSplitter.Managers;
    using KeyboardSplitter.Models;
    using KeyboardSplitter.Presets;
    using KeyboardSplitter.UI;
    using Microsoft.Win32;
    using SplitterCore;
    using SplitterCore.Preset;

    public partial class MainWindow : CustomWindow, IDisposable
    {
        public static readonly DependencyProperty SplitterProperty =
            DependencyProperty.Register(
            "Splitter",
            typeof(ISplitter),
            typeof(MainWindow),
            new PropertyMetadata(null));

        public static readonly DependencyProperty SlotsCountProperty =
            DependencyProperty.Register(
            "SlotsCount",
            typeof(int),
            typeof(MainWindow),
            new PropertyMetadata(0));

        public static readonly DependencyProperty SlotsCountItemsSourceProperty =
            DependencyProperty.Register(
            "SlotsCountItemsSource",
            typeof(IEnumerable<int>),
            typeof(MainWindow),
            new PropertyMetadata(new List<int> { 1, 2, 3, 4 }));

        public static readonly DependencyProperty IsInputMonitorExpandedProperty =
            DependencyProperty.Register(
            "IsInputMonitorExpanded",
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(false));

        public static readonly DependencyProperty InputMonitorTooltipProperty =
            DependencyProperty.Register(
            "InputMonitorTooltip",
            typeof(string),
            typeof(MainWindow),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty EmulationInformationProperty =
            DependencyProperty.Register(
            "EmulationInformation",
            typeof(string),
            typeof(MainWindow),
            new PropertyMetadata(string.Empty));

        private bool disposed;

        private DispatcherTimer autoCollapseTimer;

        private TimeSpan autoCollapseSpan = TimeSpan.FromSeconds(60);

        private ICommand startEmulationCommand;

        private ICommand stopEmulationCommand;

        private bool isControllersTestActive;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = ApplicationInfo.AppNameVersion;
            this.autoCollapseTimer = new DispatcherTimer();
            this.autoCollapseTimer.Interval = this.autoCollapseSpan;
            this.autoCollapseTimer.Tick += new EventHandler(this.AutoCollapseTimer_Tick);
        }

        public ISplitter Splitter
        {
            get { return (ISplitter)this.GetValue(SplitterProperty); }
            set { this.SetValue(SplitterProperty, value); }
        }

        public int SlotsCount
        {
            get { return (int)this.GetValue(SlotsCountProperty); }
            set { this.SetValue(SlotsCountProperty, value); }
        }

        public IEnumerable<int> SlotsCountItemsSource
        {
            get { return (IEnumerable<int>)this.GetValue(SlotsCountItemsSourceProperty); }
            set { this.SetValue(SlotsCountItemsSourceProperty, value); }
        }

        public bool IsInputMonitorExpanded
        {
            get { return (bool)this.GetValue(IsInputMonitorExpandedProperty); }
            set { this.SetValue(IsInputMonitorExpandedProperty, value); }
        }

        public string InputMonitorTooltip
        {
            get { return (string)this.GetValue(InputMonitorTooltipProperty); }
            set { this.SetValue(InputMonitorTooltipProperty, value); }
        }

        public string EmulationInformation
        {
            get { return (string)this.GetValue(EmulationInformationProperty); }
            set { this.SetValue(EmulationInformationProperty, value); }
        }

        public ICommand StartEmulationCommand
        {
            get
            {
                if (this.startEmulationCommand == null)
                {
                    this.startEmulationCommand = new RelayCommand(this.OnStartEmulationRequested);
                }

                return this.startEmulationCommand;
            }
        }

        public ICommand StopEmulationCommand
        {
            get
            {
                if (this.stopEmulationCommand == null)
                {
                    this.stopEmulationCommand = new RelayCommand(this.OnStopEmulationRequested);
                }

                return this.stopEmulationCommand;
            }
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.Dispose(true);
            GC.SuppressFinalize(this);
            this.disposed = true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Thread.Sleep(100);
                this.Splitter.Destroy();
                try
                {
                    LogWriter.Write("Saving game data");
                    GameDataManager.WriteGameDataToFile();
                }
                catch (Exception e)
                {
                    LogWriter.Write("Game data save failed! Exception details: " + Environment.NewLine + e.ToString());
                }

                LogWriter.Write("Main window disposed");
            }
        }

        private void OnStartEmulationRequested(object parameter)
        {
            if (this.Splitter == null)
            {
                return;
            }

            try
            {
                this.Splitter.EmulationManager.Start();
                this.Splitter.InputManager.ClearInputMonitorHistory();
            }
            catch (KeyboardSplitterExceptionBase ex)
            {
                Controls.MessageBox.Show(
                    ex.Message,
                    ApplicationInfo.AppNameVersion,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnStopEmulationRequested(object parameter)
        {
            if (this.Splitter != null)
            {
                this.Splitter.EmulationManager.Stop();
            }
        }

        private void FadeInEmulationInformation()
        {
            this.helperGrid.Opacity = 0;
            this.helperGrid.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromSeconds(1))));
            this.helperGrid.IsHitTestVisible = true;
        }

        private void FadeOutEmulationInformation()
        {
            this.helperGrid.Opacity = 1;
            this.helperGrid.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(1))));
            this.helperGrid.IsHitTestVisible = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            GlobalSettings.TryApplySettings();
            LogWriter.Write("Main window loaded");
            this.InputMonitorTooltip = "Click to expand/collapse the input device monitor.\r\n" +
                "It will autocollapse after " + this.autoCollapseSpan.TotalSeconds + " seconds to save CPU time.";

            XinputWrapper.XinputController.StartPolling();

            var inputDevices = KeyboardSplitter.Managers.InputManager.ConnectedInputDevices;
            if (inputDevices.Count == 0)
            {
                // We have some error or nothing is attached to the system.
                LogWriter.Write("No input devices were detected! Terminating application.");
                Controls.MessageBox.Show(
                    "No input devices were detected!\r\nApplication will now close!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Environment.Exit(0);
            }

            if (!GlobalSettings.Instance.SuggestInputDevicesForNewSlots)
            {
                this.SlotsCount = 1;
            }
            else
            {
                var keyboardsCount = inputDevices.Where(x => x.IsKeyboard).Count();
                var miceCount = inputDevices.Count - keyboardsCount;

                this.SlotsCount = Math.Min(Math.Max(keyboardsCount, miceCount), 4);
            }

            if (!string.IsNullOrWhiteSpace(App.autostartGameName)) {
                LogWriter.Write(string.Format("Autostarting keyboard splitter game={0}", App.autostartGameName));
                Game game = GameDataManager.Games.FirstOrDefault(g => g.GameTitle == App.autostartGameName);
                if (game == null)
                {
                    throw new InvalidOperationException("Unable to find game with name " + App.autostartGameName);
                }

                game.TryStart();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            LogWriter.Write("Application is about to close. Checking for unsaved presets...");

            // Checking for unsaved presets
            var unsavedPresets = new List<IPreset>();
            foreach (var preset in PresetDataManager.CurrentPresets)
            {
                if (PresetDataManager.IsPresetChanged(preset))
                {
                    unsavedPresets.Add(preset);
                }
            }

            string message = string.Empty;
            foreach (var preset in unsavedPresets)
            {
                message += string.Format("Preset '{0}'{1}", preset.Name, Environment.NewLine);
            }

            if (message.Length > 0)
            {
                var result = Controls.MessageBox.Show(
                    "Do you want to save the following unsaved presets, before you quit?\r\n\r\n" + message,
                    "You are about to quit " + ApplicationInfo.AppNameVersion,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // save the presets
                    PresetDataManager.WritePresetDataToFile();
                }

                e.Cancel = result == MessageBoxResult.Cancel;
            }
            else
            {
                // We may have a deleted preset(s)
                var xmlPresets = PresetDataManager.ReadPresetDataFromFile();
                bool hasChanges = false;
                foreach (var preset in xmlPresets.Presets)
                {
                    try
                    {
                        if (PresetDataManager.IsPresetChanged(preset))
                        {
                            hasChanges = true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Preset is deleted
                        hasChanges = true;
                    }
                }

                if (hasChanges)
                {
                    PresetDataManager.WritePresetDataToFile();
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this.Dispose();
            XinputWrapper.XinputController.StopPolling();
            GlobalSettings.TrySaveToFile();
        }

        private void OnSlotsCountChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!this.IsActive || (sender as System.Windows.Controls.ComboBox).SelectedIndex == -1)
            {
                return;
            }

            // Creating new splitter
            if (this.Splitter == null)
            {
                this.Splitter = new Splitter(this.SlotsCount);
                this.Splitter.EmulationManager.EmulationStarted += this.EmulationManager_EmulationStarted;
                this.Splitter.EmulationManager.EmulationStopped += this.EmulationManager_EmulationStopped;
            }
            else
            {
                this.Splitter.EmulationManager.ChangeSlotsCountBy(this.SlotsCount - this.Splitter.EmulationManager.Slots.Count);
            }

            if (this.SizeToContent != System.Windows.SizeToContent.Width)
            {
                // Autosizing the main window
                int screenWidth = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds.Width;
                if (screenWidth >= 1280 && this.WindowState != WindowState.Maximized)
                {
                    this.SizeToContent = SizeToContent.Width;
                }
            }

            // Preparing the emulation information
            if (this.SlotsCount == 1)
            {
                this.EmulationInformation = "There is 1 Virtual Xbox 360 Controller mounted into the system.";
                this.EmulationInformation += Environment.NewLine + "To feed it, use the assigned keyboard/mouse.";
            }
            else
            {
                this.EmulationInformation = string.Format(
                    "There are {0} Virtual Xbox 360 Controllers mounted into the system.",
                    this.SlotsCount);
                this.EmulationInformation += Environment.NewLine + "To feed them, use the assigned keyboards/mice.";
            }
        }

        private void EmulationManager_EmulationStarted(object sender, EventArgs e)
        {
            if (this.Splitter.EmulationManager.Slots.Any(x => x.Keyboard == SplitterCore.Input.Keyboard.None && x.Mouse == SplitterCore.Input.Mouse.None))
            {
                // We have mouse click feeder active
                return;
            }

            this.FadeInEmulationInformation();
        }

        private void EmulationManager_EmulationStopped(object sender, EventArgs e)
        {
            if (this.Splitter.EmulationManager.Slots.Any(x => x.Keyboard == SplitterCore.Input.Keyboard.None && x.Mouse == SplitterCore.Input.Mouse.None))
            {
                // We have mouse click feeder active
                return;
            }

            this.FadeOutEmulationInformation();
        }

        private void AutoCollapseTimer_Tick(object sender, EventArgs e)
        {
            this.IsInputMonitorExpanded = false;
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            this.autoCollapseTimer.Start();
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            this.autoCollapseTimer.Stop();
            this.Splitter.InputManager.ClearInputMonitorHistory();
        }

        private void FileExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenGamepadProperties_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("joy.cpl");
            }
            catch (Exception ex)
            {
                Controls.MessageBox.Show(
                    "Can not open gamepad properties: " + Environment.NewLine + ex.Message,
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenXboxSite(object sender, RoutedEventArgs e)
        {
            if (XboxGamepad.AreXboxAccessoriesInstalled)
            {
                Controls.MessageBox.Show(
                    "Xbox accessories is already installed on your computer!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            try
            {
                Process.Start("https://www.microsoft.com/accessories/en-gb/d/xbox-360-controller-for-windows");
            }
            catch (Exception)
            {
            }
        }

        private void HelpAbout_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow dialog = new AboutWindow("About " + ApplicationInfo.AppNameVersion);
            dialog.ShowDialog();
        }

        private void UninstallBuiltInDrivers_Click(object sender, RoutedEventArgs e)
        {
            var result = Controls.MessageBox.Show(
                "Are you sure that you want to remove the built-in drivers?",
                "Confirm uninstall",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DriversManager.UninstallBuiltInDrivers();
            }
        }

        private void ControllerTest_Click(object sender, RoutedEventArgs e)
        {
            if (this.isControllersTestActive)
            {
                Controls.MessageBox.Show(
                    "Xinput controller test window is already open!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            XinputControllerTestWindow window = new XinputControllerTestWindow(this);
            window.Closed += (oo, ss) =>
            {
                this.isControllersTestActive = false;
            };

            this.isControllersTestActive = true;
            window.Show();
        }

        private void HelpContents_Click(object sender, RoutedEventArgs e)
        {
            HelpDialog howToUse = new HelpDialog();
            howToUse.ShowDialog();
        }

        private void HowItWorks_Click(object sender, RoutedEventArgs e)
        {
            HowItWorksWindow how = new HowItWorksWindow();
            how.ShowDialog();
        }

        private void FAQ_Click(object sender, RoutedEventArgs e)
        {
            FaqWindow fac = new FaqWindow();
            fac.ShowDialog();
        }

        private void OptionsClicked(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void OnHelperGridCloseButtonClicked(object sender, RoutedEventArgs e)
        {
            this.FadeOutEmulationInformation();
        }

        private void OnControllerSubtypesClicked(object sender, RoutedEventArgs e)
        {
            if (this.Splitter.EmulationManager.IsEmulationStarted)
            {
                Controls.MessageBox.Show(
                    "You can not change controller subtypes, when emulation is running!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            var wind = new XinputSubTypesWindow();
            wind.Owner = this;
            wind.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            wind.ShowDialog();
        }

        private void OnEditGamesListClicked(object sender, RoutedEventArgs e)
        {
            if (this.Splitter.EmulationManager.IsEmulationStarted)
            {
                Controls.MessageBox.Show(
                    "You can not edit games, when emulation is running!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            var gameEditor = new GameEditor();
            gameEditor.Owner = this;
            gameEditor.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            gameEditor.ShowDialog();
        }

        private void GamesSubmenuOpened(object sender, RoutedEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            this.playGameMenuItem.Items.Clear();

            foreach (var game in GameDataManager.Games)
            {
                if (game.Status == Enums.GameStatus.OK)
                {
                    var newItem = new MenuItem() { Header = game.GameTitle };
                    newItem.Click += (ss, ee) =>
                    {
                        try
                        {
                            game.TryStart();
                        }
                        catch (Exception ex)
                        {
                            Controls.MessageBox.Show(
                                ex.Message,
                                ApplicationInfo.AppName,
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    };

                    newItem.Icon = new Image() { Source = game.GameIcon, Width = 20, Height = 20 };
                    newItem.ToolTip = game.GameNotes;
                    this.playGameMenuItem.Items.Add(newItem);
                }
            }

            this.playGameMenuItem.IsEnabled = this.playGameMenuItem.Items.Count != 0;
            this.Cursor = Cursors.Arrow;
        }

        private void OnImportPresetsClicked(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.AddExtension = true;
            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.DefaultExt = "*.xml";
            dialog.DereferenceLinks = true;
            dialog.Filter = "XML file (*.xml)|*.xml";
            dialog.Multiselect = false;
            dialog.Title = "Choose presets file to import";
            dialog.ValidateNames = true;
            Interceptor.Interception.DisableMouseEvents = true;
            var result = dialog.ShowDialog(this);
            Interceptor.Interception.DisableMouseEvents = false;
            if (result != true)
            {
                return;
            }

            var extension = System.IO.Path.GetExtension(dialog.FileName);
            if (extension.ToLower() != ".xml")
            {
                Controls.MessageBox.Show(
                    "You must provide an xml file!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            List<Preset> presets = new List<Preset>();
            Exception ex = null;
            try
            {
                presets = PresetData.Deserialize(dialog.FileName).Presets.ToList();
            }
            catch (Exception currentVersionException)
            {
                ex = currentVersionException;
            }

            if (ex != null)
            {
                ex = null;
                try
                {
                    presets = PresetUpgrader.GetUpgradedPresets(dialog.FileName).ToList();
                }
                catch (Exception oldVersionException)
                {
                    ex = oldVersionException;
                }
            }

            if (ex != null)
            {
                LogWriter.Write(ex.ToString());
                Controls.MessageBox.Show(
                    "Failed to load presets! See " + LogWriter.GetLogFileName + " for more details",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            var presetNames = string.Empty;
            var overwriteString = string.Empty;
            foreach (var preset in presets)
            {
                presetNames += Environment.NewLine + preset.Name;
                var duplicate = PresetDataManager.FindPreset(preset.Name);
                if (duplicate != null)
                {
                    presetNames += " [Will be overwritten]";
                    overwriteString = "/overwrite";
                }
            }

            var message = string.Format(
                "Are you sure that you want to add{0} the following presets?{1}{2}",
                overwriteString,
                Environment.NewLine,
                presetNames);

            var confirm = Controls.MessageBox.Show(
                message,
                ApplicationInfo.AppName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                foreach (var preset in presets)
                {
                    var duplicateToDelete = PresetDataManager.FindPreset(preset.Name);
                    if (duplicateToDelete != null)
                    {
                        PresetDataManager.DeletePreset(duplicateToDelete);
                    }

                    PresetDataManager.AddNewPreset(preset);
                }
            }
        }

        private void OnExportPresetsClicked(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.AddExtension = true;
            dialog.CheckPathExists = true;
            dialog.DefaultExt = "*.xml";
            dialog.DereferenceLinks = true;
            dialog.Filter = "XML file (*.xml)|*.xml";
            dialog.Title = "Export presets";
            dialog.ValidateNames = true;
            Interceptor.Interception.DisableMouseEvents = true;
            var result = dialog.ShowDialog(this);
            Interceptor.Interception.DisableMouseEvents = false;
            if (result != true)
            {
                return;
            }

            var extension = System.IO.Path.GetExtension(dialog.FileName);
            if (extension.ToLower() != ".xml")
            {
                Controls.MessageBox.Show(
                    "You must export presets as xml file!",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            try
            {
                PresetDataManager.WritePresetDataToFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                LogWriter.Write(ex.ToString());
                Controls.MessageBox.Show(
                    "Presets export failed! Please refer to " + LogWriter.GetLogFileName + " for details",
                    ApplicationInfo.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}