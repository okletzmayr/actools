﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AcManager.Controls;
using AcManager.Controls.Dialogs;
using AcManager.Controls.Helpers;
using AcManager.Controls.ViewModels;
using AcManager.DiscordRpc;
using AcManager.Pages.Dialogs;
using AcManager.Tools;
using AcManager.Tools.Data;
using AcManager.Tools.Helpers;
using AcManager.Tools.Managers;
using AcManager.Tools.Objects;
using AcManager.Tools.SemiGui;
using AcTools;
using AcTools.Processes;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Dialogs;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using FirstFloor.ModernUI.Windows;
using FirstFloor.ModernUI.Windows.Controls;
using FirstFloor.ModernUI.Windows.Media;

namespace AcManager.Pages.Drive {
    public partial class UserChampionships_SelectedPage : ILoadableContent, IParametrizedUriContent {
        private readonly DiscordRichPresence _discordPresence = new DiscordRichPresence(10, "Preparing to race", "Championships").Default();

        public void OnUri(Uri uri) {
            _id = uri.GetQueryParam("Id");
        }

        public async Task LoadAsync(CancellationToken cancellationToken) {
            await UserChampionshipsManager.Instance.EnsureLoadedAsync();

            var acObject = await UserChampionshipsManager.Instance.GetByIdAsync(_id);
            if (acObject == null || acObject.HasErrors || !acObject.IsAvailable) {
                UserChampionships.NavigateToChampionshipPage(null);
                return;
            }

            _discordPresence?.Default(acObject.DisplayName);
            DataContext = new ViewModel(acObject);
        }

        public void Load() {
            UserChampionshipsManager.Instance.EnsureLoaded();

            var acObject = UserChampionshipsManager.Instance.GetById(_id);
            if (acObject == null || acObject.HasErrors || !acObject.IsAvailable) {
                KunosCareer.NavigateToCareerPage(null);
                return;
            }

            _discordPresence?.Default(acObject.DisplayName);
            DataContext = new ViewModel(acObject);
        }

        public void Initialize() {
            this.OnActualUnload(_discordPresence);

            if (!(DataContext is ViewModel)) return;
            InitializeComponent();
            InputBindings.AddRange(new[] {
                new InputBinding(new DelegateCommand(() => Model.GoCommand.Execute(AssistsViewModel.Instance)), new KeyGesture(Key.G, ModifierKeys.Control)),
                new InputBinding(new DelegateCommand(() => UserChampionships.NavigateToChampionshipPage(null)), new KeyGesture(Key.W, ModifierKeys.Control)),
            });

            var acObject = Model.AcObject;
            if (acObject.LastSelectedTimestamp == 0) {
                if (_ignoreIntroId != acObject.Id) {
                    new UserChampionshipIntro(acObject).ShowDialog();
                }
                acObject.LastSelectedTimestamp = DateTime.Now.ToMillisecondsTimestamp();
            }

            _discordPresence.Details = $"Championships | {acObject.DisplayName}";
        }

        private string _id;
        private ScrollViewer _scroll;
        private bool _loaded;

        private void OnLoaded(object sender, RoutedEventArgs e) {
            _scroll = ListBox.FindVisualChild<ScrollViewer>();
            if (_scroll != null) {
                _scroll.LayoutUpdated += ScrollLayoutUpdated;
            }

            if (_loaded) return;
            _loaded = true;

            if (Model.AcObject.Outdated) {
                Model.OnLoaded();
                OnOutdated();
            } else {
                Model.AcObject.AcObjectOutdated += AcObject_AcObjectOutdated;
                Model.OnLoaded();
            }
        }

        private bool _positionLoaded;

        private void ScrollLayoutUpdated(object sender, EventArgs e) {
            if (_positionLoaded) return;
            var value = ValuesStorage.GetDoubleNullable(KeyScrollValue) ?? 0d;
            _scroll?.ScrollToHorizontalOffset(value);
            _positionLoaded = true;
        }

        private void OnOutdated() {
            var acObject = UserChampionshipsManager.Instance.GetById(Model.AcObject.Id);
            if (acObject == null || acObject.HasErrors || !acObject.IsAvailable) {
                KunosCareer.NavigateToCareerPage(null);
                return;
            }

            Model.AcObject.AcObjectOutdated -= AcObject_AcObjectOutdated;
            Model.AcObject = acObject;
            acObject.AcObjectOutdated += AcObject_AcObjectOutdated;
            Model.OnUnloaded();
        }

        private void AcObject_AcObjectOutdated(object sender, EventArgs e) {
            OnOutdated();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) {
            if (!_loaded) return;
            _loaded = false;

            Model.AcObject.AcObjectOutdated -= AcObject_AcObjectOutdated;
        }

        private string KeyScrollValue => @"KunosCareer_SelectedPage.ListBox.Scroll__" + _id;

        private void ListBox_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            if (_scroll == null || !_positionLoaded) return;
            ValuesStorage.Set(KeyScrollValue, _scroll.HorizontalOffset);
        }

        private ViewModel Model => (ViewModel)DataContext;

        public class ViewModel : NotifyPropertyChanged {
            private UserChampionshipObject _acObject;

            public UserChampionshipObject AcObject {
                get => _acObject;
                set {
                    if (Equals(value, _acObject)) return;
                    _acObject = value;
                    OnPropertyChanged();
                }
            }

            public ViewModel(UserChampionshipObject careerObject) {
                _acObject = careerObject;
                UpdateConditions(true).Forget();
            }

            public AssistsViewModel AssistsViewModel => AssistsViewModel.Instance;

            public SettingsHolder.DriveSettings DriveSettings => SettingsHolder.Drive;

            private AsyncCommand<AssistsViewModel> _goCommand;

            public AsyncCommand<AssistsViewModel> GoCommand => _goCommand ?? (_goCommand = new AsyncCommand<AssistsViewModel>(async o => {
                var round = _acObject.CurrentRound;
                if (!round.IsAvailable) return;

                var time = round.Time;
                var weather = round.Weather;
                var temperature = round.Temperature;

                if (_acObject.RealConditions) {
                    if (ConditionsLoading) {
                        WeatherDescription description = null;

                        using (var waiting = new WaitingDialog()) {
                            waiting.Report(AsyncProgressEntry.FromStringIndetermitate("Loading conditions…"));
                            await RealConditionsHelper.UpdateConditions(round.Track, false, true,
                                    _acObject.RealConditionsManualTime
                                            ? default(Action<int>) : t => { time = t.Clamp(CommonAcConsts.TimeMinimum, CommonAcConsts.TimeMaximum); },
                                    w => { description = w; }, waiting.CancellationToken);
                        }

                        if (description != null) {
                            temperature = description.Temperature.Clamp(CommonAcConsts.TemperatureMinimum, CommonAcConsts.TemperatureMaximum);
                            weather = WeatherTypeHelper.TryToGetWeather(description, time);
                        }
                    } else {
                        time = CurrentRoundTime;
                        weather = CurrentRoundWeather;
                        temperature = CurrentRoundRoadTemperature;
                    }
                }

                var conditions = new Game.ConditionProperties {
                    AmbientTemperature = temperature,
                    CloudSpeed = 1d,
                    RoadTemperature = Game.ConditionProperties.GetRoadTemperature(time, temperature, weather?.TemperatureCoefficient ?? 1d),
                    SunAngle = Game.ConditionProperties.GetSunAngle(time),
                    TimeMultipler = 1d,
                    WeatherName = weather?.Id ?? round.WeatherId
                };

                var multipler = SettingsHolder.Drive.KunosCareerUserAiLevel ? _acObject.UserAiLevelMultipler : 1d;
                var trackId = round.TrackId.Split('/');
                await GameWrapper.StartAsync(new Game.StartProperties(new Game.BasicProperties {
                    CarId = _acObject.PlayerCarId,
                    CarSkinId = _acObject.PlayerCarSkinId,
                    TrackId = trackId[0],
                    TrackConfigurationId = trackId.ElementAtOrDefault(1)
                }, o?.ToGameProperties(), conditions, round.TrackProperties.Properties, new Game.WeekendProperties {
                    AiLevel = 100,
                    RaceLaps = round.LapsCount,
                    PracticeDuration = _acObject.Rules.Practice,
                    QualificationDuration = _acObject.Rules.Qualifying,
                    JumpStartPenalty = _acObject.Rules.JumpStartPenalty,
                    Penalties = _acObject.Rules.Penalties,
                    BotCars = _acObject.Drivers.Where(x => !x.IsPlayer).Select(x => new Game.AiCar {
                        AiLevel = (x.AiLevel * multipler).Clamp(25, 100),
                        AiAggression = (x.AiAggression * multipler).Clamp(0, 100),
                        Ballast = x.Ballast,
                        Restrictor = x.Restrictor,
                        CarId = x.CarId,
                        SkinId = x.SkinId,
                        DriverName = x.Name,
                        Nationality = x.Nationality
                    })
                }) {
                    AdditionalPropertieses = {
                    new UserChampionshipsManager.ChampionshipProperties {
                        ChampionshipId = _acObject.Id,
                        RoundIndex = round.Index
                    }
                }
                });
            }));

            private AsyncCommand _resetCommand;

            public AsyncCommand ResetCommand => _resetCommand ?? (_resetCommand = new AsyncCommand(async () => {
                if (ModernDialog.ShowMessage(AppStrings.KunosCareer_ResetProgress_Message, AppStrings.KunosCareer_ResetProgress_Title,
                        MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

                AcObject.ChampionshipResetCommand.Execute();
                if (AcObject.SerializedRaceGridData == null) return;

                try {
                    using (var waiting = new WaitingDialog()) {
                        waiting.Report(AsyncProgressEntry.FromStringIndetermitate("Rebuilding race grid…"));
                        var cancellation = waiting.CancellationToken;

                        await Task.Delay(50);
                        if (cancellation.IsCancellationRequested) return;
                        var model = new RaceGridViewModel(true, null);
                        model.ImportFromPresetData(AcObject.SerializedRaceGridData);
                        model.TrackPitsNumber = AcObject.MaxCars;
                        model.PlayerCar = AcObject.PlayerCar;

                        var generated = await model.GenerateGameEntries(cancellation);
                        if (generated == null) {
                            // Only happens when task was cancelled.
                            return;
                        }

                        var saveLater = !AcObject.Changed;
                        AcObject.Drivers = generated.Select(x => new UserChampionshipDriver(x.DriverName, x.CarId, x.SkinId) {
                            AiLevel = x.AiLevel,
                            AiAggression = x.AiAggression,
                            Ballast = x.Ballast,
                            Restrictor = x.Restrictor,
                            Nationality = x.Nationality
                        }).Prepend(new UserChampionshipDriver(UserChampionshipDriver.PlayerName, AcObject.PlayerCarId,
                                AcObject.PlayerCarSkinId)).ToArray();

                        if (saveLater) {
                            AcObject.Save();
                        }
                    }
                } catch (Exception e) {
                    NonfatalError.Notify("Can’t create race grid", e);
                }
            }));

            private readonly WeatherTypeHelper _weatherTypeHelper = new WeatherTypeHelper();
            private CancellationTokenSource _conditionsTokenSource;

            private bool _currentSet;

            private async Task UpdateConditions(bool reset) {
                _conditionsTokenSource?.Cancel();

                var round = _acObject.CurrentRound;
                if (round == null) return;

                if (reset) {
                    _weatherTypeHelper.Reset();
                }

                if (!_currentSet || !_acObject.RealConditions || _acObject.RealConditionsManualTime) {
                    _currentRoundTime = round.Time;
                }

                if (!_currentSet || !_acObject.RealConditions) {
                    _currentSet = true;
                    _currentRoundTemperature = round.Temperature;
                    _currentRoundWeather = round.Weather;
                }

                if (!_acObject.RealConditions) {
                    OnPropertyChanged(nameof(CurrentRoundWeather));
                    OnPropertyChanged(nameof(CurrentRoundTemperature));
                    return;
                }

                WeatherType? weatherType = null;
                ConditionsLoading = true;

                using (var cancellation = new CancellationTokenSource()) {
                    _conditionsTokenSource = cancellation;
                    try {
                        await RealConditionsHelper.UpdateConditions(round.Track, false, true,
                                _acObject.RealConditionsManualTime
                                        ? default(Action<int>)
                                        : t => {
                                            _currentRoundTime = t.Clamp(CommonAcConsts.TimeMinimum, CommonAcConsts.TimeMaximum);
                                        }, w => {
                                            _currentRoundTemperature = w.Temperature.Clamp(CommonAcConsts.TemperatureMinimum,
                                                    CommonAcConsts.TemperatureMaximum);
                                            weatherType = w.Type;
                                        }, cancellation.Token);
                        if (cancellation.Token.IsCancellationRequested) return;
                    } finally {
                        OnPropertyChanged(nameof(CurrentRoundTemperature));
                        OnPropertyChanged(nameof(CurrentRoundTime));
                        if (ReferenceEquals(_conditionsTokenSource, cancellation)) {
                            _conditionsTokenSource = null;
                        }
                    }
                }

                ConditionsLoading = false;

                if (weatherType != null) {
                    _weatherTypeHelper.SetParams(CurrentRoundTime, CurrentRoundTemperature);
                    var weather = CurrentRoundWeather;
                    if (_weatherTypeHelper.TryToGetWeather(weatherType.Value, ref weather)) {
                        CurrentRoundWeather = weather;
                    } else {
                        OnPropertyChanged(nameof(CurrentRoundWeather));
                    }
                }
            }

            private bool _conditionsLoading;

            public bool ConditionsLoading {
                get => _conditionsLoading;
                set {
                    if (Equals(value, _conditionsLoading)) return;
                    _conditionsLoading = value;
                    OnPropertyChanged();
                }
            }

            private int _currentRoundTime;

            public int CurrentRoundTime {
                get => _currentRoundTime;
                set {
                    if (Equals(value, _currentRoundTime)) return;
                    _currentRoundTime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentRoundRoadTemperature));
                }
            }

            private double _currentRoundTemperature;

            public double CurrentRoundTemperature {
                get => _currentRoundTemperature;
                set {
                    if (Equals(value, _currentRoundTemperature)) return;
                    _currentRoundTemperature = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentRoundRoadTemperature));
                }
            }

            private WeatherObject _currentRoundWeather;

            public WeatherObject CurrentRoundWeather {
                get => _currentRoundWeather;
                set {
                    if (Equals(value, _currentRoundWeather)) return;
                    _currentRoundWeather = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentRoundRoadTemperature));
                }
            }

            public double CurrentRoundRoadTemperature => Game.ConditionProperties.GetRoadTemperature(CurrentRoundTime, CurrentRoundTemperature,
                    CurrentRoundWeather?.TemperatureCoefficient ?? 0d);

            private DispatcherTimer _timer;

            public void OnLoaded() {
                if (_timer != null) return;

                AcObject.PropertyChanged += OnAcObjectPropertyChanged;
                _timer = new DispatcherTimer {
                    Interval = TimeSpan.FromMinutes(0.5),
                    IsEnabled = true
                };

                _timer.Tick += OnTimerTick;
            }

            public void OnUnloaded() {
                if (_timer == null) return;
                AcObject.PropertyChanged -= OnAcObjectPropertyChanged;
                _timer.IsEnabled = false;
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }

            private void OnTimerTick(object sender, EventArgs e) {
                UpdateConditions(false).Forget();
            }

            private void OnAcObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
                switch (e.PropertyName) {
                    case nameof(AcObject.CurrentRound):
                    case nameof(AcObject.RealConditions):
                    case nameof(AcObject.RealConditionsManualTime):
                        UpdateConditions(true).Forget();
                        OnPropertyChanged(nameof(CurrentRoundTime));
                        break;
                }
            }

        }

        private void ListBox_OnPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) {
            Model.GoCommand.Execute(AssistsViewModel.Instance);
        }

        private void AssistsMore_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            new AssistsDialog(Model.AssistsViewModel).ShowDialog();
        }

        private void NextButton_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            /*var career = Model.AcObject.NextCareerObject;
            if (career == null) return;
            KunosCareer.NavigateToCareerPage(career);*/
        }

        private void TableSection_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            var scrollViewer = (ScrollViewer)sender;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void OnResetButtonClick(object sender, MouseButtonEventArgs e) {
            Model.ResetCommand.ExecuteAsync().Forget();
        }

        private void OnCarPreviewClick(object sender, MouseButtonEventArgs e) {
            var carObject = Model.AcObject.PlayerCar;
            var carSkin = Model.AcObject.PlayerCarSkin;

            var control = new CarBlock {
                Car = carObject,
                SelectedSkin = carSkin,
                SelectSkin = SettingsHolder.Drive.KunosCareerUserSkin,
                OpenShowroom = true
            };

            var dialog = new ModernDialog {
                Content = control,
                Width = 640,
                Height = 720,
                MaxWidth = 640,
                MaxHeight = 720,
                SizeToContent = SizeToContent.Manual,
                Title = carObject.DisplayName
            };

            dialog.Buttons = new[] { dialog.OkButton, dialog.CancelButton };
            dialog.ShowDialog();

            if (dialog.IsResultOk && SettingsHolder.Drive.KunosCareerUserSkin) {
                Model.AcObject.PlayerCarSkin = control.SelectedSkin;
            }
        }

        private async void OnChangeSkinMenuItemClick(object sender, MouseButtonEventArgs e) {
            var carObject = Model.AcObject.PlayerCar;
            var carSkin = Model.AcObject.PlayerCarSkin;

            await carObject.SkinsManager.EnsureLoadedAsync();

            var skins = carObject.EnabledOnlySkins.ToList();
            var viewer = new ImageViewer(
                    skins.Select(x => x.PreviewImage),
                    skins.IndexOf(carSkin),
                    CommonAcConsts.PreviewWidth,
                    details: CarBlock.GetSkinImageViewerDetailsCallback(carObject));

            if (SettingsHolder.Drive.KunosCareerUserSkin) {
                var selected = viewer.ShowDialogInSelectMode();
                Model.AcObject.PlayerCarSkin = skins.ElementAtOrDefault(selected ?? -1) ?? carSkin;
            } else {
                viewer.ShowDialog();
            }
        }

        private void ListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            var image = (ListBox.SelectedItem as UserChampionshipRoundExtended)?.Track?.PreviewImage;
            if (image != null) {
                FancyBackgroundManager.Instance.ChangeBackground(image);
            }
        }

        private static string _ignoreIntroId;

        public static void IgnoreIntro(string openId) {
            _ignoreIntroId = openId;
        }
    }
}
