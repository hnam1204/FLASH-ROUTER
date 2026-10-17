using System.Reflection;
using FlashAI.Modules.Accounts;
using FlashAI.Modules.Connections;
using FlashAI.Modules.Operations;
using FlashAI.Modules.Workspace;

namespace FLASH_ROUTER.Components;

public static class ModuleAssemblyCatalog
{
    public static IReadOnlyList<Assembly> All { get; } =
    [
        typeof(AccountsModuleMarker).Assembly,
        typeof(ConnectionsModuleMarker).Assembly,
        typeof(OperationsModuleMarker).Assembly,
        typeof(WorkspaceModuleMarker).Assembly
    ];
}
