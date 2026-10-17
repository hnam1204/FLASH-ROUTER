using FlashAI.Foundation.Contracts;
using FlashAI.Modules.ImportExport.Contracts;

namespace FlashAI.Modules.ImportExport.Application;

public sealed class PreviewImportExportUiPort : IImportExportUiPort
{
    public Task<ImportPreviewResultVm> ParseAndPreviewCsvAsync(string fileName, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CsvMetadataParser.Parse(fileName, content));
    }

    public Task<OperationResultVm> CommitImportAsync(ImportPreviewResultVm preview, string conflictStrategy, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResultVm.Failure(
            "BACKEND_PENDING",
            "Chức năng ghi dữ liệu nhập vào cơ sở dữ liệu thật đang chờ Backend triển khai ở Phase 1. Xem trước dữ liệu thành công."));
    }

    public Task<string> GenerateExportContentAsync(ExportOptionsVm options, CancellationToken cancellationToken = default)
    {
        var headers = new List<string> { "Email", "DisplayName", "Provider" };
        if (options.IncludePlan) headers.Add("RecordedPlan");
        if (options.IncludeNotes) headers.Add("Notes");
        if (options.IncludeDates) headers.Add("TrackingDate");
        return Task.FromResult(string.Join(',', headers) + Environment.NewLine);
    }
}
