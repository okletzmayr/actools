﻿using System;
using System.Windows.Forms;
using FirstFloor.ModernUI.Helpers;
using JetBrains.Annotations;

namespace FirstFloor.ModernUI.Windows {
    public static class Toast {
        public static bool OptionFallbackMode = false;

        private static bool _winToasterIsNotAvailable;
        private static Action _defaultAction;

        public static void SetDefaultAction(Action defaultAction) {
            _defaultAction = defaultAction;
        }

        /// <summary>
        /// Show a toast.
        /// </summary>
        /// <param name="title">Ex.: “Something Happened”</param>
        /// <param name="message">Ex.: “This and that. Without dot in the end”</param>
        /// <param name="click">Click action</param>
        public static void Show(string title, string message, Action click = null) {
            Show(title.ToTitle(), message, AppIconService.GetAppIconUri(), click ?? _defaultAction);
        }

        /// <summary>
        /// Show a toast.
        /// </summary>
        /// <param name="title">Ex.: “Something Happened”</param>
        /// <param name="message">Ex.: “This and that. Without dot in the end”</param>
        /// <param name="icon">Uri to some icon</param>
        /// <param name="click">Click action</param>
        public static void Show(string title, string message, [NotNull] Uri icon, Action click = null) {
            ActionExtension.InvokeInMainThreadAsync(() => {
                if (!_winToasterIsNotAvailable && !OptionFallbackMode) {
                    try {
                        ToastWin8Helper.ShowToast(title, message, icon, click ?? _defaultAction);
                        return;
                    } catch {
                        Logging.Warning("Win8 Toaster is not available");
                        _winToasterIsNotAvailable = true;
                    }
                }

                ShowFallback(title, message, icon, click);
            });
        }

        private static void ShowFallback(string title, string message, [NotNull] Uri icon, Action click) {
            try {
                var text = title + Environment.NewLine + message;
                var notifyIcon = new NotifyIcon {
                    Icon = AppIconService.GetTrayIcon(),
                    Text = text.Length > 63 ? text.Substring(0, 63) : text,
                    Visible = true
                };

                if (click != null) {
                    notifyIcon.BalloonTipClicked += (sender, args) => {
                        click.InvokeInMainThreadAsync();
                    };
                }

                notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
                notifyIcon.BalloonTipClosed += (sender, args) => {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                };
            } catch (Exception e) {
                Logging.Error(e);
            }
        }
    }
}
