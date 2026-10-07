using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;
namespace Marrow.App.Tests.Port.Voice.Cuda;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class CudaHardwareCardMountTests(HeadlessSession headless)
{
    // The handler never opens a socket. A real button enters its held request, then the mounted x cancels it.
    [Fact]
    public void The_factory_offer_is_mounted_below_the_recommendation_and_its_buttons_start_and_cancel_the_same_download()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "CudaMount", Guid.NewGuid().ToString("N"));
            using var handler = new CudaHeldRequest();
            var (_, pin) = CudaPackFixture.Pack();
            var downloader = new Marrow.Services.Port.Downloads.Downloader(Path.Combine(root, "parts"), new DownloadPolicy(["example.invalid"]), handler: handler);
            using var offer = new CudaOffer(new ScriptedProbe(ScriptedMachines.OwnersDesktop), new CudaPackInstaller(root, downloader), pin, new ScriptedCudaCompanions());
            using var factory = CudaOfferComposition.Factory.Override(() => offer);
            var settings = new Marrow.App.AppSettings();
            var card = new VoiceHardwareCard(SpeechModelController.For(settings), settings);
            var window = new Avalonia.Controls.Window { Width = 700, Height = 600, Content = card };
            try
            {
                window.Show(); window.UpdateLayout();
                HeadlessSession.RunDispatcherUntil(offer.EvaluateAsync(CancellationToken.None), TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                var row = LogicalQuery.SelfAndDescendants<CudaOfferView>(card).Single();
                Assert.True(row.IsVisible);
                Assert.Equal("NVIDIA CUDA", row.Title);
                var stack = (StackPanel)row.Parent!;
                Assert.Same(card.RecommendationFold, stack.Children[stack.Children.IndexOf(row) - 1]);
                row.Download.Activate();
                HeadlessSession.RunDispatcherUntil(handler.Entered.Task, TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.Cancel.IsVisible);
                var running = offer.StartAsync();
                row.Cancel.Activate();
                HeadlessSession.RunDispatcherUntil(running, TimeSpan.FromSeconds(10));
                running.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(CudaOfferState.Offer, offer.Row.State);
                Assert.True(row.Download.IsVisible);
            }
            finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
}
