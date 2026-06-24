namespace VoltStream.WPF.Debitors.Views;

using System.Windows.Controls;
using VoltStream.WPF.Commons.Services;
using VoltStream.WPF.Debitors.Models;

public partial class DebitorCreditorPage : Page
{
    private readonly IServiceProvider service;
    
    public DebitorCreditorPage(IServiceProvider service)
    {
        InitializeComponent();
        this.service = service;
        DataContext = new DebitorCreditorPageViewModel(service);
        
        Loaded += (s, e) => RegisterFocusNavigation();
    }
    
    private void RegisterFocusNavigation()
    {
        FocusNavigator.RegisterElements([
            cbxCustomer,
            cbxSign
        ]);
    }
}
