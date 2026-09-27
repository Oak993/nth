using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

    internal sealed class HydraPanelContext : IDisposable
    {
        private readonly AppModelHarness harness = new();
        private readonly IDisposable actor;
        private readonly IDisposable frameClock;
        private readonly IDisposable shared;
        private readonly ManualFrameSource frames = new();
        private readonly ThreadRuntime runtime;
        private readonly WindowLiveResize resize;
        public AppModel Model { get; }
        public HydraPanel Panel { get; }
        public Avalonia.Controls.Window Window { get; }
        public ChatThread First { get; }
        public ChatThread Second { get; }

        public HydraPanelContext()
        {
            actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            frameClock = FrameClock.Source.Override(frames);
            var project = new Project("isolated",harness.Scratch);
            var lead = new ChatThread(project.Id,ProviderKind.Codex,null,null,RuntimeMode.Supervised);
            First = Head(project,lead,0); Second = Head(project,lead,1);
            harness.WriteLibrary([project],[lead,First,Second]);
            var manager = harness.Main.OnMain(() => new WindowManager());
            Model = manager.Model;
            foreach(var head in new[]{First,Second}) Model.UpdateThread(head.Id,t=>t with { Hydra=t.Hydra! with { HydraHeadInfoStatus=HydraHeadInfo.Status.Running, FinishedAt=null } });
            shared = WindowManager.OverrideShared(manager);
            runtime = harness.Main.OnMain(() => { Model.Settings.HydraShowsHeadDetails=false; var r=Model.Runtime(lead.Id); r.HydraSelectedHeadID=First.Id; return r; });
            Window = new Avalonia.Controls.Window { Width=420,Height=280 };
            resize = new WindowLiveResize(Window);
            Panel = harness.Main.OnMain(() => new HydraPanel(runtime,[],new Size(420,280),null,null,_=>{},()=>{},()=>{},popOut:_=>{}));
            typeof(HydraPanel).GetProperty("StageFactory",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(Panel,(Func<ChatThread,Control>)(head => new Border { Tag=head.Id }));
            AppEnvironment.SetAppModel(Panel,Model); AppEnvironment.SetWindowLiveResize(Panel,resize);
            // Attach real controls without asking the headless backend to shape Inter Medium glyphs.
            // The sweep is independently laid out in the wave test; fonts are unchanged.
            Panel.IsVisible=false;
            Window.Content=Panel; Window.Show(); Drain();
        }

        private static ChatThread Head(Project project,ChatThread lead,long index) => new(project.Id,ProviderKind.Codex,null,null,RuntimeMode.Supervised)
        {
            ParentThreadID=lead.Id, IsInPanel=true,
            Hydra=new HydraHeadInfo(index,"Task",HydraHeadInfo.Kind.Native,HydraHeadInfo.Origin.Delegated) {CanStop=false},
        };
        public T[] Find<T>() where T : class => LogicalQuery.SelfAndDescendants<T>(Panel).ToArray();
        public Guid StageId(Border stage) => (Guid)stage.Tag!;
        public Border Stage(Guid id) => Find<Border>().Single(s => s.Tag is Guid value && value==id);
        public Border[] Stages() => Find<Border>().Where(s=>s.Tag is Guid).ToArray();
        public ChromeCircleButton[] PopOutButtons() => Find<ChromeCircleButton>().Where(b=>LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i=>i.SymbolName=="arrow.up.right")).ToArray();
        public void Status(ChatThread head,HydraHeadInfo.Status value) { Model.UpdateThread(head.Id,t=>t with {Hydra=t.Hydra! with {HydraHeadInfoStatus=value,FinishedAt=value==HydraHeadInfo.Status.Running?null:SwiftDate.Now}}); Drain(); }
        public void InPanel(ChatThread head,bool value) {Model.UpdateThread(head.Id,t=>t with {IsInPanel=value}); Drain();}
        public void Select(ChatThread head) {runtime.HydraSelectedHeadID=head.Id; Drain();}
        public void Resize(bool active)
        {
            var handled=false;
            if(active) {resize.WndProc(0,0x0231,0,0,ref handled);resize.WndProc(0,0x0214,0,0,ref handled);}
            else resize.WndProc(0,0x0232,0,0,ref handled);
            Drain();
        }
        public void Drain() {harness.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); harness.Main.RunUntilIdle();}
        public void Tick(double seconds) {var span=TimeSpan.FromTicks((long)Math.Round(seconds*TimeSpan.TicksPerSecond)); harness.Main.Advance(span); frames.Advance(span); Drain();}
        public void Dispose() {Window.Close(); Drain(); shared.Dispose(); frameClock.Dispose(); actor.Dispose(); harness.Dispose();}
    }
