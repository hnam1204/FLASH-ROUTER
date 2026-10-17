namespace FlashAI.Modules.ImportExport.Integration;

public sealed class CsvImportBoundary
{
    public bool RejectFormulaLikeCells => true;

    public bool IsMetadataOnly => true;
}
