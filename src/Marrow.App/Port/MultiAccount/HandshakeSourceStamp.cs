using Marrow.Core.Models;

namespace Marrow.App.Runtime;

internal sealed record HandshakeSourceStamp(long Revision, long Epoch, ChatThread? Thread);
