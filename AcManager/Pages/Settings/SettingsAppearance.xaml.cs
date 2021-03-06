﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AcManager.Controls.Helpers;
using AcManager.Controls.Presentation;
using AcManager.Tools.Helpers;
using AcTools.Utils;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using FirstFloor.ModernUI.Windows;
using FirstFloor.ModernUI.Windows.Attached;
using FirstFloor.ModernUI.Windows.Controls;
using FirstFloor.ModernUI.Windows.Media;
using Microsoft.Win32;
using OxyPlot;
using SlimDX.DirectWrite;
using Path = System.IO.Path;

namespace AcManager.Pages.Settings {
    public partial class SettingsAppearance {
        public SettingsAppearance() {
            InitializeComponent();
            DataContext = new ViewModel();
            this.AddWidthCondition(1080).Add(v => Grid.Columns = v ? 2 : 1);
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            ScaleSlider.PreviewMouseLeftButtonUp += (s, a) => ScaleSlider.RemoveFocus();
            var thumb = ScaleSlider.FindVisualChild<Thumb>();
            if (thumb != null) {
                thumb.DragCompleted += (s, a) => ScaleSlider.RemoveFocus();
                thumb.DragDelta += OnDragDelta;
            }
        }

        private void OnDragDelta(object sender, DragDeltaEventArgs e) {
            var window = Application.Current.MainWindow;
            if (window == null) return;

            var thumb = (Thumb)sender;
            var oldPosition = thumb.PointToScreen(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
            DpiAwareWindow.AppScale.ScalePercentage = ScaleSlider.Value.Round();
            var newPosition = thumb.PointToScreen(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
            window.Left += oldPosition.X - newPosition.X;
            window.Top += oldPosition.Y - newPosition.Y;
        }

        public class ViewModel : NotifyPropertyChanged {
            private static BitmapScalingMode? _originalScalingMode;
            private static bool? _originalSoftwareRendering;

            public FancyBackgroundManager FancyBackgroundManager => FancyBackgroundManager.Instance;
            public AppAppearanceManager AppAppearanceManager => AppAppearanceManager.Instance;
            public SettingsHolder.InterfaceSettings Interface => SettingsHolder.Interface;

            internal ViewModel() {
                BitmapScaling = BitmapScalings.FirstOrDefault(x => x.Value == AppAppearanceManager.BitmapScalingMode) ?? BitmapScalings.First();
                SoftwareRendering = AppAppearanceManager.SoftwareRenderingMode;
                TextFormatting = AppAppearanceManager.IdealFormattingMode == null ? TextFormattings[0] :
                        AppAppearanceManager.IdealFormattingMode.Value ? TextFormattings[2] : TextFormattings[1];

                if (!_originalScalingMode.HasValue) {
                    _originalScalingMode = BitmapScaling.Value;
                }

                if (!_originalSoftwareRendering.HasValue) {
                    _originalSoftwareRendering = SoftwareRendering;
                }
            }

            public class BitmapScalingEntry : Displayable {
                public BitmapScalingMode Value { get; set; }
            }

            private bool _bitmapScalingRestartRequired;

            public bool BitmapScalingRestartRequired {
                get => _bitmapScalingRestartRequired;
                set {
                    if (Equals(value, _bitmapScalingRestartRequired)) return;
                    _bitmapScalingRestartRequired = value;
                    OnPropertyChanged();
                }
            }

            private CommandBase _restartCommand;

            public ICommand RestartCommand => _restartCommand ?? (_restartCommand = new DelegateCommand(WindowsHelper.RestartCurrentApplication));

            private BitmapScalingEntry _bitmapScaling;

            public BitmapScalingEntry BitmapScaling {
                get => _bitmapScaling;
                set {
                    if (Equals(value, _bitmapScaling)) return;
                    _bitmapScaling = value;
                    OnPropertyChanged();

                    if (_originalScalingMode.HasValue && value != null) {
                        AppAppearanceManager.BitmapScalingMode = value.Value;
                        BitmapScalingRestartRequired = value.Value != _originalScalingMode;
                    }
                }
            }

            public BitmapScalingEntry[] BitmapScalings { get; } = {
                new BitmapScalingEntry { DisplayName = Tools.ToolsStrings.AcSettings_Quality_Low, Value = BitmapScalingMode.NearestNeighbor },
                new BitmapScalingEntry { DisplayName = Tools.ToolsStrings.AcSettings_Quality_Normal, Value = BitmapScalingMode.LowQuality },
                new BitmapScalingEntry { DisplayName = Tools.ToolsStrings.AcSettings_Quality_High, Value = BitmapScalingMode.HighQuality }
            };

            private bool _softwareRenderingRestartRequired;

            public bool SoftwareRenderingRestartRequired {
                get => _softwareRenderingRestartRequired;
                set {
                    if (Equals(value, _softwareRenderingRestartRequired)) return;
                    _softwareRenderingRestartRequired = value;
                    OnPropertyChanged();
                }
            }

            private bool _softwareRendering;

            public bool SoftwareRendering {
                get => _softwareRendering;
                set {
                    if (Equals(value, _softwareRendering)) return;
                    _softwareRendering = value;
                    OnPropertyChanged();

                    if (_originalScalingMode.HasValue) {
                        AppAppearanceManager.SoftwareRenderingMode = value;
                        SoftwareRenderingRestartRequired = value != _originalSoftwareRendering;
                    }
                }
            }

            private Displayable _textFormatting;

            public Displayable TextFormatting {
                get => _textFormatting;
                set {
                    if (Equals(value, _textFormatting)) return;
                    _textFormatting = value;
                    OnPropertyChanged();
                    AppAppearanceManager.IdealFormattingMode = value == TextFormattings[0] ? (bool?)null :
                            value == TextFormattings[2];
                }
            }

            public Displayable[] TextFormattings { get; } = {
                new Displayable { DisplayName = string.Format(Tools.ToolsStrings.Common_Recommended, "Auto") },
                new Displayable { DisplayName = Tools.ToolsStrings.AcSettings_Quality_Subpixel },
                new Displayable { DisplayName = Tools.ToolsStrings.AcSettings_Quality_Ideal },
            };

            private DelegateCommand _changeBackgroundImageCommand;

            public DelegateCommand ChangeBackgroundImageCommand => _changeBackgroundImageCommand ?? (_changeBackgroundImageCommand = new DelegateCommand(() => {
                var dialog = new OpenFileDialog {
                    Filter = FileDialogFilters.ImagesFilter,
                    Title = "Select background image",
                    InitialDirectory = Path.GetDirectoryName(AppAppearanceManager.BackgroundFilename) ?? AcPaths.GetDocumentsScreensDirectory(),
                    RestoreDirectory = true
                };

                if (dialog.ShowDialog() == true) {
                    AppAppearanceManager.BackgroundFilename = dialog.FileName;
                }
            }));

            private DelegateCommand _resetBackgroundImageCommand;

            public DelegateCommand ResetBackgroundImageCommand => _resetBackgroundImageCommand ?? (_resetBackgroundImageCommand = new DelegateCommand(() => {
                AppAppearanceManager.BackgroundFilename = null;
            }));
        }
    }
}
