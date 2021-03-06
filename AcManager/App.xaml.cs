﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcManager.AcSound;
using AcManager.Assets;
using AcManager.ContentRepair;
using AcManager.Controls;
using AcManager.Controls.Converters;
using AcManager.Controls.Dialogs;
using AcManager.Controls.Helpers;
using AcManager.Controls.Presentation;
using AcManager.Controls.UserControls;
using AcManager.Controls.ViewModels;
using AcManager.CustomShowroom;
using AcManager.DiscordRpc;
using AcManager.Internal;
using AcManager.Pages.ContentTools;
using AcManager.Pages.Dialogs;
using AcManager.Pages.Drive;
using AcManager.Pages.Lists;
using AcManager.Pages.Windows;
using AcManager.Plugins;
using AcManager.Tools;
using AcManager.Tools.AcErrors;
using AcManager.Tools.AcManagersNew;
using AcManager.Tools.AcObjectsNew;
using AcManager.Tools.Data;
using AcManager.Tools.Data.GameSpecific;
using AcManager.Tools.GameProperties;
using AcManager.Tools.Helpers;
using AcManager.Tools.Helpers.AcSettings;
using AcManager.Tools.Helpers.Api;
using AcManager.Tools.Helpers.Loaders;
using AcManager.Tools.Helpers.PresetsPerMode;
using AcManager.Tools.Profile;
using AcManager.Tools.Managers;
using AcManager.Tools.Managers.Plugins;
using AcManager.Tools.Managers.Online;
using AcManager.Tools.Managers.Presets;
using AcManager.Tools.Miscellaneous;
using AcManager.Tools.Objects;
using AcManager.Tools.SemiGui;
using AcManager.Tools.SharedMemory;
using AcManager.Tools.Starters;
using AcTools;
using AcTools.AcdEncryption;
using AcTools.AcdFile;
using AcTools.DataFile;
using AcTools.GenericMods;
using AcTools.Kn5File;
using AcTools.Processes;
using AcTools.Render.Kn5SpecificSpecial;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using AcTools.Windows;
using FirstFloor.ModernUI;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using FirstFloor.ModernUI.Win32;
using FirstFloor.ModernUI.Windows;
using FirstFloor.ModernUI.Windows.Attached;
using FirstFloor.ModernUI.Windows.Controls;
using FirstFloor.ModernUI.Windows.Converters;
using JetBrains.Annotations;
using Newtonsoft.Json;
using StringBasedFilter;

namespace AcManager {
    public partial class App : IDisposable {
        /// <summary>
        /// It’s incredible how I fail to implement something that simple.
        /// TODO: Find a way to reproduce that RivaTuner crash here and figure out why that flag doesn’t help.
        /// </summary>
        public static bool OptionUseTryingToRunFlag = false;

        private const string WebBrowserEmulationModeDisabledKey = "___webBrowserEmulationModeDisabled";

        [CanBeNull]
        private static string _tryingToRunFlag;

        public static void CreateAndRun() {
            FilesStorage.Initialize(EntryPoint.ApplicationDataDirectory);
            if (AppArguments.GetBool(AppFlag.DisableSaving)) {
                ValuesStorage.Initialize();
                CacheStorage.Initialize();
            } else {
                ValuesStorage.Initialize(FilesStorage.Instance.GetFilename("Values.data"),
                        InternalUtils.GetValuesStorageEncryptionKey(),
                        AppArguments.GetBool(AppFlag.DisableValuesCompression));
                CacheStorage.Initialize(FilesStorage.Instance.GetFilename("Cache.data"), AppArguments.GetBool(AppFlag.DisableValuesCompression));
                LargeFileUploaderParams.Initialize(FilesStorage.Instance.GetFilename("Authentication.data"),
                        AppArguments.GetBool(AppFlag.DisableValuesCompression));
            }

            if (!AppArguments.GetBool(AppFlag.DisableLogging)) {
                var logFilename = EntryPoint.GetLogName("Main Log");
                Logging.Initialize(FilesStorage.Instance.GetFilename("Logs", logFilename), AppArguments.GetBool(AppFlag.OptimizeLogging));
                Logging.Write($"App version: {BuildInformation.AppVersion} ({BuildInformation.Platform}, {WindowsVersionHelper.GetVersion()})");
            }

            if (AppArguments.GetBool(AppFlag.NoProxy, true)) {
                WebRequest.DefaultWebProxy = null;
            }

            NonfatalError.Initialize();
            LocaleHelper.InitializeAsync().Wait();

            if (OptionUseTryingToRunFlag) {
                SetTryingToRunFlag();
            }

            if (AppArguments.GetBool(AppFlag.SoftwareRendering) || ValuesStorage.GetBool(AppAppearanceManager.KeySoftwareRendering)) {
                SwitchToSoftwareRendering();
            }

            AppearanceManager.DefaultValuesSource = new Uri("/AcManager.Controls;component/Assets/ModernUI.Default.xaml", UriKind.Relative);
            new App().Run();
        }

        private static void SetTryingToRunFlag() {
            _tryingToRunFlag = FilesStorage.Instance.GetTemporaryFilename("Trying to run.flag");
            if (!File.Exists(_tryingToRunFlag)) {
                try {
                    File.WriteAllBytes(_tryingToRunFlag, new byte[0]);
                } catch {
                    // ignored
                }
            } else if (new CustomMessageBox(
                    @"Looks like CM failed to start last time, it could be related to third-party apps such as RivaTuner messing with UI rendering. If this is the case, please, add Content Manager to exceptions.

As an alternative solution, you can switch to software UI rendering, but it will slow app down.", "Potential compatibility issue",
                    "Switch to software rendering", "Use hardware-accelerated rendering").ShowDialog() == System.Windows.Forms.DialogResult.Yes) {
                ValuesStorage.Set(AppAppearanceManager.KeySoftwareRendering, true);
            }
        }

        private static void SwitchToSoftwareRendering() {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(24));
        }

        private AppHibernator _hibernator;

        private App() {
            if (AppArguments.GetBool(AppFlag.IgnoreHttps)) {
                ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            }

            AppArguments.Set(AppFlag.SyncNavigation, ref ModernFrame.OptionUseSyncNavigation);
            AppArguments.Set(AppFlag.DisableTransitionAnimation, ref ModernFrame.OptionDisableTransitionAnimation);
            AppArguments.Set(AppFlag.RecentlyClosedQueueSize, ref LinkGroupFilterable.OptionRecentlyClosedQueueSize);

            AppArguments.Set(AppFlag.NoProxy, ref KunosApiProvider.OptionNoProxy);

            var proxy = AppArguments.Get(AppFlag.Proxy);
            if (!string.IsNullOrWhiteSpace(proxy)) {
                try {
                    var s = proxy.Split(':');
                    WebRequest.DefaultWebProxy = new WebProxy(s[0], FlexibleParser.ParseInt(s.ElementAtOrDefault(1), 1080));
                } catch (Exception e) {
                    Logging.Error(e);
                }
            }

            // TODO: AppArguments.Set(AppFlag.ScanPingTimeout, ref RecentManagerOld.OptionScanPingTimeout);
            AppArguments.Set(AppFlag.LanSocketTimeout, ref KunosApiProvider.OptionLanSocketTimeout);
            AppArguments.Set(AppFlag.LanPollTimeout, ref KunosApiProvider.OptionLanPollTimeout);
            AppArguments.Set(AppFlag.WebRequestTimeout, ref KunosApiProvider.OptionWebRequestTimeout);
            AppArguments.Set(AppFlag.DirectRequestTimeout, ref KunosApiProvider.OptionDirectRequestTimeout);
            AppArguments.Set(AppFlag.CommandTimeout, ref GameCommandExecutorBase.OptionCommandTimeout);

            AppArguments.Set(AppFlag.DisableAcRootChecking, ref AcPaths.OptionEaseAcRootCheck);
            AppArguments.Set(AppFlag.AcObjectsLoadingConcurrency, ref BaseAcManagerNew.OptionAcObjectsLoadingConcurrency);
            AppArguments.Set(AppFlag.SkinsLoadingConcurrency, ref CarObject.OptionSkinsLoadingConcurrency);
            AppArguments.Set(AppFlag.KunosCareerIgnoreSkippedEvents, ref KunosCareerEventsManager.OptionIgnoreSkippedEvents);
            AppArguments.Set(AppFlag.IgnoreMissingSkinsInKunosEvents, ref KunosEventObjectBase.OptionIgnoreMissingSkins);

            AppArguments.Set(AppFlag.CanPack, ref AcCommonObject.OptionCanBePackedFilter);
            AppArguments.Set(AppFlag.CanPackCars, ref CarObject.OptionCanBePackedFilter);

            AppArguments.Set(AppFlag.ForceToastFallbackMode, ref Toast.OptionFallbackMode);

            AppArguments.Set(AppFlag.SmartPresetsChangedHandling, ref UserPresetsControl.OptionSmartChangedHandling);
            AppArguments.Set(AppFlag.EnableRaceIniRestoration, ref Game.OptionEnableRaceIniRestoration);
            AppArguments.Set(AppFlag.EnableRaceIniTestMode, ref Game.OptionRaceIniTestMode);
            AppArguments.Set(AppFlag.RaceOutDebug, ref Game.OptionDebugMode);

            AppArguments.Set(AppFlag.NfsPorscheTribute, ref RaceGridViewModel.OptionNfsPorscheNames);
            AppArguments.Set(AppFlag.KeepIniComments, ref IniFile.OptionKeepComments);
            AppArguments.Set(AppFlag.AutoConnectPeriod, ref OnlineServer.OptionAutoConnectPeriod);
            AppArguments.Set(AppFlag.GenericModsLogging, ref GenericModsEnabler.OptionLoggingEnabled);
            AppArguments.Set(AppFlag.SidekickOptimalRangeThreshold, ref SidekickHelper.OptionRangeThreshold);
            AppArguments.Set(AppFlag.GoogleDriveLoaderDebugMode, ref GoogleDriveLoader.OptionDebugMode);
            AppArguments.Set(AppFlag.GoogleDriveLoaderManualRedirect, ref GoogleDriveLoader.OptionManualRedirect);
            AppArguments.Set(AppFlag.RdLoaderAllowed, ref RaceDepartmentLoader.OptionAllowed);
            AppArguments.Set(AppFlag.DebugPing, ref ServerEntry.OptionDebugPing);
            AppArguments.Set(AppFlag.JpegQuality, ref ImageUtilsOptions.JpegQuality);
            AppArguments.Set(AppFlag.FbxMultiMaterial, ref Kn5.OptionJoinToMultiMaterial);

            Acd.Factory = new AcdFactory();
            LimitedSpace.Initialize();
            DataProvider.Initialize();
            CountryIdToImageConverter.Initialize(
                    FilesStorage.Instance.GetDirectory(FilesStorage.DataDirName, ContentCategory.CountryFlags),
                    FilesStorage.Instance.GetDirectory(FilesStorage.DataUserDirName, ContentCategory.CountryFlags));
            FilesStorage.Instance.Watcher(ContentCategory.CountryFlags).Update += (sender, args) => { CountryIdToImageConverter.ResetCache(); };

            TestKey();

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            if (!AppArguments.GetBool(AppFlag.PreventDisableWebBrowserEmulationMode) && (
                    ValuesStorage.GetInt(WebBrowserEmulationModeDisabledKey) < WebBrowserHelper.EmulationModeDisablingVersion ||
                            AppArguments.GetBool(AppFlag.ForceDisableWebBrowserEmulationMode))) {
                try {
                    WebBrowserHelper.DisableBrowserEmulationMode();
                    ValuesStorage.Set(WebBrowserEmulationModeDisabledKey, WebBrowserHelper.EmulationModeDisablingVersion);
                } catch (Exception e) {
                    Logging.Warning("Can’t disable emulation mode: " + e);
                }
            }

            JsonConvert.DefaultSettings = () => new JsonSerializerSettings {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,
                Culture = CultureInfo.InvariantCulture
            };

            AcToolsLogging.Logger = (s, m, p, l) => Logging.Write($"{s} (AcTools)", m, p, l);
            AcToolsLogging.NonFatalErrorHandler = (s, c, e, b) => {
                if (b) {
                    NonfatalError.NotifyBackground(s, c, e);
                } else {
                    NonfatalError.Notify(s, c, e);
                }
            };

            AppArguments.Set(AppFlag.ControlsDebugMode, ref ControlsSettings.OptionDebugControlles);
            AppArguments.Set(AppFlag.ControlsRescanPeriod, ref ControlsSettings.OptionRescanPeriod);
            var ignoreControls = AppArguments.Get(AppFlag.IgnoreControls);
            if (!string.IsNullOrWhiteSpace(ignoreControls)) {
                ControlsSettings.OptionIgnoreControlsFilter = Filter.Create(new StringTester(), ignoreControls);
            }

            var sseStart = AppArguments.Get(AppFlag.SseName);
            if (!string.IsNullOrWhiteSpace(sseStart)) {
                SseStarter.OptionStartName = sseStart;
            }
            AppArguments.Set(AppFlag.SseLogging, ref SseStarter.OptionLogging);

            FancyBackgroundManager.Initialize();
            if (AppArguments.Has(AppFlag.UiScale)) {
                DpiAwareWindow.OptionScale = AppArguments.GetDouble(AppFlag.UiScale, 1d);
            }

            if (!AppKeyHolder.IsAllRight) {
                AppAppearanceManager.OptionCustomThemes = false;
            } else {
                AppArguments.Set(AppFlag.CustomThemes, ref AppAppearanceManager.OptionCustomThemes);
            }

            AppArguments.Set(AppFlag.FancyHintsDebugMode, ref FancyHint.OptionDebugMode);
            AppArguments.Set(AppFlag.FancyHintsMinimumDelay, ref FancyHint.OptionMinimumDelay);

            /*AppAppearanceManager.OptionIdealFormattingModeDefaultValue = AppArguments.GetBool(AppFlag.IdealFormattingMode,
                    !Equals(DpiAwareWindow.OptionScale, 1d));*/
            AppAppearanceManager.Initialize();

            ContentUtils.Register("AppStrings", AppStrings.ResourceManager);
            ContentUtils.Register("ControlsStrings", ControlsStrings.ResourceManager);
            ContentUtils.Register("ToolsStrings", ToolsStrings.ResourceManager);
            ContentUtils.Register("UiStrings", UiStrings.ResourceManager);

            AcObjectsUriManager.Register(new UriProvider());

            {
                var uiFactory = new GameWrapperUiFactory();
                GameWrapper.RegisterFactory(uiFactory);
                ServerEntry.RegisterFactory(uiFactory);
            }

            GameWrapper.RegisterFactory(new DefaultAssistsFactory());
            LapTimesManager.Instance.SetListener();
            RaceResultsStorage.Instance.SetListener();

            AcError.RegisterFixer(new AcErrorFixer());
            AcError.RegisterSolutionsFactory(new SolutionsFactory());

            InitializePresets();

            SharingHelper.Initialize();
            SharingUiHelper.Initialize(AppArguments.GetBool(AppFlag.ModernSharing) ? new Win10SharingUiHelper() : null);

            {
                var addonsDir = FilesStorage.Instance.GetFilename("Addons");
                var pluginsDir = FilesStorage.Instance.GetFilename("Plugins");
                if (Directory.Exists(addonsDir) && !Directory.Exists(pluginsDir)) {
                    Directory.Move(addonsDir, pluginsDir);
                } else {
                    pluginsDir = FilesStorage.Instance.GetDirectory("Plugins");
                }

                PluginsManager.Initialize(pluginsDir);
                PluginsWrappers.Initialize(
                        new FmodPluginWrapper(),
                        new MagickPluginWrapper(),
                        new CefSharpPluginWrapper(),
                        new StarterPlus());
            }

            {
                var onlineMainListFile = FilesStorage.Instance.GetFilename("Online Servers", "Main List.txt");
                var onlineFavouritesFile = FilesStorage.Instance.GetFilename("Online Servers", "Favourites.txt");
                if (File.Exists(onlineMainListFile) && !File.Exists(onlineFavouritesFile)) {
                    Directory.Move(onlineMainListFile, onlineFavouritesFile);
                }
            }

            SteamIdHelper.Initialize(AppArguments.Get(AppFlag.ForceSteamId));
            CupClient.Initialize();
            Superintendent.Initialize();

            AppArguments.Set(AppFlag.OfflineMode, ref AppKeyDialog.OptionOfflineMode);

            FlexibleLoader.CmRequestHandler = new CmRequestHandler();
            ContextMenus.ContextMenusProvider = new ContextMenusProvider();
            PrepareUi();

            AppShortcut.Initialize("Content Manager", "Content Manager");

            // If shortcut exists, make sure it has a proper app ID set for notifications
            if (File.Exists(AppShortcut.ShortcutLocation)) {
                AppShortcut.CreateShortcut();
            }

            AppIconService.Initialize(new AppIconProvider());

            Toast.SetDefaultAction(() => (Current.Windows.OfType<ModernWindow>().FirstOrDefault(x => x.IsActive) ??
                    Current.MainWindow as ModernWindow)?.BringToFront());
            BbCodeBlock.ImageClicked += OnBbImageClick;
            BbCodeBlock.OptionEmojiProvider = new EmojiProvider();
            BbCodeBlock.OptionImageCacheDirectory = FilesStorage.Instance.GetTemporaryFilename("Images");
            BbCodeBlock.OptionEmojiCacheDirectory = FilesStorage.Instance.GetTemporaryFilename("Emoji");

            BbCodeBlock.AddLinkCommand(new Uri("cmd://findmissing/car"),
                    new DelegateCommand<string>(
                            id => {
                                WindowsHelper.ViewInBrowser(SettingsHolder.Content.MissingContentSearch.GetUri(id, SettingsHolder.MissingContentType.Car));
                            }));

            BbCodeBlock.AddLinkCommand(new Uri("cmd://findmissing/track"),
                    new DelegateCommand<string>(
                            id => {
                                WindowsHelper.ViewInBrowser(SettingsHolder.Content.MissingContentSearch.GetUri(id, SettingsHolder.MissingContentType.Track));
                            }));

            BbCodeBlock.AddLinkCommand(new Uri("cmd://downloadmissing/car"), new DelegateCommand<string>(id => {
                var s = id.Split('|');
                IndexDirectDownloader.DownloadCarAsync(s[0], s.ElementAtOrDefault(1)).Forget();
            }));

            BbCodeBlock.AddLinkCommand(new Uri("cmd://downloadmissing/track"), new DelegateCommand<string>(id => {
                var s = id.Split('|');
                IndexDirectDownloader.DownloadTrackAsync(s[0], s.ElementAtOrDefault(1)).Forget();
            }));

            BbCodeBlock.AddLinkCommand(new Uri("cmd://createneutrallut"),
                    new DelegateCommand<string>(id => { NeutralColorGradingLut.CreateNeutralLut(id.AsInt(16)); }));

            BbCodeBlock.DefaultLinkNavigator.PreviewNavigate += (sender, args) => {
                if (args.Uri.IsAbsoluteUri && args.Uri.Scheme == "acmanager") {
                    ArgumentsHandler.ProcessArguments(new[] { args.Uri.ToString() }).Forget();
                    args.Cancel = true;
                }
            };

            AppArguments.SetSize(AppFlag.ImagesCacheLimit, ref BetterImage.OptionCacheTotalSize);
            AppArguments.Set(AppFlag.ImagesMarkCached, ref BetterImage.OptionMarkCached);
            BetterImage.RemoteUserAgent = CmApiProvider.UserAgent;
            BetterImage.RemoteCacheDirectory = BbCodeBlock.OptionImageCacheDirectory;

            AppArguments.Set(AppFlag.UseVlcForAnimatedBackground, ref DynamicBackground.OptionUseVlc);
            Filter.OptionSimpleMatching = SettingsHolder.Content.SimpleFiltering;

            GameResultExtension.RegisterNameProvider(new GameSessionNameProvider());
            CarBlock.CustomShowroomWrapper = new CustomShowroomWrapper();
            CarBlock.CarSetupsView = new CarSetupsView();

            var acRootIsFine = Superintendent.Instance.IsReady && !AcRootDirectorySelector.IsReviewNeeded();
            if (acRootIsFine && SteamStarter.Initialize(AcRootDirectory.Instance.Value)) {
                if (SettingsHolder.Drive.SelectedStarterType != SettingsHolder.DriveSettings.SteamStarterType) {
                    SettingsHolder.Drive.SelectedStarterType = SettingsHolder.DriveSettings.SteamStarterType;
                    Toast.Show("Starter changed to replacement", "Enjoy Steam being included into CM");
                }
            } else if (SettingsHolder.Drive.SelectedStarterType == SettingsHolder.DriveSettings.SteamStarterType) {
                SettingsHolder.Drive.SelectedStarterType = SettingsHolder.DriveSettings.DefaultStarterType;
                Toast.Show($"Starter changed to {SettingsHolder.Drive.SelectedStarterType.DisplayName}", "Steam Starter is unavailable", () => {
                    ModernDialog.ShowMessage(
                            "To use Steam Starter, please make sure CM is taken place of the official launcher and AC root directory is valid.",
                            "Steam Starter is unavailable", MessageBoxButton.OK);
                });
            }

            InitializeUpdatableStuff();
            BackgroundInitialization();
            ExtraProgressRings.Initialize();

            FatalErrorMessage.Register(new AppRestartHelper());
            ImageUtils.SafeMagickWrapper = fn => {
                try {
                    return fn();
                } catch (OutOfMemoryException e) {
                    NonfatalError.Notify(ToolsStrings.MagickNet_CannotLoad, ToolsStrings.MagickNet_CannotLoad_Commentary, e);
                } catch (Exception e) {
                    NonfatalError.Notify(ToolsStrings.MagickNet_CannotLoad, e);
                }
                return null;
            };

            DataFileBase.ErrorsCatcher = new DataSyntaxErrorCatcher();
            AppArguments.Set(AppFlag.SharedMemoryLiveReadingInterval, ref AcSharedMemory.OptionLiveReadingInterval);
            AcSharedMemory.Initialize();

            AppArguments.Set(AppFlag.RunRaceInformationWebserver, ref PlayerStatsManager.OptionRunStatsWebserver);
            AppArguments.Set(AppFlag.RaceInformationWebserverFile, ref PlayerStatsManager.OptionWebserverFilename);
            PlayerStatsManager.Instance.SetListener();

            // AppArguments.Set(AppFlag.RhmKeepAlive, ref RhmService.OptionKeepRunning);
            RhmService.Instance.SetListener();

            _hibernator = new AppHibernator();
            _hibernator.SetListener();

            AppArguments.Set(AppFlag.TrackMapGeneratorMaxSize, ref TrackMapRenderer.OptionMaxSize);
            CommonFixes.Initialize();

            CmPreviewsTools.MissingShowroomHelper = new CarUpdatePreviewsDialog.MissingShowroomHelper();

            // paint shop+livery generator?
            LiteShowroomTools.LiveryGenerator = new LiveryGenerator();

            // discord
            if (AppArguments.Has(AppFlag.DiscordCmd)) {
                // Do not show main window and wait for futher instructions?
            }

            AppArguments.Set(AppFlag.DiscordVerbose, ref DiscordConnector.OptionVerboseMode);
            DiscordConnector.Initialize(AppArguments.Get(AppFlag.DiscordClientId) ?? InternalUtils.GetDiscordClientId(), new DiscordHandler());
            DiscordImage.OptionDefaultImage = "track_ks_brands_hatch";
            GameWrapper.Started += (sender, args) => {
                args.StartProperties.SetAdditional(new GameDiscordPresence(args.StartProperties, args.Mode));
            };
            /*new DiscordHandler().JoinRequest(new DiscordJoinRequest {
                UserId = "395654492135358465",
                AvatarUrl = "https://cdn.discordapp.com/avatars/395654492135358465/267c97d7c8f1bf20c18e7898d120b464.png",
                Discriminator = "1234",
                UserName = "Test user"
            }, CancellationToken.None, reply => {
                Logging.Debug(reply);
            });*/
            /*new DiscordRichPresence(10000, "Testing", "Testing") {
                LargeImage = new DiscordImage("", "Testing"),
                Party = new DiscordParty("party") {
                    JoinSecret = "join0"
                }
            };*/

            // reshade?
            var loadReShade = AppArguments.GetBool(AppFlag.ForceReshade);
            if (!loadReShade && string.Equals(AppArguments.Get(AppFlag.ForceReshade), "kn5only", StringComparison.OrdinalIgnoreCase)) {
                loadReShade = AppArguments.Values.Any(x => x.EndsWith(".kn5", StringComparison.OrdinalIgnoreCase));
            }

            if (loadReShade) {
                var reshade = Path.Combine(MainExecutingFile.Directory, "dxgi.dll");
                if (File.Exists(reshade)) {
                    Kernel32.LoadLibrary(reshade);
                }
            }

            // auto-show that thing
            InstallAdditionalContentDialog.Initialize();

            // let’s roll
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new AppUi(this, _tryingToRunFlag).Run();
        }

        private class CarSetupsView : ICarSetupsView {
            public void Open(CarObject car, CarSetupsRemoteSource forceRemoteSource = CarSetupsRemoteSource.None, bool forceNewWindow = false) {
                CarSetupsListPage.Open(car, forceRemoteSource, forceNewWindow);
            }
        }

        private class LiveryGenerator : ILiveryGenerator {
            public Task CreateLiveryAsync(CarSkinObject skin, Color[] colors, string preferredStyle) {
                return LiveryIconEditor.GenerateAsync(skin, colors, preferredStyle);
            }
        }

        private class DataSyntaxErrorCatcher : ISyntaxErrorsCatcher {
            public void Catch(DataFileBase file, int line) {
                if (file.Filename != null) {
                    NonfatalError.NotifyBackground(string.Format(ToolsStrings.SyntaxError_Unpacked, Path.GetFileName(file.Filename), line),
                            ToolsStrings.SyntaxError_Commentary, null, new[] {
                                new NonfatalErrorSolution(ToolsStrings.SyntaxError_Solution, null, token => {
                                    WindowsHelper.OpenFile(file.Filename);
                                    return Task.Delay(0, token);
                                })
                            });
                } else {
                    NonfatalError.NotifyBackground(string.Format(ToolsStrings.SyntaxError_Packed,
                            $"{file.Name} ({Path.GetFileName(file.Data?.Location ?? "?")})", line), ToolsStrings.SyntaxError_Commentary);
                }
            }
        }

        private static void PrepareUi() {
            try {
                ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(true));
                ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(500));
                ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(500));
                ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(60000));
                ItemsControl.IsTextSearchCaseSensitiveProperty.OverrideMetadata(typeof(ComboBox), new FrameworkPropertyMetadata(true));
            } catch (Exception e) {
                Logging.Error(e);
            }

            PopupHelper.Initialize();
            CommonCommands.SetHelper(new SomeCommonCommandsHelper());
        }

        private async void TestKey() {
            AppKeyHolder.Initialize(FilesStorage.Instance.GetFilename("License.txt"));
            if (AppKeyHolder.Instance.Revoked == null) return;

            await Task.Delay(3000);

            ValuesStorage.SetEncrypted(AppKeyDialog.AppKeyRevokedKey, AppKeyHolder.Instance.Revoked);
            AppKeyHolder.Instance.SetKey(null);

            Current.Dispatcher.Invoke(() => {
                if (Current?.MainWindow is MainWindow && Current.MainWindow.IsActive) {
                    AppKeyDialog.ShowRevokedMessage();
                }
            });
        }

        private static void RevertFileChanges() {
            PresetsPerModeBackup.Revert();
            WeatherSpecificCloudsHelper.Revert();
            WeatherSpecificTyreSmokeHelper.Revert();
            WeatherSpecificVideoSettingsHelper.Revert();
            CarSpecificControlsPresetHelper.Revert();
            CarCustomDataHelper.Revert();
            CopyFilterToSystemForOculusHelper.Revert();
        }

        private static async void BackgroundInitialization() {
            try {
#if DEBUG
                CupClient.Instance.LoadRegistries().Forget();
#endif

                await Task.Delay(500);
                AppArguments.Set(AppFlag.SimilarThreshold, ref CarAnalyzer.OptionSimilarThreshold);

                string additional = null;
                AppArguments.Set(AppFlag.SimilarAdditionalSourceIds, ref additional);
                if (!string.IsNullOrWhiteSpace(additional)) {
                    CarAnalyzer.OptionSimilarAdditionalSourceIds = additional.Split(';', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                }

                await Task.Delay(500);
                if (AppArguments.Has(AppFlag.TestIfAcdAvailable) && !Acd.IsAvailable) {
                    NonfatalError.NotifyBackground(@"This build can’t work with encrypted ACD-files");
                }

                if (AppUpdater.JustUpdated && SettingsHolder.Common.ShowDetailedChangelog) {
                    List<ChangelogEntry> changelog;
                    try {
                        changelog = await Task.Run(() =>
                                AppUpdater.LoadChangelog().Where(x => x.Version.IsVersionNewerThan(AppUpdater.PreviousVersion)).ToList());
                    } catch (WebException e) {
                        NonfatalError.NotifyBackground(AppStrings.Changelog_CannotLoad, ToolsStrings.Common_MakeSureInternetWorks, e);
                        return;
                    } catch (Exception e) {
                        NonfatalError.NotifyBackground(AppStrings.Changelog_CannotLoad, e);
                        return;
                    }

                    Logging.Debug("Changelog entries: " + changelog.Count);
                    if (changelog.Any()) {
                        Toast.Show(AppStrings.App_AppUpdated, AppStrings.App_AppUpdated_Details, () => {
                            ModernDialog.ShowMessage(changelog.Select(x => $@"[b]{x.Version}[/b]{Environment.NewLine}{x.Changes}")
                                                              .JoinToString(Environment.NewLine.RepeatString(2)), AppStrings.Changelog_RecentChanges_Title,
                                    MessageBoxButton.OK);
                        });
                    }
                }

                await Task.Delay(1500);
                RevertFileChanges();

                await Task.Delay(1500);
                CustomUriSchemeHelper.Initialize();

#if !DEBUG
                CupClient.Instance.LoadRegistries().Forget();
#endif

                await Task.Delay(5000);
                await Task.Run(() => {
                    foreach (var f in from file in Directory.GetFiles(FilesStorage.Instance.GetDirectory("Logs"))
                                      where file.EndsWith(@".txt") || file.EndsWith(@".log") || file.EndsWith(@".json")
                                      let info = new FileInfo(file)
                                      where info.LastWriteTime < DateTime.Now - TimeSpan.FromDays(3)
                                      select info) {
                        f.Delete();
                    }
                });
            } catch (Exception e) {
                Logging.Error(e);
            }
        }

        private void OnBbImageClick(object sender, BbCodeImageEventArgs e) {
            (e.BlockImages?.Select(x => x.Item1).Contains(e.ImageUrl) == true ?
                    new ImageViewer(e.BlockImages.Select(x => x.Item1), e.BlockImages.Select(x => x.Item1).IndexOf(e.ImageUrl),
                            details: i => e.BlockImages.FirstOrDefault(x => x.Item1 == i as string)?.Item2) {
                                HorizontalDetailsAlignment = HorizontalAlignment.Center
                            } :
                    new ImageViewer(e.ImageUrl)).ShowDialog();
        }

        private void InitializeUpdatableStuff() {
            DataUpdater.Initialize();
            DataUpdater.Instance.Updated += OnDataUpdated;

            if (AcRootDirectory.Instance.IsFirstRun) {
                AppUpdater.OnFirstRun();
            }

            AppUpdater.Initialize();
            AppUpdater.Instance.Updated += OnAppUpdated;

            if (LocaleHelper.JustUpdated) {
                Toast.Show(AppStrings.App_LocaleUpdated, string.Format(AppStrings.App_DataUpdated_Details, LocaleHelper.LoadedVersion));
            }

            LocaleUpdater.Initialize(LocaleHelper.LoadedVersion);
            LocaleUpdater.Instance.Updated += OnLocaleUpdated;
        }

        private void OnDataUpdated(object sender, EventArgs e) {
            Toast.Show(AppStrings.App_DataUpdated, string.Format(AppStrings.App_DataUpdated_Details, DataUpdater.Instance.InstalledVersion));
        }

        private void OnAppUpdated(object sender, EventArgs e) {
            Toast.Show(AppStrings.App_NewVersion,
                    string.Format(AppStrings.App_NewVersion_Details, AppUpdater.Instance.UpdateIsReady),
                    () => AppUpdater.Instance.FinishUpdateCommand.Execute(null));
        }

        private void OnLocaleUpdated(object sender, EventArgs e) {
            if (string.Equals(CultureInfo.CurrentUICulture.Name, SettingsHolder.Locale.LocaleName, StringComparison.OrdinalIgnoreCase)) {
                Toast.Show(AppStrings.App_LocaleUpdated, AppStrings.App_LocaleUpdated_Details, WindowsHelper.RestartCurrentApplication);
            }
        }

        private static void InitializePresets() {
            PresetsManager.Initialize(FilesStorage.Instance.GetDirectory("Presets"));
            DefaultPresets.Initialize();
            TrackStatesHelper.Initialize();
        }

        private void OnProcessExit(object sender, EventArgs e) {
            Logging.Flush();
            Storage.SaveBeforeExit();
            KunosCareerProgress.SaveBeforeExit();
            UserChampionshipsProgress.SaveBeforeExit();
            RhmService.Instance.Dispose();
            DiscordConnector.Instance.Dispose();
            Dispose();
        }

        public void Dispose() {
            DisposeHelper.Dispose(ref _hibernator);
        }
    }
}