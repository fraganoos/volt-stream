namespace VoltStream.WPF.Products.Views;

using System.Windows.Controls;
using VoltStream.WPF.Commons.Services;
using VoltStream.WPF.Products.Models;

public partial class ProductsPage : Page
{
    private ProductPageViewModel vm;
    private readonly IServiceProvider services;
    
    public ProductsPage(IServiceProvider services)
    {
        InitializeComponent();
        this.services = services;
        vm = new ProductPageViewModel(services);
        DataContext = vm;
        
        Loaded += (s, e) => RegisterFocusNavigation();
    }
    
    private void RegisterFocusNavigation()
    {
        FocusNavigator.RegisterElements([
            cbxCategory,
            cbxProductName
        ]);
    }
}
