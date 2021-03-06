﻿using System;
using System.Windows;
using AcManager.Controls.ViewModels;
using AcManager.Tools.AcObjectsNew;
using AcManager.Tools.Filters.Testers;
using AcManager.Tools.Managers;
using AcManager.Tools.Objects;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Windows;
using StringBasedFilter;

namespace AcManager.Pages.Lists {
    public partial class ServerPresetsListPage : IParametrizedUriContent {
        public void OnUri(Uri uri) {
            var filter = uri.GetQueryParam("Filter");
            DataContext = new ViewModel(string.IsNullOrEmpty(filter) ? null : Filter.Create(ServerPresetObjectTester.Instance, filter));
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            ((ViewModel)DataContext).Load();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) {
            ((ViewModel)DataContext).Unload();
        }

        private class ViewModel : AcListPageViewModel<ServerPresetObject> {
            public ViewModel(IFilter<ServerPresetObject> listFilter) : base(ServerPresetsManager.Instance, listFilter) { }

            protected override string GetSubject() {
                return AppStrings.List_ServerPresets;
            }
        }

        protected override void OnItemDoubleClick(AcObjectNew obj) {
            var server = obj as ServerPresetObject;
            if (server == null) return;
            if (server.IsRunning) {
                server.StopServerCommand.Execute();
            } else {
                server.RunServerCommand.Execute();
            }
        }
    }
}
