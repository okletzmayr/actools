using System.Collections.Generic;
using AcManager.Tools.Helpers.AcSettings;
using AcManager.Tools.Helpers.DirectInput;
using AcTools.DataFile;
using JetBrains.Annotations;

namespace AcManager.Tools.Helpers.AcSettingsControls {
    public class SystemButtonEntry : KeyboardButtonEntry {
        public SystemButtonEntry([LocalizationRequired(false)] string id, string name) : base(id, name) {}

        public override EntryLayer Layer => EntryLayer.CtrlShortcut;

        private string _displayInvertCombination;

        public string DisplayInvertCombination {
            get => _displayInvertCombination;
            set {
                if (Equals(value, _displayInvertCombination)) return;
                _displayInvertCombination = value;
                OnPropertyChanged();
            }
        }

        protected override void OnInputChanged(KeyboardInputButton oldValue, KeyboardInputButton newValue) {
            base.OnInputChanged(oldValue, newValue);
            DisplayInvertCombination = $"Ctrl+Shift+{newValue?.DisplayName}";
        }

        public override void Load(IniFile ini, IReadOnlyList<IDirectInputDevice> devices) {
            var section = ini[Id];
            Input = AcSettingsHolder.Controls.GetKeyboardInputButton(section.GetInt("KEY", -1));
        }

        public override void Save(IniFile ini) {
            var section = ini[Id];
            var input = Input;
            section.SetCommentary("KEY", input?.DisplayName);
            section.Set("KEY", input == null || !CheckValue(input.Id) ? @"-1" : @"0x" + input.Id.ToString(@"X"));
        }
    }
}