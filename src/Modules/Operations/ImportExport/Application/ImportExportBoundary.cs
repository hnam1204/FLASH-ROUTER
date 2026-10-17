using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.ImportExport.Application;

public sealed class ImportExportBoundary
{
    public Result RejectUntilPreviewPipelineExists()
        => Result.Failure("IMPORT_NOT_ENABLED", "Preview and commit workflows are deferred to Phase 3.");
}
