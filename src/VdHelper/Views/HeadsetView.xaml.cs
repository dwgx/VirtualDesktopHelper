using System.Windows.Controls;

namespace VdHelper.Views;

public partial class HeadsetView : UserControl
{
    public HeadsetView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await HeadsetViewModel.Current.LoadAsync();
    }
}