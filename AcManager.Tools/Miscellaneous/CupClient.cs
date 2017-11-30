﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AcManager.Tools.AcManagersNew;
using AcManager.Tools.AcObjectsNew;
using AcManager.Tools.ContentInstallation;
using AcManager.Tools.Helpers;
using AcManager.Tools.Helpers.Api;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI.Dialogs;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcManager.Tools.Miscellaneous {
    public class CupClient {
        private static ApiCacheThing _cache;
        private static ApiCacheThing Cache => _cache ?? (_cache = new ApiCacheThing("CUP", TimeSpan.FromHours(1)));

        public static CupClient Instance { get; private set; }

        public static void Initialize() {
            Debug.Assert(Instance == null);
            Instance = new CupClient();
        }

        private CupClient() { }

        private Dictionary<CupContentType, IAcManagerNew> _managers = new Dictionary<CupContentType, IAcManagerNew>();

        [CanBeNull]
        public IAcManagerNew GetAssociatedManager(CupContentType type) {
            return _managers.TryGetValue(type, out var result) ? result : null;
        }

        public void Register<T>(BaseAcManager<T> manager, CupContentType type) where T : AcObjectNew, ICupSupportedObject {
            _managers.Add(type, manager);
            Instance.NewLatestVersion += (sender, args) => {
                if (args.Key.Type != type) return;
                var obj = manager.GetWrapperById(args.Key.Id);
                if (obj == null || !obj.IsLoaded) return;
                ((ICupSupportedObject)obj.Value).OnCupUpdateAvailableChanged();
            };
        }

        public void LoadRegistries() {
            CheckRegistriesAsync().Forget();
        }

        private async Task CheckRegistriesAsync() {
            foreach (var registry in SettingsHolder.Content.CupRegistries.Split(new[] { ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                try {
                    await LoadList(registry);
                } catch (Exception e) {
                    NonfatalError.NotifyBackground("Can’t check registry for updates", e);
                }
            }
        }

        public class CupKey {
            public readonly CupContentType Type;

            [NotNull]
            public readonly string Id;

            public CupKey(CupContentType type, [NotNull] string id) {
                Type = type;
                Id = id ?? throw new ArgumentNullException(nameof(id));
            }

            protected bool Equals(CupKey other) {
                return Type == other.Type && string.Equals(Id, other.Id);
            }

            public override bool Equals(object obj) {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj.GetType() == GetType() && Equals((CupKey)obj);
            }

            public override int GetHashCode() {
                unchecked {
                    return ((int)Type * 397) ^ Id.GetHashCode();
                }
            }

            public override string ToString() {
                return $"{Type}/{Id}";
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class CupInformation : NotifyPropertyChanged {
            [NotNull]
            public string Version { get; }

            public bool IsLimited { get; }

            internal string SourceRegistry;
            internal bool IsExtendedLoaded;

            #region Extra properties
            [CanBeNull, JsonProperty("name")]
            public string Name { get; private set; }

            [CanBeNull, JsonProperty("author")]
            public string Author { get; private set; }

            [CanBeNull, JsonProperty("changelog")]
            public string Changelog { get; private set; }

            [JsonProperty("cleanInstallation")]
            public bool PreferCleanInstallation { get; private set; }

            [CanBeNull, JsonProperty("informationUrl")]
            public string InformationUrl { get; private set; }

            [CanBeNull, JsonProperty("updateUrl")]
            // ReSharper disable once InconsistentNaming
            internal string _updateUrl { get; set; }

            [CanBeNull, JsonProperty("alternativeIds")]
            public string[] AlternativeIds { get; private set; }
            #endregion

            [JsonConstructor]
            private CupInformation(string version, bool limited) {
                Version = version;
                IsLimited = limited;
            }

            public CupInformation([NotNull] string version, bool isLimited, string sourceRegistry) {
                Version = version ?? throw new ArgumentNullException(nameof(version));
                IsLimited = isLimited;
                SourceRegistry = sourceRegistry;
            }

            public override string ToString() {
                return $"(version={Version}; limited={IsLimited})";
            }
        }

        public bool ContainsAnUpdate(CupContentType type, [NotNull] string id, [CanBeNull] string installedVersion) {
            return _versions.TryGetValue(new CupKey(type, id), out var information) && information.Version.IsVersionNewerThan(installedVersion);
        }

        private async Task LoadExtendedInformation(CupKey key, string registry) {
            try {
                var data = JsonConvert.DeserializeObject<CupInformation>(await Cache.GetStringAsync($"{registry}/{key.Type.ToString().ToLowerInvariant()}/{key.Id}"));
                data.SourceRegistry = registry;
                data.IsExtendedLoaded = true;
                RegisterLatestVersion(key, data);
            } catch (Exception e) {
                Logging.Warning(e);
            }
        }

        [CanBeNull]
        public CupInformation GetInformation(CupContentType type, [NotNull] string id) {
            var key = new CupKey(type, id);
            if (!_versions.TryGetValue(key, out var data)) return null;

            if (!data.IsExtendedLoaded) {
                data.IsExtendedLoaded = true;
                LoadExtendedInformation(key, data.SourceRegistry).Forget();
            }

            return data;
        }

        private readonly TaskCache _installationUrlTaskCache = new TaskCache();

        [ItemCanBeNull]
        public Task<string> GetUpdateUrlAsync(CupContentType type, [NotNull] string id) {
            var information = GetInformation(type, id);
            if (information == null || information._updateUrl != null) return Task.FromResult(information?._updateUrl);

            var url = $"{information.SourceRegistry}/{type.ToString().ToLowerInvariant()}/{id}/get";
            return _installationUrlTaskCache.Get(async () => {
                var updateUrl = await CookieAwareWebClient.GetFinalRedirectAsync(url);
                if (updateUrl != null) {
                    information._updateUrl = updateUrl;
                }
                return updateUrl;
            }, url);
        }

        private readonly TaskCache _installTaskCache = new TaskCache();

        public Task<bool> InstallUpdateAsync(CupContentType type, [NotNull] string id, CancellationToken cancellationToken) {
            var information = GetInformation(type, id);
            if (information == null) return Task.FromResult(false);

            if (information.IsLimited) {
                WindowsHelper.ViewInBrowser(information.InformationUrl);
                return Task.FromResult(false);
            }

            return _installTaskCache.Get(async () => {
                var updateUrl = await GetUpdateUrlAsync(type, id);
                var idsToUpdate = new[] { id }.Append(information.AlternativeIds ?? new string[0]).ToArray();
                return updateUrl != null && !cancellationToken.IsCancellationRequested &&
                        await ContentInstallationManager.Instance.InstallAsync(updateUrl, new ContentInstallationParams {
                            CupType = type,
                            IdsToUpdate = idsToUpdate,
                            DisplayName = information.Name,
                            Author = information.Author,
                            Version = information.Version,
                            InformationUrl = information.InformationUrl,
                            PreferCleanInstallation = information.PreferCleanInstallation,
                            PostInstallation = {
                                async (progress, token) => {
                                    var manager = GetAssociatedManager(type);
                                    if (manager == null) return;

                                    progress.Report(new AsyncProgressEntry("Syncing versions…", 0.9999));
                                    await Task.Delay(1000);
                                    foreach (var cupSupportedObject in idsToUpdate.Select(x => manager.GetObjectById(x) as ICupSupportedObject).NonNull()) {
                                        Logging.Debug($"Set values: {cupSupportedObject}={information.Version}");
                                        cupSupportedObject.SetValues(information.Author, information.InformationUrl, information.Version);
                                    }
                                }
                            }
                        });
            });
        }

        public void IgnoreUpdate(CupContentType type, [NotNull] string id) {
            // TODO
        }

        public void IgnoreAllUpdates(CupContentType type, [NotNull] string id) {
            // TODO
        }

        private readonly Dictionary<CupKey, CupInformation> _versions = new Dictionary<CupKey, CupInformation>();

        public event EventHandler<CupEventArgs> NewLatestVersion;

        private void RegisterLatestVersion([NotNull] CupKey key, [NotNull] CupInformation information) {
            if (_versions.TryGetValue(key, out var existing) && existing.Version.IsVersionNewerThan(information.Version)) {
                return;
            }

            _versions[key] = information;
            NewLatestVersion?.Invoke(this, new CupEventArgs(key, information));
        }

        private async Task LoadList(string registry) {
            var list = JObject.Parse(await Cache.GetStringAsync(registry + "/list"));
            foreach (var p in list) {
                if (!Enum.TryParse<CupContentType>(p.Key, true, out var type)) {
                    Logging.Warning("Not supported type: " + type);
                    continue;
                }

                if (!(p.Value is JObject obj)) {
                    Logging.Warning("Not supported format: " + type);
                    continue;
                }

                foreach (var item in obj) {
                    try {
                        var key = new CupKey(type, item.Key);
                        if (item.Value.Type == JTokenType.String) {
                            RegisterLatestVersion(key, new CupInformation((string)item.Value, false, registry));
                        } else if (item.Value is JObject details) {
                            RegisterLatestVersion(key, new CupInformation((string)details["version"], (bool)details["limited"], registry));
                        } else {
                            Logging.Warning("Not supported format: " + type + "/" + item.Key);
                        }
                    } catch (Exception e) {
                        Logging.Warning("Not supported format: " + type + "/" + item.Key);
                        Logging.Warning(e);
                    }
                }
            }
        }
    }
}