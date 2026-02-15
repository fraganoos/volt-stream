namespace VoltStream.WPF.Commons.ViewModels;
using ApiServices.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using VoltStream.WPF.Sales.ViewModels;
public partial class CustomerOperationForDisplayViewModel : ObservableObject
{
    public long Id { get; set; }
    public long? CustomerId { get; set; }
    public long AccountId { get; set; }
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private DateTime date;
    [ObservableProperty] private string customer = string.Empty;
    [ObservableProperty] private decimal debit;
    [ObservableProperty] private decimal credit;
    [ObservableProperty] private string? description;
    [ObservableProperty] private TextBlock? formattedTextBlock;
    [ObservableProperty] private OperationType operationType;
    [ObservableProperty] private AccountViewModel account = new();
    public bool CanEdit => OperationType == OperationType.Sale;
    public bool IsEditable { get; set; }
    [ObservableProperty] private Sale? sale;
    [ObservableProperty] private PaymentViewModel? payment;
    public string FormattedDescription
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description))
                return string.Empty;
            return string.Join("\n",
                Description.Split(';')
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x)));
        }
    }
    partial void OnDescriptionChanged(string? oldValue, string? newValue)
    {
        FormattedTextBlock = CreateFormattedTextBlock();
    }
    private TextBlock CreateFormattedTextBlock()
    {
        if (string.IsNullOrWhiteSpace(Description))
            return new TextBlock();

        var textBlock = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap, // Tekislik buzilmasligi uchun
            Padding = new Thickness(5, 3, 5, 0),
            TextAlignment = TextAlignment.Left
        };

        // Qatorlarga bo'lish (PDF mantiqi kabi)
        string fullDesc = Description ?? "";
        string[] lines = fullDesc.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => x.Trim())
                                 .ToArray();

        if (lines.Length == 0) return textBlock;

        int maxNameLen = 0;
        int maxCalcLen = 0;
        int maxSumLen = 0;

        // 1. MAKSIMAL UZUNLIKLARNI HISOBLASH (PDF-dagi mantiq)
        foreach (var rawLine in lines)
        {
            string line = rawLine;
            if (line.StartsWith("Savdo:", StringComparison.OrdinalIgnoreCase))
                line = line.Substring(6).Trim();

            int lastDash = line.LastIndexOf('-');
            int firstEqual = line.IndexOf('=');
            int firstBracket = line.IndexOf('[');

            if (lastDash != -1 && firstEqual != -1 && firstEqual > lastDash)
            {
                string namePart = line.Substring(0, lastDash).Trim();
                if (namePart.Length > maxNameLen) maxNameLen = namePart.Length;

                string calcPart = line.Substring(lastDash + 1, firstEqual - (lastDash + 1)).Trim();
                if (calcPart.Length > maxCalcLen) maxCalcLen = calcPart.Length;

                string sumPart = (firstBracket != -1 && firstBracket > firstEqual)
                    ? line.Substring(firstEqual + 1, firstBracket - (firstEqual + 1)).Trim()
                    : line.Substring(firstEqual + 1).Trim();

                if (sumPart.Length > maxSumLen) maxSumLen = sumPart.Length;
            }
        }

        // 2. FORMATLASH VA INLINE-LARNI QO'SHISH
        bool insideSavdo = false;

        foreach (var line in lines)
        {
            string trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("Savdo:", StringComparison.OrdinalIgnoreCase))
            {
                insideSavdo = true;
                textBlock.Inlines.Add(new Run("Savdo:") { FontWeight = FontWeights.Bold });

                string remainder = trimmedLine.Substring(6).Trim();
                if (!string.IsNullOrEmpty(remainder))
                {
                    // Agar Savdo: dan keyin mahsulot bo'lsa (PDF-dagi mantiqqa o'xshash)
                    if (remainder.Contains("-") && remainder.Contains("="))
                    {
                        textBlock.Inlines.Add(new LineBreak());
                        FormatProductLine(textBlock, remainder, maxNameLen, maxCalcLen, maxSumLen);
                    }
                    else
                    {
                        // Shunchaki qo'shimcha matn bo'lsa
                        textBlock.Inlines.Add(new Run(" " + remainder));
                        textBlock.Inlines.Add(new LineBreak());
                    }
                }
                else
                {
                    textBlock.Inlines.Add(new LineBreak());
                }
            }
            else if (insideSavdo && line.Contains("-") && line.Contains("="))
            {
                FormatProductLine(textBlock, line, maxNameLen, maxCalcLen, maxSumLen);
            }
            else
            {
                // Sarlavhalar (Jami:, Chegirma:, Naqd: va h.k.)
                if (line.Contains(":"))
                {
                    int colonIndex = line.IndexOf(':');
                    textBlock.Inlines.Add(new Run(line.Substring(0, colonIndex + 1)) { FontWeight = FontWeights.Bold });
                    textBlock.Inlines.Add(new Run(line.Substring(colonIndex + 1)));

                    if (trimmedLine.StartsWith("Jami:", StringComparison.OrdinalIgnoreCase) ||
                        trimmedLine.StartsWith("Chegirma:", StringComparison.OrdinalIgnoreCase))
                    {
                        insideSavdo = false;
                    }
                }
                else
                {
                    textBlock.Inlines.Add(new Run(line));
                }
                textBlock.Inlines.Add(new LineBreak());
            }
        }

        return textBlock;
    }

    private void FormatProductLine(TextBlock tb, string line, int maxName, int maxCalc, int maxSum)
    {
        int lastDash = line.LastIndexOf('-');
        int firstEqual = line.IndexOf('=');
        int firstBracket = line.IndexOf('[');

        if (lastDash != -1 && firstEqual != -1 && firstEqual > lastDash)
        {
            // A. Mahsulot nomi (Bold + PadRight)
            string namePart = line.Substring(0, lastDash).Trim();
            tb.Inlines.Add(new Run(namePart.PadRight(maxName)) { FontWeight = FontWeights.Bold });

            // B. Hisob-kitob qismi
            tb.Inlines.Add(new Run(" - "));
            string calcPart = line.Substring(lastDash + 1, firstEqual - (lastDash + 1)).Trim();
            tb.Inlines.Add(new Run(calcPart.PadRight(maxCalc)));

            // C. Summa (Bold + PadRight)
            tb.Inlines.Add(new Run(" = "));
            string sumPart = (firstBracket != -1 && firstBracket > firstEqual)
                ? line.Substring(firstEqual + 1, firstBracket - (firstEqual + 1)).Trim()
                : line.Substring(firstEqual + 1).Trim();

            tb.Inlines.Add(new Run(sumPart.PadRight(maxSum)) { FontWeight = FontWeights.Bold });

            // D. Chegirma qismi
            if (firstBracket != -1 && firstBracket > firstEqual)
            {
                tb.Inlines.Add(new Run(" " + line.Substring(firstBracket).Trim()));
            }
        }
        else
        {
            tb.Inlines.Add(new Run(line));
        }
        tb.Inlines.Add(new LineBreak());
    }
    partial void OnOperationTypeChanged(OperationType oldValue, OperationType newValue)
    {
        IsEditable = newValue == OperationType.Sale;
    }
}