using CommunityToolkit.Mvvm.ComponentModel;
using LGSTrayPrimitives;
using Microsoft.Win32;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace LGSTrayUI
{
    /// <summary>
    /// User settings of the tray (selected devices, numeric icon), stored in HKCU\Software\LGSTray.
    /// Before 3.0.6 they were .NET user settings (user.config), whose folder depends on the program version
    /// and the exact program path - so the selection was lost after an update or when started via a
    /// differently spelled path. On first start, the old values are taken over once.
    /// </summary>
    public partial class UserSettingsWrapper : ObservableObject
    {
        private const string KeyPath = @"Software\LGSTray";
        private const string SelectedDevicesValue = "SelectedDevices";
        private const string NumericDisplayValue = "NumericDisplay";

        private readonly StringCollection _selectedDevices = [];
        private bool _numericDisplay;

        public UserSettingsWrapper()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
                if (key?.GetValue(SelectedDevicesValue) is string[] devices)
                {
                    _selectedDevices.AddRange(devices.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray());
                    _numericDisplay = key.GetValue(NumericDisplayValue) is int n && n != 0;
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("User settings could not be read from the registry", ex);
            }

            // First start with 3.0.6: take over the old .NET user settings once.
            try
            {
                var old = Properties.Settings.Default.SelectedDevices;
                if (old != null)
                {
                    _selectedDevices.AddRange(old.Cast<string?>().Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray()!);
                }
                _numericDisplay = Properties.Settings.Default.NumericDisplay;
            }
            catch (Exception ex)
            {
                AppLog.Write("Old user settings (user.config) could not be read", ex);
            }
            Save();
        }

        public StringCollection SelectedDevices => _selectedDevices;

        public bool HasSelectedDevices => _selectedDevices.Count > 0;

        public bool NumericDisplay
        {
            get => _numericDisplay;
            set
            {
                _numericDisplay = value;
                Save();

                OnPropertyChanged();
            }
        }

        public void AddDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || _selectedDevices.Contains(deviceId))
            {
                return;
            }

            _selectedDevices.Add(deviceId);
            Save();

            OnPropertyChanged(nameof(SelectedDevices));
        }

        public void RemoveDevice(string deviceId)
        {
            _selectedDevices.Remove(deviceId);
            Save();

            OnPropertyChanged(nameof(SelectedDevices));
        }

        private void Save()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
                key.SetValue(SelectedDevicesValue, _selectedDevices.Cast<string>().ToArray(), RegistryValueKind.MultiString);
                key.SetValue(NumericDisplayValue, _numericDisplay ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLog.Write("User settings could not be saved to the registry", ex);
            }
        }
    }
}
