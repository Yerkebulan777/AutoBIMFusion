#if !CORECONSOLE_DIAGNOSTICS
using AutoBIMFusion.QuickPdf;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;

namespace AutoBIMFusion.Plugin.Commands;

/// <summary>
///     Одно окно выбора QuickPDF: масштаб, цветность и начальный номер листа.
///     Собирается кодом без XAML, чтобы не трогать desktop/headless-конфигурации сборки.
///     Последний принятый выбор запоминается и подставляется при следующем открытии.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class QuickPdfOptionsDialog
{
    private static readonly string[] Colors = ["Цветной", "Черно-белый"];

    private static QuickPdfOptions _last = QuickPdfOptions.Default;

    internal static bool TryShow(out QuickPdfOptions options)
    {
        options = _last;

        ComboBox scaleBox = new()
        {
            Margin = new Thickness(0, 4, 0, 12),
            SelectedIndex = ScaleIndex(_last.ScaleDenominator)
        };
        foreach (int scale in QuickPdfOptions.SupportedScales)
        {
            _ = scaleBox.Items.Add("1:" + scale.ToString(CultureInfo.InvariantCulture));
        }

        ComboBox colorBox = new()
        {
            Margin = new Thickness(0, 4, 0, 12),
            SelectedIndex = _last.Monochrome ? 1 : 0
        };
        foreach (string color in Colors)
        {
            _ = colorBox.Items.Add(color);
        }

        TextBox startBox = new()
        {
            Margin = new Thickness(0, 4, 0, 16),
            MaxLength = 3,
            Width = 60,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = _last.StartSheetLabel
        };

        Button okButton = new()
        {
            Content = "Печать",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        Button cancelButton = new()
        {
            Content = "Отмена",
            Width = 90,
            IsCancel = true
        };
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        StackPanel panel = new()
        {
            Margin = new Thickness(16)
        };
        panel.Children.Add(new TextBlock { Text = "Масштаб:" });
        panel.Children.Add(scaleBox);
        panel.Children.Add(new TextBlock { Text = "Цвет:" });
        panel.Children.Add(colorBox);
        panel.Children.Add(new TextBlock { Text = "Начинать нумерацию с:" });
        panel.Children.Add(startBox);
        panel.Children.Add(buttons);

        Window window = new()
        {
            Title = "QuickPDF — параметры печати",
            Content = panel,
            Width = 300,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize
        };
        QuickPdfOptions accepted = _last;
        okButton.Click += (_, _) =>
        {
            if (!TryAccept(scaleBox.SelectedIndex, colorBox.SelectedIndex, startBox.Text, out accepted))
            {
                _ = MessageBox.Show(
                    window,
                    "Введите начальный номер от 000 до 999.",
                    "QuickPDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            window.DialogResult = true;
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        options = _last = accepted;
        return true;
    }

    private static bool TryAccept(int scaleIndex, int colorIndex, string? startText, out QuickPdfOptions options)
    {
        options = _last;
        int scale = scaleIndex >= 0 && scaleIndex < QuickPdfOptions.SupportedScales.Length
            ? QuickPdfOptions.SupportedScales[scaleIndex]
            : QuickPdfOptions.DefaultScaleDenominator;
        if (!TryParseStart(startText, out int start))
        {
            return false;
        }

        options = new QuickPdfOptions(scale, colorIndex == 1, start);
        return true;
    }

    private static int ScaleIndex(int denominator)
    {
        int index = Array.IndexOf(QuickPdfOptions.SupportedScales, denominator);
        return index >= 0
            ? index
            : Array.IndexOf(QuickPdfOptions.SupportedScales, QuickPdfOptions.DefaultScaleDenominator);
    }

    private static bool TryParseStart(string? text, out int start)
    {
        start = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
            parsed is < QuickPdfOptions.MinStartSheet or > QuickPdfOptions.MaxStartSheet)
        {
            return false;
        }

        start = parsed;
        return true;
    }
}
#endif
