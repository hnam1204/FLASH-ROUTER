namespace FlashAI.Foundation.Contracts;

public sealed record ModuleDescriptor(
    string Key,
    string DisplayName,
    string Route,
    string AssemblyName,
    string Status);
