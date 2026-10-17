namespace FlashAI.Modules.ImportExport.Contracts;

public sealed record ImportRowResult(int RowNumber, bool IsValid, IReadOnlyList<string> Errors);
