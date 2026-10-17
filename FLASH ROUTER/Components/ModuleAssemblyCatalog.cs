using System.Reflection;
using FlashAI.Modules.Activity;
using FlashAI.Modules.Authenticator;
using FlashAI.Modules.BackupRestore;
using FlashAI.Modules.Connections;
using FlashAI.Modules.Dashboard;
using FlashAI.Modules.Diagnostics;
using FlashAI.Modules.ImportExport;
using FlashAI.Modules.Reminders;
using FlashAI.Modules.Settings;
using FlashAI.Modules.Subscriptions;

namespace FLASH_ROUTER.Components;

public static class ModuleAssemblyCatalog
{
    public static IReadOnlyList<Assembly> All { get; } =
    [
        typeof(DashboardModuleMarker).Assembly,
        typeof(ConnectionsModuleMarker).Assembly,
        typeof(SubscriptionsModuleMarker).Assembly,
        typeof(AuthenticatorModuleMarker).Assembly,
        typeof(ImportExportModuleMarker).Assembly,
        typeof(RemindersModuleMarker).Assembly,
        typeof(BackupRestoreModuleMarker).Assembly,
        typeof(ActivityModuleMarker).Assembly,
        typeof(SettingsModuleMarker).Assembly,
        typeof(DiagnosticsModuleMarker).Assembly
    ];
}
