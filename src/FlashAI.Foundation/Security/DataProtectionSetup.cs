using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.Foundation.Security;

public static class DataProtectionSetup
{
    public static IServiceCollection AddFlashDataProtection(this IServiceCollection services, string keyRingPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("FLASH ROUTER requires Windows DPAPI for the local key ring.");
        }

        Directory.CreateDirectory(keyRingPath);
        services.AddDataProtection()
            .SetApplicationName("FLASH ROUTER")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
            .ProtectKeysWithDpapi(protectToLocalMachine: false);
        return services;
    }
}
