using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using IndustrialEquipmentMonitoring.App.ViewModels;

namespace IndustrialEquipmentMonitoring.App;

public partial class MainWindow : Window
{
    private bool _closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        viewModel.Events.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, ScrollLogToEnd);
        Loaded += (_, _) => ScrollLogToEnd();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeRequested)
        {
            return;
        }

        e.Cancel = true;
        _closeRequested = true;

        if (DataContext is MainViewModel viewModel)
        {
            try
            {
                await viewModel.ShutdownAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        Close();
    }

    private void ScrollLogToEnd()
    {
        if (EventLog.Items.Count == 0)
        {
            return;
        }

        EventLog.ScrollIntoView(EventLog.Items[EventLog.Items.Count - 1]);
    }
}
