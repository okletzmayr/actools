﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AcManager.Tools.Helpers;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using JetBrains.Annotations;
using Newtonsoft.Json;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local

namespace AcManager.Tools.Managers.Plugins {
    [JsonObject(MemberSerialization.OptIn)]
    public class PluginEntry : NotifyPropertyChanged, IWithId, IProgress<double?> {
        [Localizable(false)]
        public static readonly Tuple<string, string>[] SupportedVersions = {
            Tuple.Create("Magick", ""),
            Tuple.Create("Awesomium", ""),
            Tuple.Create("SSE", "1.4.2.1"),
        };

        [JsonProperty(PropertyName = @"id")]
        public string Id { get; private set; }

        [JsonProperty(PropertyName = @"name")]
        private string _name;

        [JsonProperty(PropertyName = @"hidden")]
        private bool _hidden;

        [JsonProperty(PropertyName = @"recommended")]
        private bool? _isRecommended;

        [JsonProperty(PropertyName = @"platform"), CanBeNull]
        private string _platform;

        [JsonProperty(PropertyName = @"description")]
        private string _description;

        [JsonProperty(PropertyName = @"version")]
        private string _version;

        [JsonProperty(PropertyName = @"installedVersion")]
        private string _installedVersion;

        [JsonProperty(PropertyName = @"size")]
#pragma warning disable 649
        private int _size;
#pragma warning restore 649

        [Localizable(false),JsonProperty(PropertyName = @"appVersion")]
        public string AppVersion { get; private set; }

        public string Name {
            get => _name;
            set {
                if (value == _name) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        public string DisplaySize => LocalizationHelper.ToReadableSize(_size);
        public string KeyEnabled => $"_appAddon__{Id}__enabled";

        public bool AvailableToInstall => !IsInstalled && !InstallationInProgress;
        public bool IsInstalled => _installedVersion != null;

        public bool IsEnabled {
            get => ValuesStorage.GetBool(KeyEnabled, true) && !IsObsolete;
            set {
                if (value == IsEnabled) return;
                if (value) {
                    ValuesStorage.Remove(KeyEnabled);
                } else {
                    ValuesStorage.Set(KeyEnabled, false);
                }

                PluginsManager.Instance.OnPluginEnabled(this, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsReady));
            }
        }

        public bool IsAvailable => !BuildInformation.AppVersion.IsVersionOlderThan(AppVersion);

        public bool CanWork => IsAvailable && !IsObsolete;

        /// <summary>
        /// Addon is installed and enabled.
        /// </summary>
        public bool IsReady => IsInstalled && IsEnabled;

        private bool _isInstalling;

        public bool IsInstalling {
            get => _isInstalling;
            set {
                if (value == _isInstalling) return;
                _isInstalling = value;
                OnPropertyChanged();
                _installCommand?.RaiseCanExecuteChanged();
            }
        }

        public bool HasUpdate => IsInstalled && Version.IsVersionNewerThan(InstalledVersion);

        public string InstalledVersion {
            get => _installedVersion;
            set {
                if (value == _installedVersion) return;
                _installedVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(AvailableToInstall));
                OnPropertyChanged(nameof(HasUpdate));
                _installCommand?.RaiseCanExecuteChanged();

                UpdateObsolete();
            }
        }

        public string Description {
            get => _description;
            set {
                if (value == _description) return;
                _description = value;
                OnPropertyChanged();
            }
        }

        public bool IsRecommended {
            get => _isRecommended ?? (_isRecommended = Id != "Awesomium" && Id != "VLC").Value;
            set {
                if (Equals(value, _isRecommended)) return;
                _isRecommended = value;
                OnPropertyChanged();
            }
        }

        public bool IsHidden {
            get => _hidden;
            set {
                if (Equals(value, _hidden)) return;
                _hidden = value;
                OnPropertyChanged();
            }
        }

        public string Version {
            get => _version;
            set {
                if (value == _version) return;
                _version = value;
                OnPropertyChanged();
            }
        }

        public string Platform {
            get => _platform;
            set {
                if (value == _platform) return;
                _platform = value;
                OnPropertyChanged();
            }
        }

        public bool PlatformFits => _platform == null || _platform == BuildInformation.Platform;

        private bool _installationInProgress;

        public bool InstallationInProgress {
            get => _installationInProgress;
            set {
                if (Equals(value, _installationInProgress)) return;
                _installationInProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvailableToInstall));
            }
        }

        private bool _downloadProgressIndeterminate;

        public bool DownloadProgressIndeterminate {
            get => _downloadProgressIndeterminate;
            set {
                if (Equals(value, _downloadProgressIndeterminate)) return;
                _downloadProgressIndeterminate = value;
                OnPropertyChanged();
            }
        }

        private double _downloadProgress;

        public double DownloadProgress {
            get => _downloadProgress;
            set {
                if (Equals(value, _downloadProgress)) return;
                _downloadProgress = value;
                OnPropertyChanged();
            }
        }

        public void Report(double? value) {
            var v = value ?? 0d;
            DownloadProgressIndeterminate = Equals(v, 0d);
            DownloadProgress = v;
        }

        private PluginEntry(string id) {
            Id = id;
            AppVersion = "0";
        }

        [JsonConstructor, UsedImplicitly]
        private PluginEntry(string id, string version) {
            Id = id;
            Version = version;
            UpdateObsolete();
        }

        private bool _isObsolete;

        public bool IsObsolete {
            get => _isObsolete;
            set {
                if (value == _isObsolete) return;
                _isObsolete = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(CanWork));
            }
        }

        private void UpdateObsolete() {
            var supported = SupportedVersions.FirstOrDefault(x => x.Item1 == Id);
            IsObsolete = supported != null && (string.IsNullOrEmpty(supported.Item2) || supported.Item2.IsVersionNewerThan(InstalledVersion ?? Version));
        }

        private AsyncCommand _installCommand;

        public AsyncCommand InstallCommand => _installCommand ??
                (_installCommand = new AsyncCommand(Install, () => (!IsInstalled || HasUpdate) && !IsInstalling));

        private CancellationTokenSource _cancellation;

        private async Task Install() {
            using (_cancellation = new CancellationTokenSource()) {
                Report(0d);

                InstallationInProgress = true;

                try {
                    await PluginsManager.Instance.InstallPlugin(this, this, _cancellation.Token);
                } finally {
                    InstallationInProgress = false;
                }
            }
            _cancellation = null;
        }

        private DelegateCommand _cancellationCommand;

        public DelegateCommand CancellationCommand => _cancellationCommand ?? (_cancellationCommand = new DelegateCommand(() => {
            _cancellation?.Cancel();
        }));

        public bool IsAllRight => Id != null && Name != null && Version != null && PlatformFits;

        [JsonIgnore]
        public string Directory => PluginsManager.Instance.GetPluginDirectory(Id);

        public string GetFilename([Localizable(false)] string fileId) {
            return PluginsManager.Instance.GetPluginFilename(Id, fileId);
        }
    }
}
