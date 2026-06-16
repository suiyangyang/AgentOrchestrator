using System.Collections.Generic;
using System.Threading;

namespace AgentOrchestrator.App.Models.Chat;

public interface IChatTransport
{
    IAsyncEnumerable<IChatStreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
