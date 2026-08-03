using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using AutoRestartVoicemeeter.Core;

namespace AutoRestartVoicemeeter.UI;

public partial class DeviceSelectionWindow : Window
{
    private readonly AppSettings _settings;
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();
    private ICollectionView? _deviceView;

    public DeviceSelectionWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        _deviceView = CollectionViewSource.GetDefaultView(DiscoveredDevices);
        _deviceView.Filter = FilterDeviceItem;
        _deviceView.SortDescriptions.Add(new SortDescription(nameof(DiscoveredDevice.IsSelected), ListSortDirection.Descending));
        _deviceView.SortDescriptions.Add(new SortDescription(nameof(DiscoveredDevice.Name), ListSortDirection.Ascending));

        DeviceListView.ItemsSource = _deviceView;
        LoadDevices();
    }

    private void LoadDevices()
    {
        foreach (var dev in DiscoveredDevices)
        {
            dev.PropertyChanged -= OnDevicePropertyChanged;
        }

        DiscoveredDevices.Clear();

        // Reload fresh settings from disk to reflect saved TargetDevices state
        var freshSettings = AppSettings.Load();
        _settings.TargetDevices = freshSettings.TargetDevices;

        var devices = DeviceEnumerator.GetDiscoveredDevices(_settings);

        foreach (var dev in devices)
        {
            dev.PropertyChanged += OnDevicePropertyChanged;
            DiscoveredDevices.Add(dev);
        }

        // Also add existing custom keyword/pattern target devices from settings if not already present
        foreach (var target in _settings.TargetDevices)
        {
            if (!DiscoveredDevices.Any(d => string.Equals(d.DeviceCode, target.DeviceCode, StringComparison.OrdinalIgnoreCase)))
            {
                var customDev = new DiscoveredDevice
                {
                    Name = target.Name,
                    DeviceCode = target.DeviceCode,
                    Type = target.Type,
                    IsSelected = target.IsEnabled
                };
                customDev.PropertyChanged += OnDevicePropertyChanged;
                DiscoveredDevices.Add(customDev);
            }
        }

        _deviceView?.Refresh();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscoveredDevice.IsSelected))
        {
            _deviceView?.Refresh();
        }
    }

    private void CardBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Ignore if click originated directly on the CheckBox to avoid double-toggle
        if (e.OriginalSource is DependencyObject original && FindParent<System.Windows.Controls.CheckBox>(original) != null)
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is DiscoveredDevice dev)
        {
            dev.IsSelected = !dev.IsSelected;
            _deviceView?.Refresh();
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typedParent) return typedParent;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private bool FilterDeviceItem(object item)
    {
        if (item is not DiscoveredDevice dev) return false;

        string query = SearchTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return dev.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               dev.DeviceCode.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchTextBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

        _deviceView?.Refresh();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Clear();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDevices();
        Logger.Instance.Log("Refreshed discovered devices list.", LogLevel.Info);
    }

    private void AddCustomCode_Click(object sender, RoutedEventArgs e)
    {
        string code = CustomCodeTextBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            System.Windows.MessageBox.Show("Please enter a valid device code or pattern.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DiscoveredDevices.Any(d => string.Equals(d.DeviceCode, code, StringComparison.OrdinalIgnoreCase)))
        {
            System.Windows.MessageBox.Show("Device code or pattern already exists in the list.", "Duplicate Pattern", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newDev = new DiscoveredDevice
        {
            Name = $"Custom Pattern: {code}",
            DeviceCode = code,
            Type = DeviceFilterType.Keyword,
            IsSelected = true
        };
        newDev.PropertyChanged += OnDevicePropertyChanged;

        DiscoveredDevices.Add(newDev);
        _deviceView?.Refresh();
        CustomCodeTextBox.Clear();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.TargetDevices.Clear();

        foreach (var dev in DiscoveredDevices)
        {
            if (dev.IsSelected)
            {
                _settings.TargetDevices.Add(new DeviceFilter
                {
                    Name = dev.Name,
                    DeviceCode = dev.DeviceCode,
                    Type = dev.Type,
                    IsEnabled = true
                });
            }
        }

        _settings.EnsureDefaultFallback();
        _settings.Save();

        Logger.Instance.Log($"✓ Updated target devices selection ({_settings.TargetDevices.Count} active rules).", LogLevel.Success);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
