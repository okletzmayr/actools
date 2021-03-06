﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using AcTools.Windows;
using HidLibrary;
using JetBrains.Annotations;

namespace AcTools.WheelAngles.Implementations {
    internal class LogitechG25 : IWheelSteerLockSetter {
        public virtual string ControllerName => "Logitech G25";

        public virtual bool Test(string productGuid) {
            return string.Equals(productGuid, "C299046D-0000-0000-0000-504944564944", StringComparison.OrdinalIgnoreCase);
        }

        public int MaximumSteerLock => 900;
        public int MinimumSteerLock => 40;

        public bool Apply(int steerLock, bool isReset, out int appliedValue) {
            if (isReset) {
                // Don’t need to reset, Logitech does that for you as soon as AC is closed. Now, that’s neat.
                appliedValue = steerLock;
                return true;
            }

            if (!LoadLogitechSteeringWheelDll()) {
                appliedValue = 0;
                return false;
            }

            appliedValue = Math.Min(Math.Max(steerLock, MinimumSteerLock), MaximumSteerLock);

            // Actual range will be changed as soon as game is started, we need to wait first.
            // Why not run .Apply() then? Because configs should be prepared BEFORE game is started,
            // and they depend on a steer lock app has set. Of course, it’s not a perfect solution
            // (what if .ApplyLater() will fail?), but it’s the only option I see at the moment.
            ApplyLater(appliedValue);
            return true;
        }

        #region SDK-related stuff
        private const string LogitechSteeringWheel = "LogitechSteeringWheel.dll";
        private static bool? _logitechDllInitialized;

        private static IEnumerable<string> LocateLogitechSteeringWheelDll () {
            // For 32-bit apps

            var programFileses = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).Replace(" (x86)", ""),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            }.Distinct().ToArray();

            var appPaths = new[] {
                Path.Combine("Logitech", "Gaming Software"),
                "Logitech Gaming Software"
            };

            var sdkFolders = new[] {
                "SDKs",
                "SDK",
            };

            var platforms = new[] {
                "32", "x86", "64", "x64", null
            };

            foreach (var programFiles in programFileses)
            foreach (var appPath in appPaths)
            foreach (var sdkFolder in sdkFolders)
            foreach (var platform in platforms) {
                yield return new[] { programFiles, appPath, sdkFolder, platform }.NonNull().JoinToString(Path.DirectorySeparatorChar);
            }
        }

        private static bool LoadLogitechSteeringWheelDll() {
            if (_logitechDllInitialized.HasValue) return _logitechDllInitialized.Value;

            // Library is in PATH, next to executable, somewhere in system or in a list of libraries to load, nice.
            try {
                if (Kernel32.LoadLibrary(LogitechSteeringWheel) != IntPtr.Zero) {
                    return (_logitechDllInitialized = true).Value;
                }
            } catch (Exception e) {
                AcToolsLogging.Write($"Failed to load: {e.Message}");
            }

            // Trying to find the library in Logitech Gaming Software installation…
            AcToolsLogging.Write($"Trying to locate {LogitechSteeringWheel}…");

            foreach (var location in LocateLogitechSteeringWheelDll().Where(File.Exists)) {
                AcToolsLogging.Write($"Found: {location}");
                try {
                    Kernel32.LoadLibrary(location);
                    return (_logitechDllInitialized = true).Value;
                } catch (Exception e) {
                    AcToolsLogging.Write($"Failed to load: {e.Message}");
                }
            }

            AcToolsLogging.NonFatalErrorNotifyBackground($"Failed to find “{LogitechSteeringWheel}”",
                    "Please, make sure you have Logitech Gaming Software installed, or simply put that library next to executable.");
            return (_logitechDllInitialized = false).Value;
        }

        private static int _applyId;

        private async void ApplyLater(int value) {
            var id = ++_applyId;
            Process process = null;

            for (var i = 0; i < 30; i++) {
                await Task.Delay(300);
                process = AcProcess.TryToFind();
                if (_applyId != id) return;
                if (process != null) break;
            }

            if (process == null) {
                AcToolsLogging.NonFatalErrorNotifyBackground($"Can’t set {ControllerName} steer lock", "Failed to find game process");
                return;
            }

            await Task.Delay(2000);
            if (process.HasExitedSafe()) {
                AcToolsLogging.Write("Process finished");
                return;
            }

            LogiSteeringInitializeWithWindow(true, process.MainWindowHandle);
            AcToolsLogging.Write("Initialized");

            var properties = new LogiControllerPropertiesData();
            LogiGetCurrentControllerProperties(0, ref properties);
            AcToolsLogging.Write("WheelRange: " + properties.WheelRange);
            AcToolsLogging.Write("ForceEnable: " + properties.ForceEnable);
            AcToolsLogging.Write("OverallGain: " + properties.OverallGain);
            AcToolsLogging.Write("SpringGain: " + properties.SpringGain);
            AcToolsLogging.Write("DamperGain: " + properties.DamperGain);
            AcToolsLogging.Write("DefaultSpringEnabled: " + properties.DefaultSpringEnabled);
            AcToolsLogging.Write("DefaultSpringGain: " + properties.DefaultSpringGain);
            AcToolsLogging.Write("CombinePedals: " + properties.CombinePedals);
            AcToolsLogging.Write("GameSettingsEnabled: " + properties.GameSettingsEnabled);
            AcToolsLogging.Write("AllowGameSettings: " + properties.AllowGameSettings);
            properties.WheelRange = value;
            LogiSetPreferredControllerProperties(properties);
            LogiUpdate();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct LogiControllerPropertiesData {
            public bool ForceEnable;
            public int OverallGain;
            public int SpringGain;
            public int DamperGain;
            public bool DefaultSpringEnabled;
            public int DefaultSpringGain;
            public bool CombinePedals;
            public int WheelRange;
            public bool GameSettingsEnabled;
            public bool AllowGameSettings;
        }

        [DllImport(LogitechSteeringWheel, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiSteeringInitializeWithWindow(bool ignoreXInputControllers, IntPtr windowHandle);

        [DllImport(LogitechSteeringWheel, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiUpdate();

        [DllImport(LogitechSteeringWheel, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiSetPreferredControllerProperties(LogiControllerPropertiesData properties);

        [DllImport(LogitechSteeringWheel, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiGetCurrentControllerProperties(int index, ref LogiControllerPropertiesData properties);
        #endregion
    }
}