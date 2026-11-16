using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DAP.Presentation.AvaloniaApp.ViewModels.Pages;

namespace DAP.Presentation.AvaloniaApp.Views.Pages;

/// <summary>
/// 班组能耗报表视图。
///
/// 导出走视图层：文件选择器依赖窗口的 StorageProvider，视图模型不持有 TopLevel，
/// 把交互留在视图、把内容生成留在视图模型，便于单独验证 CSV 内容。
/// </summary>
public partial class EnergyReportPageView : UserControl
{
    /// <summary>
    /// 初始化视图。
    /// </summary>
    public EnergyReportPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 导出 CSV。
    /// </summary>
    private async void OnExportClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not EnergyReportPageViewModel viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出班组能耗报表",
            SuggestedFileName = $"班组能耗报表_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV 文件") { Patterns = ["*.csv"] }
            ]
        });

        if (file is null)
        {
            return;
        }

        await using Stream stream = await file.OpenWriteAsync();

        // UTF-8 BOM：Excel 默认按本地编码解析无 BOM 的 CSV，中文列名会变成乱码。
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteAsync(viewModel.BuildCsv());
    }
}
