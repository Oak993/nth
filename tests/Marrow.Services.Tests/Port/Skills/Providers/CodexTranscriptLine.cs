// PORT-ONLY: SK-11b helper. One line of the committed fixture transcript: a server notification or a server request.
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Port.Skills.Providers;

internal sealed record CodexTranscriptLine(string Kind, int? Id, string Method, JSONValue Params);
