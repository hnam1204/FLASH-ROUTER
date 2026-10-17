using Microsoft.EntityFrameworkCore;

namespace FlashAI.Foundation.Persistence;

public interface IFlashAiModelContributor
{
    void Configure(ModelBuilder modelBuilder);
}
