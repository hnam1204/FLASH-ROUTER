using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.ImportExport.Contracts;

public sealed record ImportPreviewRowVm(
    int RowIndex,
    string Email,
    string DisplayName,
    string Provider,
    string Plan,
    string Tags,
    bool IsValid,
    string? ValidationError);

public sealed record ImportPreviewResultVm(
    string FileName,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    IReadOnlyList<ImportPreviewRowVm> PreviewRows);

public sealed class ExportOptionsVm
{
    public bool IncludeTags { get; set; }
    public bool IncludeNotes { get; set; } = true;
    public bool IncludePlan { get; set; } = true;
    public bool IncludeDates { get; set; } = true;
    public string Format { get; set; } = "csv";
}

public interface IImportExportUiPort
{
    Task<ImportPreviewResultVm> ParseAndPreviewCsvAsync(
        string fileName,
        string content,
        CancellationToken cancellationToken = default);

    Task<OperationResultVm> CommitImportAsync(
        ImportPreviewResultVm preview,
        string conflictStrategy,
        CancellationToken cancellationToken = default);

    Task<string> GenerateExportContentAsync(
        ExportOptionsVm options,
        CancellationToken cancellationToken = default);
}
