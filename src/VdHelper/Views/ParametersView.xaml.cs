using System.Windows.Controls;

namespace VdHelper.Views;

public partial class ParametersView : UserControl
{
    public ParametersView()
    {
        InitializeComponent();
        Loaded += (_, _) => ParametersViewModel.Current.Load();
    }
}