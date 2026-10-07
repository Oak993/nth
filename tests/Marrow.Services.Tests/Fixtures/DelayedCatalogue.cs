// PORT-ONLY: fake-only palette pipeline fixture, separated for unchanged traceability guards.
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class DelayedCatalogue(HttpMessageHandler transport) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker=new(transport,disposeHandler:false);
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/models",StringComparison.Ordinal)){Entered.TrySetResult();await Release.Task.WaitAsync(ct);}
            return await invoker.SendAsync(request,ct);
        }
        protected override void Dispose(bool disposing){if(disposing)invoker.Dispose();base.Dispose(disposing);}
    }
