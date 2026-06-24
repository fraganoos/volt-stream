namespace VoltStream.WPF.Turnovers.Models;

using ApiServices.Enums;
using ApiServices.Extensions;
using ApiServices.Interfaces;
using ApiServices.Models.Responses;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoltStream.WPF.Commons;
using VoltStream.WPF.Commons.Messages;
using VoltStream.WPF.Commons.Services;
using VoltStream.WPF.Commons.ViewModels;
using VoltStream.WPF.Payments.Views;
using VoltStream.WPF.Sales.Views;

public partial class TurnoversPageViewModel : ViewModelBase
{
    private readonly ICustomersApi customersApi;
    private readonly ICustomerOperationsApi customerOperationsApi;
    private readonly IMapper mapper;
    private readonly IServiceProvider services;
    private readonly INavigationService navigationService;

    private ObservableCollection<CustomerOperationForDisplayViewModel> allOperationsForDisplay = [];

    public TurnoversPageViewModel(IServiceProvider services, INavigationService navigationService)
    {
        this.services = services;
        this.navigationService = navigationService;
        customersApi = services.GetRequiredService<ICustomersApi>();
        customerOperationsApi = services.GetRequiredService<ICustomerOperationsApi>();
        mapper = services.GetRequiredService<IMapper>();

        WeakReferenceMessenger.Default.Register<EntityUpdatedMessage<string>>(this, (r, m) =>
        {
            if (m.Value == "OperationUpdated")
            {
                _ = LoadCustomerOperationsForSelectedCustomerAsync();
            }
        });

        _ = LoadInitialDataAsync();
    }

    [ObservableProperty] private CustomerResponse? selectedCustomer;
    [ObservableProperty] private ObservableCollection<CustomerResponse> customers = [];
    [ObservableProperty] private ObservableCollection<CustomerOperationViewModel> customerOperations = [];
    [ObservableProperty] private ObservableCollection<CustomerOperationForDisplayViewModel> customerOperationsForDisplay = [];
    [ObservableProperty] private CustomerOperationForDisplayViewModel? selectedItem;
    [ObservableProperty] private DateTime beginDate = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime endDate = DateTime.Today;
    [ObservableProperty] private decimal? beginBalance;
    [ObservableProperty] private decimal? lastBalance;

    #region Property Changes

    partial void OnSelectedCustomerChanged(CustomerResponse? value)
    => _ = LoadCustomerOperationsForSelectedCustomerAsync();

    partial void OnBeginDateChanged(DateTime value)
        => _ = LoadCustomerOperationsForSelectedCustomerAsync();

    partial void OnEndDateChanged(DateTime value)
        => _ = LoadCustomerOperationsForSelectedCustomerAsync();

    #endregion Property Changes

    #region Load Data

    private async Task LoadCustomerOperationsForSelectedCustomerAsync()
    {
        if (SelectedCustomer is null)
        {
            CustomerOperationsForDisplay.Clear();
            return;
        }

        var response = await customerOperationsApi.GetByCustomerId(
            SelectedCustomer.Id,
            BeginDate,
            EndDate
        );

        CustomerOperationsForDisplay.Clear();

        if (!response.IsSuccess)
            return;

        var displayList = new ObservableCollection<CustomerOperationForDisplayViewModel>();

        foreach (var op in response.Data.Operations)
        {
            decimal debit = 0;
            decimal credit = 0;

            if (op.OperationType == OperationType.Payment)
            {
                if (op.Amount < 0)
                    debit = Math.Abs(op.Amount);
                else
                    credit = op.Amount;
            }
            else if (op.OperationType == OperationType.Sale)
            {
                debit = Math.Abs(op.Amount);
            }
            else if (op.OperationType == OperationType.Discount)
            {
                credit = op.Amount;
            }
            displayList.Add(new CustomerOperationForDisplayViewModel
            {
                Id = op.Id,
                Date = op.Date.LocalDateTime,
                Customer = SelectedCustomer.Name ?? "Noma'lum",
                Debit = debit,
                Credit = credit,
                Description = op.Description,
                OperationType = op.OperationType
            });
        }

        BeginBalance = response.Data.BeginBalance;
        LastBalance = response.Data.EndBalance;
        allOperationsForDisplay = displayList;
        ApplyFilter();
    }

    private async Task LoadInitialDataAsync()
    {
        await LoadCustomersAsync();
    }

    private async Task LoadCustomersAsync()
    {
        var response = await customersApi.GetAllAsync().Handle(isLoading => IsLoading = isLoading);
        if (response.IsSuccess)
            Customers = mapper.Map<ObservableCollection<CustomerResponse>>(response.Data!);
        else Error = response.Message ?? "Mijozlarni yuklashda xatolik yuz berdi.";
    }

    #endregion Load Data

    #region Commands

    [RelayCommand]
    private async Task Delete(CustomerOperationForDisplayViewModel? operation)
    {
        if (operation is null)
        {
            Warning = "O'chiriladigan operatsiya tanlanmagan!";
            return;
        }

        var result = MessageBox.Show(
            $"Ushbu operatsiyani o'chirishni xohlaysizmi?\n\n" +
            $"Sana: {operation.Date:dd.MM.yyyy}\n" +
            $"Debit: {operation.Debit:N2}\n" +
            $"Kredit: {operation.Credit:N2}\n" +
            $"Izoh: {operation.Description}",
            "O'chirishni tasdiqlash",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.No)
            return;

        var response = await customerOperationsApi.Delete(operation.Id)
            .Handle(isLoading => IsLoading = isLoading);

        if (response.IsSuccess)
        {
            CustomerOperationsForDisplay.Remove(operation);
            Success = "Operatsiya muvaffaqiyatli o'chirildi.";
        }
        else Error = response.Message ?? "Operatsiyani o'chirishda xatolik yuz berdi.";
    }

    [RelayCommand]
    private async Task Edit(CustomerOperationForDisplayViewModel? operation)
    {
        if (operation is null)
        {
            Warning = "Tahrirlanadigan operatsiya tanlanmagan!";
            return;
        }

        try
        {
            switch (operation.OperationType)
            {
                case OperationType.Sale:
                    await OpenSaleEditPage(operation.Id);
                    break;

                case OperationType.Payment:
                    await OpenPaymentEditPage(operation.Id);
                    break;

                case OperationType.Discount:
                    Warning = "Chegirmani tahrirlash mumkin emas!";
                    break;

                default:
                    Warning = "Noma'lum operatsiya turi!";
                    break;
            }
        }
        catch (Exception ex)
        {
            Error = $"Tahrirlash sahifasini ochishda xatolik: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SelectedCustomer = null;
        BeginDate = DateTime.Now.AddMonths(-1);
        EndDate = DateTime.Now;
        CustomerOperationsForDisplay = new ObservableCollection<CustomerOperationForDisplayViewModel>(allOperationsForDisplay);
    }

    [RelayCommand]
    private void ExportToExcel()
    {
        try
        {
            if (CustomerOperationsForDisplay is null || !CustomerOperationsForDisplay.Any())
            {
                Info = "Eksport qilish uchun ma'lumot topilmadi.";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel fayllari (*.xlsx)|*.xlsx",
                FileName = "Mijoz Operatsiyalari.xlsx"
            };

            if (dialog.ShowDialog() != true) return;

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Operatsiyalar");

                int row = 1;

                ws.Cell(row, 1).Value = "MIJOZ OPERATSIYALARI HISOBOTI";
                ws.Range($"A{row}:E{row}").Merge().Style
                    .Font.SetBold()
                    .Font.SetFontSize(16)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                row++;

                ws.Cell(row, 1).Value = $"Mijoz: {SelectedCustomer?.Name.ToUpper() ?? "Tanlanmagan"}";
                ws.Range($"A{row}:E{row}").Merge().Style
                    .Font.SetBold()
                    .Font.SetFontSize(14)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                row++;

                ws.Cell(row, 1).Value = $"Davr oralig'i: {BeginDate.ToString("dd.MM.yyyy") ?? "-"} dan {EndDate.ToString("dd.MM.yyyy") ?? "-"} gacha";
                ws.Range($"A{row}:E{row}").Merge().Style
                    .Font.SetBold()
                    .Font.SetFontSize(14)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                row += 2;

                string[] headers = { "Sana", "Mijoz", "Debit", "Kredit", "Izoh" };
                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(row, i + 1).Value = headers[i];

                ws.Range($"A{row}:E{row}").Style.Font.Bold = true;
                row++;

                ws.Range($"A{row}:D{row}").Merge();
                ws.Cell(row, 1).Value = "Boshlang'ich qoldiq";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 5).Value = BeginBalance?.ToString("N2") ?? "0.00";
                ws.Cell(row, 5).Style.Font.Bold = true;
                ws.Cell(row, 5).Style.Alignment.WrapText = true;
                row++;

                foreach (var item in CustomerOperationsForDisplay)
                {
                    ws.Cell(row, 1).Value = item.Date.ToString("dd.MM.yyyy");
                    ws.Cell(row, 2).Value = item.Customer;
                    ws.Cell(row, 3).Value = item.Debit;
                    ws.Cell(row, 4).Value = item.Credit;

                    var formattedDescription = string.Join(Environment.NewLine,
                        (item.Description ?? "").Split(';').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)));

                    ws.Cell(row, 5).Value = formattedDescription;
                    ws.Cell(row, 5).Style.Alignment.WrapText = true;

                    row++;
                }

                ws.Range($"A{row}:D{row}").Merge();
                ws.Cell(row, 1).Value = "Oxirgi qoldiq";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 5).Value = LastBalance?.ToString("N2") ?? "0.00";
                ws.Cell(row, 5).Style.Font.Bold = true;
                ws.Cell(row, 5).Style.Alignment.WrapText = true;

                ws.Columns().AdjustToContents();

                workbook.SaveAs(dialog.FileName);
            }

            Success = "Excel fayl muvaffaqiyatli saqlandi.";
        }
        catch (Exception ex)
        {
            Error = $"Xatolik: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Print()
    {
        if (CustomerOperationsForDisplay is null || !CustomerOperationsForDisplay.Any())
        {
            Info = "Chop etish uchun ma'lumot topilmadi.";
            return;
        }

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
            dlg.PrintDocument(CreateFixedDocument().DocumentPaginator, "Operatsiyalar");
    }

    [RelayCommand]
    private void Preview()
    {
        if (CustomerOperationsForDisplay is null || !CustomerOperationsForDisplay.Any())
        {
            Info = "Oldindan ko'rish uchun ma'lumot topilmadi.";
            return;
        }

        var doc = CreateFixedDocument();
        var viewer = new DocumentViewer { Document = doc };

        var shareButton = new Button
        {
            Content = "📤 Telegram'da ulashish",
            Margin = new Thickness(5),
            Padding = new Thickness(10, 5, 10, 5),
            Background = Brushes.LightBlue
        };

        shareButton.Click += (s, e) =>
        {
            try
            {
                if (SelectedCustomer is null) { /* ... xato */ return; }

                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string voltFolder = Path.Combine(documentsPath, "VoltStream");

                if (!Directory.Exists(voltFolder)) Directory.CreateDirectory(voltFolder);

                string safeName = string.Join("_", SelectedCustomer.Name.Split(Path.GetInvalidFileNameChars()));
                string begin = BeginDate.ToString("dd.MM.yyyy") ?? "-";
                string end = EndDate.ToString("dd.MM.yyyy") ?? "-";
                string fileName = $"{safeName}_{begin}-{end}.pdf";
                string pdfPath = Path.Combine(voltFolder, fileName);

                ExportToPdf(doc, pdfPath);

                if (File.Exists(pdfPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{pdfPath}\"");
                    Success = $"Hisobot \"{fileName}\" nomli fayl sifatida \"Documents\\VoltStream\" papkasida saqlandi.";
                }
            }
            catch (Exception ex)
            {
                Error = $"Ulashish/Saqlashda xatolik yuz berdi: {ex.Message}";
            }
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        toolbar.Children.Add(shareButton);

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(viewer);

        new Window
        {
            Title = "Hisobot ko'rinishi",
            Width = 950,
            Height = 850,
            Content = layout,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        }.ShowDialog();
    }

    private void ExportToPdf(FixedDocument doc, string path)
    {
        PdfDocument pdf = new PdfDocument();

        // Sifatni oshirish uchun 300 DPI yaxshi, 
        // lekin FixedDocument o'lchamlari 96 DPI (DIPs) da hisoblanadi.
        const double dpi = 300;
        const double scale = dpi / 96.0;

        for (int i = 0; i < doc.Pages.Count; i++)
        {
            // Sahifani olish
            PageContent pageContent = doc.Pages[i];
            FixedPage fixedPage = pageContent.Child;

            // Sahifa o'lchamlarini olish (odatda 793.7 x 1122.5)
            double width = fixedPage.Width;
            double height = fixedPage.Height;

            // Sahifa hali yuklanmagan bo'lsa, uni majburan yangilaymiz
            fixedPage.UpdateLayout();

            // Render o'lchamlarini hisoblash
            int pixelWidth = (int)(width * scale);
            int pixelHeight = (int)(height * scale);

            RenderTargetBitmap rtb = new(
                pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);

            // MUHIM: Sahifani vizual Tree ga qaytadan bog'lamaslik va buzmaslik uchun
            // faqat vizual render qilish kifoya
            rtb.Render(fixedPage);

            // Koder orqali MemoryStream ga saqlash
            PngBitmapEncoder pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(rtb));

            using (MemoryStream stream = new MemoryStream())
            {
                pngEncoder.Save(stream);
                stream.Seek(0, SeekOrigin.Begin);

                // PDF sahifasini yaratish
                PdfPage pdfPage = pdf.AddPage();

                // PDF sahifasi o'lchamini FixedPage bilan bir xil qilish (Point birligida)
                pdfPage.Width = XUnit.FromPoint(width);
                pdfPage.Height = XUnit.FromPoint(height);

                XGraphics gfx = XGraphics.FromPdfPage(pdfPage);
                XImage image = XImage.FromStream(stream);

                // Rasmni PDF ga chizish
                gfx.DrawImage(image, 0, 0, pdfPage.Width, pdfPage.Height);
            }
        }

        pdf.Save(path);
    }
    #endregion Commands

    #region Private Helpers

    private void ApplyFilter()
    {
        if (allOperationsForDisplay is null || allOperationsForDisplay.Count == 0)
            return;

        var filtered = allOperationsForDisplay.AsEnumerable();

        CustomerOperationsForDisplay = new ObservableCollection<CustomerOperationForDisplayViewModel>(filtered);
    }

    private async Task OpenSaleEditPage(long operationId)
    {
        var saleResponse = await customerOperationsApi.GetById(operationId)
            .Handle(isLoading => IsLoading = isLoading);

        if (!saleResponse.IsSuccess)
        {
            Error = saleResponse.Message ?? "Savdo ma'lumotlari topilmadi!";
            return;
        }

        navigationService.Navigate(new SaleEditPage(services, saleResponse.Data.Sale!));
    }

    private async Task OpenPaymentEditPage(long operationId)
    {
        var response = await customerOperationsApi.GetById(operationId)
            .Handle(isLoading => IsLoading = isLoading);

        if (!response.IsSuccess)
        {
            Error = response.Message ?? "Savdo ma'lumotlari topilmadi!";
            return;
        }

        navigationService.Navigate(new PaymentEditPage(services, response.Data.Payment!));
    }

    #endregion Private Helpers

    #region PDF Export and Share

    private FixedDocument CreateFixedDocument()
    {
        var doc = new FixedDocument();
        const double pageWidth = 793.7;
        const double pageHeight = 1122.5;
        const double margin = 25;
        const double approxSingleRowHeight = 25;

        var operations = CustomerOperationsForDisplay?.ToList() ?? [];
        double currentY = 0;
        int pageNumber = 1;
        int currentIndex = 0;
        List<FixedPage> tempPages = [];

        while (currentIndex < operations.Count)
        {
            bool isFirstPage = (pageNumber == 1);
            var page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };
            var container = new StackPanel { Margin = new Thickness(margin, 30, margin, margin) };

            if (isFirstPage)
            {
                currentY = AddHeaderContent(container, pageNumber, true);
                var beginBalanceBlock = CreateBalanceInfoBlock("Boshlang'ich qoldiq", BeginBalance?.ToString("N2") ?? "0.00", Brushes.AliceBlue);
                beginBalanceBlock.Margin = new Thickness(0);
                container.Children.Add(beginBalanceBlock);
                currentY += 30;
            }
            else
            {
                currentY = AddHeaderContent(container, pageNumber, false);
            }

            // 2. JADVAL - Ustunlar o'zgardi (70, 550, 140)
            var table = new Grid();
            double[] widths = [70, 555, 120]; // Kredit ustuni olib tashlanib, izohga qo'shildi
            foreach (var w in widths)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            AddRowHeader(table, "Sana", "Izoh", "Debit/Kredit", approxSingleRowHeight);

            // Jami qismi uchun joy (Endi 2 ta qator bo'lgani uchun joyni ko'paytiramiz)
            double footerSpace = approxSingleRowHeight * 4 + 50;
            var opsOnPage = new List<CustomerOperationForDisplayViewModel>();

            int tempIndex = currentIndex;
            while (tempIndex < operations.Count)
            {
                var op = operations[tempIndex];
                double requiredHeight = CalculateOperationRowHeight(op, widths[1]);
                double availableSpace = pageHeight - (margin * 2) - currentY - footerSpace;

                if (requiredHeight > availableSpace && tempIndex > currentIndex) break;

                opsOnPage.Add(op);
                tempIndex++;
                currentY += requiredHeight;
            }

            foreach (var op in opsOnPage)
            {
                AddOperationRow(table, op, approxSingleRowHeight);
            }

            currentIndex += opsOnPage.Count;
            bool isLastPage = (currentIndex >= operations.Count);

            if (isLastPage)
            {
                decimal totalDebit = operations.Sum(x => x.Debit);
                decimal totalCredit = operations.Sum(x => x.Credit);

                // JAMI QISMI - Ikkita qator qilib chiqarish
                AddRowTotalNew(table, totalCredit, totalDebit, approxSingleRowHeight);

                container.Children.Add(table);

                var lastBalanceBlock = CreateBalanceInfoBlock("Oxirgi qoldiq", LastBalance?.ToString("N2") ?? "0.00", Brushes.GhostWhite);
                lastBalanceBlock.Margin = new Thickness(0);
                container.Children.Add(lastBalanceBlock);
            }
            else
            {
                container.Children.Add(table);
            }

            page.Children.Add(container);
            tempPages.Add(page);
            pageNumber++;
            currentY = 0;
        }

        // Sahifalarni yig'ish (avvalgi kod bilan bir xil)
        int totalPages = tempPages.Count;
        int finalPageNumber = 1;
        foreach (var finalPage in tempPages)
        {
            AddFooterContent(finalPage, finalPageNumber, totalPages);
            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(finalPage);
            doc.Pages.Add(pageContent);
            finalPageNumber++;
        }
        return doc;
    }

    // 1. Yangi qator qo'shish metodi (Debit/Kredit ranglari bilan)
    private void AddOperationRow(Grid grid, CustomerOperationForDisplayViewModel op, double approxSingleRowHeight)
    {
        int row = grid.RowDefinitions.Count;
        double requiredHeight = CalculateOperationRowHeight(op, 555);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(requiredHeight) });

        Brush amountBrush = op.Debit > 0 ? Brushes.DarkRed : Brushes.Black;
        string amountText = op.Debit > 0 ? op.Debit.ToString("N2") : op.Credit.ToString("N2");

        AddSimpleCell(grid, row, 0, op.Date.ToString("dd.MM.yyyy"), TextAlignment.Center, FontWeights.Normal, 12, new Thickness(0.5, 0.5, 0, 0.5), Brushes.Black);

        var descriptionTb = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(3,5,3,0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas")
        };

        string fullDesc = op.Description ?? op.FormattedDescription ?? "";
        string[] lines = fullDesc.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        int maxNameLen = 0;
        int maxCalcLen = 0;
        int maxSumLen = 0;

        // 1. USTUNLARNI TEKISLASH UCHUN MAKSIMAL UZUNLIKLARNI HISOBLASH
        foreach (var line in lines)
        {
            int lastDash = line.LastIndexOf('-');
            int firstEqual = line.IndexOf('=');
            int firstBracket = line.IndexOf('[');

            if (lastDash != -1 && firstEqual != -1 && firstEqual > lastDash)
            {
                string namePart = line[..lastDash].Trim();
                if (namePart.Length > maxNameLen) maxNameLen = namePart.Length;

                string calcPart = line[(lastDash + 1)..firstEqual].Trim();
                if (calcPart.Length > maxCalcLen) maxCalcLen = calcPart.Length;

                string sumPart;
                if (firstBracket != -1 && firstBracket > firstEqual)
                    sumPart = line[(firstEqual + 1)..firstBracket].Trim();
                else
                    sumPart = line[(firstEqual + 1)..].Trim();

                if (sumPart.Length > maxSumLen) maxSumLen = sumPart.Length;
            }
        }

        bool insideSavdo = false;

        foreach (var line in lines)
        {
            string trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("Savdo:", StringComparison.OrdinalIgnoreCase))
            {
                insideSavdo = true;
                descriptionTb.Inlines.Add(new Run(line) { FontWeight = FontWeights.Bold });
            }
            else if (insideSavdo && line.Contains('-') && line.Contains('='))
            {
                int lastDash = line.LastIndexOf('-');
                int firstEqual = line.IndexOf('=');
                int firstBracket = line.IndexOf('[');

                if (lastDash != -1 && firstEqual != -1 && firstEqual > lastDash)
                {
                    // A. Mahsulot nomi (Bold)
                    string namePart = line[..lastDash].Trim();
                    descriptionTb.Inlines.Add(new Run(namePart.PadRight(maxNameLen + 1)) { FontWeight = FontWeights.Bold });

                    // B. Hisob-kitob
                    descriptionTb.Inlines.Add(new Run("- "));
                    string calcPart = line[(lastDash + 1)..firstEqual].Trim();
                    descriptionTb.Inlines.Add(new Run(calcPart.PadRight(maxCalcLen + 1)));

                    // C. Summa (Bold)
                    descriptionTb.Inlines.Add(new Run("= "));
                    string sumPart;
                    if (firstBracket != -1 && firstBracket > firstEqual)
                        sumPart = line[(firstEqual + 1)..firstBracket].Trim();
                    else
                        sumPart = line[(firstEqual + 1)..].Trim();

                    // PadRight bu yerda summadan keyin kerakli bo'shliqni o'zi qo'shadi
                    descriptionTb.Inlines.Add(new Run(sumPart.PadRight(maxSumLen + 1)) { FontWeight = FontWeights.Bold });

                    // D. Chegirma qismi (Normal)
                    if (firstBracket != -1 && firstBracket > firstEqual)
                    {
                        descriptionTb.Inlines.Add(new Run(line[firstBracket..].Trim()));
                    }
                }
                else { descriptionTb.Inlines.Add(new Run(line)); }
            }
            else if (line.Contains(':'))
            {
                int colonIndex = line.IndexOf(':');
                descriptionTb.Inlines.Add(new Run(line[..(colonIndex + 1)]) { FontWeight = FontWeights.Bold });
                descriptionTb.Inlines.Add(new Run(line[(colonIndex + 1)..]));

                if (trimmedLine.StartsWith("Jami:", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("Chegirma:", StringComparison.OrdinalIgnoreCase))
                    insideSavdo = false;
            }
            else { descriptionTb.Inlines.Add(new Run(line)); }

            descriptionTb.Inlines.Add(new LineBreak());
        }

        var borderDesc = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5, 0.5, 0, 0.5), Child = descriptionTb };
        Grid.SetRow(borderDesc, row); Grid.SetColumn(borderDesc, 1); grid.Children.Add(borderDesc);

        AddSimpleCell(grid, row, 2, amountText, TextAlignment.Right, FontWeights.Bold, 12, new Thickness(0.5, 0.5, 0.5, 0.5), amountBrush);
    }

    private void AddRowHeader(Grid grid, string date, string description, string debitKreditLabel, double height)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });

        AddSimpleCell(grid, row, 0, date, TextAlignment.Center, FontWeights.Bold, 12, new Thickness(0.5, 0.5, 0, 0.5), Brushes.Black);
        AddSimpleCell(grid, row, 1, description, TextAlignment.Center, FontWeights.Bold, 12, new Thickness(0.5, 0.5, 0, 0.5), Brushes.Black);

        var tb = new TextBlock
        {
            FontSize = 12, // 14 dan 12 ga tushirildi
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        tb.Inlines.Add(new Run("Debit") { Foreground = Brushes.DarkRed });
        tb.Inlines.Add(new Run(" / ") { Foreground = Brushes.Black });
        tb.Inlines.Add(new Run("Kredit") { Foreground = Brushes.Black });

        var border = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5),
            Child = tb
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, 2);
        grid.Children.Add(border);
    }

    private void AddRowTotalNew(Grid grid, decimal totalCredit, decimal totalDebit, double height)
    {
        int row1 = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        int row2 = row1 + 1;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });

        // JAMI birlashgan katak - FontSize 14 (Oldingi kelishuv bo'yicha)
        var jamiBorder = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5, 0.5, 0, 0.5),
            Child = new TextBlock
            {
                Text = "JAMI",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5)
            }
        };
        Grid.SetRow(jamiBorder, row1);
        Grid.SetRowSpan(jamiBorder, 2);
        Grid.SetColumn(jamiBorder, 0);
        grid.Children.Add(jamiBorder);

        // Debit qatori - FontSize 14 va DarkRed
        AddSimpleCell(grid, row1, 1, "Debit", TextAlignment.Left, FontWeights.Bold, 14, new Thickness(0.5, 0.5, 0, 0.5), Brushes.DarkRed);
        AddSimpleCell(grid, row1, 2, totalDebit.ToString("N2"), TextAlignment.Right, FontWeights.Bold, 14, new Thickness(0.5, 0.5, 0.5, 0.5), Brushes.DarkRed);

        // Kredit qatori - FontSize 14 va Black
        AddSimpleCell(grid, row2, 1, "Kredit", TextAlignment.Left, FontWeights.Bold, 14, new Thickness(0.5, 0, 0, 0.5), Brushes.Black);
        AddSimpleCell(grid, row2, 2, totalCredit.ToString("N2"), TextAlignment.Right, FontWeights.Bold, 14, new Thickness(0.5, 0, 0.5, 0.5), Brushes.Black);
    }
    // 4. AddSimpleCell metodiga rang (Brush) qo'shish
    private void AddSimpleCell(Grid grid, int row, int column, string value, TextAlignment align, FontWeight weight, double size, Thickness borderThickness, Brush foreground)
    {
        var tb = new TextBlock
        {
            Text = value,
            Padding = new Thickness(5, 2, 5, 2),
            FontSize = size,
            FontWeight = weight,
            TextAlignment = align,
            Foreground = foreground, // Rang shu yerda ishlatiladi
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var border = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = borderThickness,
            Child = tb
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private Border CreateBalanceInfoBlock(string label, string value, Brush background)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lblText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var valText = new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Grid.SetColumn(lblText, 0);
        Grid.SetColumn(valText, 1);
        grid.Children.Add(lblText);
        grid.Children.Add(valText);

        return new Border
        {
            Background = background,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5), // Jadval chiziqlari bilan bir xil bo'lishi uchun
            Child = grid
        };
    }

    private void AddFooterContent(FixedPage page, int currentPage, int totalPages)
    {
        const double margin = 40;
        //const double pageWidth = 793.7;

        // Sahifa raqami matnini yaratish
        var pageInfo = new TextBlock
        {
            Text = $"{currentPage}-bet / {totalPages}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right // Kerak emas, chunki FixedPage.SetLeft/Top ishlatiladi
        };

        // Sahifa raqamini joylashtirish
        FixedPage.SetRight(pageInfo, margin); // O'ng chetidan margin masofada
        FixedPage.SetBottom(pageInfo, 20);    // Pastki chetidan 20 piksel yuqorida

        page.Children.Add(pageInfo);
    }

    private double AddHeaderContent(StackPanel container, int pageNumber, bool isFullHeader)
    {
        if (isFullHeader)
        {
            container.Children.Add(new TextBlock
            {
                Text = "MIJOZ OPERATSIYALARI HISOBOTI",
                FontSize = 20,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            });

            container.Children.Add(new TextBlock
            {
                Text = $"Mijoz: {SelectedCustomer?.Name.ToUpper()}",
                FontSize = 16,
                FontWeight = FontWeights.Medium
            });

            container.Children.Add(new TextBlock
            {
                Text = $"Davr: {BeginDate:dd.MM.yyyy} — {EndDate:dd.MM.yyyy}",
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 10)
            });
            return 120; // Taxminiy band qilingan balandlik
        }
        else
        {
            container.Children.Add(new TextBlock
            {
                Text = $"",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 10)
            });
            return 30; // Kamroq joy oladi
        }
    }

    private double CalculateOperationRowHeight(CustomerOperationForDisplayViewModel op, double commentColumnWidth)
    {
        string description = op.Description ?? op.FormattedDescription ?? "";

        var tempTextBlock = new TextBlock
        {
            Text = description,
            Width = commentColumnWidth - 10,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12 // Qator balandligini 12 shriftga qarab hisoblaydi
        };

        tempTextBlock.Measure(new Size(commentColumnWidth - 10, double.MaxValue));
        double actualHeight = tempTextBlock.DesiredSize.Height + 8;

        return Math.Max(25, actualHeight);
    }

    public class PaginatedOperation
    {
        // Asosiy operatsiya ma'lumotlari
        public DateTime Date { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string Description { get; set; } = string.Empty;

        // Sahifalash uchun qo'shimcha ma'lumot
        public int StartLineIndex { get; set; } // Bu qatorda Izoh qayerdan boshlanadi
        public int EndLineIndex { get; set; } // Bu qator Izohning qayerda tugaydi
        public int TotalLines { get; set; } // Izohning jami satrlar soni
        public bool IsFirstSegment { get; set; } // Bu segmentda Sana/Debit/Kredit bo'ladimi
        public bool IsLastSegment { get; set; } // Bu oxirgi segmentmi
    }

    #endregion PDF Export and Share
}