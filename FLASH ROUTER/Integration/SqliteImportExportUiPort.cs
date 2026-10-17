using System.Text;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.ImportExport.Application;
using FlashAI.Modules.ImportExport.Contracts;

namespace FLASH_ROUTER.Integration;

public sealed class SqliteImportExportUiPort : IImportExportUiPort
{
    private readonly IAccountWorkspaceReader _accounts;
    private readonly IAccountCommandPort _commands;

    public SqliteImportExportUiPort(IAccountWorkspaceReader accounts, IAccountCommandPort commands)
    {
        _accounts = accounts;
        _commands = commands;
    }

    public Task<ImportPreviewResultVm> ParseAndPreviewCsvAsync(string fileName, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CsvMetadataParser.Parse(fileName, content));
    }

    public async Task<OperationResultVm> CommitImportAsync(ImportPreviewResultVm preview, string conflictStrategy, CancellationToken cancellationToken = default)
    {
        var imported = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var row in preview.PreviewRows.Where(row => row.IsValid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = (await _accounts.GetAccountsAsync(row.Email, null, null, cancellationToken))
                .FirstOrDefault(account => account.Provider.Equals(row.Provider, StringComparison.OrdinalIgnoreCase));
            var existingDetail = existing is null ? null : await _accounts.GetAccountDetailAsync(existing.AccountId, cancellationToken);

            if (existing is not null && conflictStrategy.Equals("skip", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            List<string> tags = string.IsNullOrWhiteSpace(row.Tags)
                ? []
                : row.Tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var model = new AccountEditorVm
            {
                AccountId = existing?.AccountId,
                Email = row.Email,
                DisplayName = row.DisplayName,
                Provider = row.Provider,
                RecordedPlan = row.Plan,
                Notes = null,
                Tags = tags,
                Status = "Unverified",
                Version = existingDetail?.Version
            };

            var result = existing is null
                ? await _commands.CreateAccountAsync(model, cancellationToken)
                : await _commands.UpdateAccountAsync(existing.AccountId, model, cancellationToken);
            if (result.IsSuccess) imported++; else failed++;
        }

        return failed > 0
            ? OperationResultVm.Failure("IMPORT_PARTIAL_FAILURE", $"Đã nhập {imported} dòng, bỏ qua {skipped} dòng và gặp lỗi ở {failed} dòng.")
            : OperationResultVm.Success($"Đã nhập {imported} dòng vào SQLite; bỏ qua {skipped} dòng trùng.");
    }

    public async Task<string> GenerateExportContentAsync(ExportOptionsVm options, CancellationToken cancellationToken = default)
    {
        var accounts = await _accounts.GetAccountsAsync(null, null, "newest", cancellationToken);
        var builder = new StringBuilder();
        var headers = new List<string> { "Email", "DisplayName", "Provider" };
        if (options.IncludePlan) headers.Add("RecordedPlan");
        if (options.IncludeNotes) headers.Add("Notes");
        if (options.IncludeDates) headers.Add("TrackingDate");
        builder.AppendLine(string.Join(',', headers));

        foreach (var account in accounts)
        {
            var values = new List<string> { SafeCsv(account.Email), SafeCsv(account.DisplayName), SafeCsv(account.Provider) };
            if (options.IncludePlan) values.Add(SafeCsv(account.RecordedPlan));
            if (options.IncludeNotes)
            {
                var detail = await _accounts.GetAccountDetailAsync(account.AccountId, cancellationToken);
                values.Add(SafeCsv(detail?.Notes));
            }
            if (options.IncludeDates) values.Add(SafeCsv(account.TrackingDate.ToString("O")));
            builder.AppendLine(string.Join(',', values));
        }

        return builder.ToString();
    }

    private static string SafeCsv(string? value)
    {
        var normalized = value ?? string.Empty;
        if (normalized.Length > 0 && normalized[0] is '=' or '+' or '-' or '@') normalized = "'" + normalized;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }
}
