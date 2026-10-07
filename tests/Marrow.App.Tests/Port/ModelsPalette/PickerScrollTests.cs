using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
// PORT-ONLY: Q-PICKER A scrollIntoView({block:'nearest',behavior:'smooth'}); ED-616.
[Collection("Headless")]
[Trait("Decision","ED-NEW-modelspalette-5")]
public sealed class PickerScrollTests(HeadlessSession headless)
{
    [Fact]
    public void Keyboard_scroll_moves_toward_the_nearest_row_and_reduce_motion_lands_immediately()=>headless.OnUiThread(()=>
    {
        using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var runtime=new TestRuntime();var frames=new ManualFrameSource();using var source=FrameClock.Source.Override(frames);
        var picker=new OpenRouterPicker(Enumerable.Range(0,20).Select(i=>new PickerModel("acme/"+i,"Model "+i,"Acme","#10AA60")).ToArray(),"acme/0",_=>{},()=>{});
        var window=new Avalonia.Controls.Window{Content=picker};window.Show();
        try
        {
            picker.Search.Text="model";Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            for(var i=0;i<14;i++)picker.Search.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Down});
            Assert.True(picker.TargetScrollOffset>0);Assert.Equal(0,picker.ScrollOffset);
            runtime.Advance(TimeSpan.FromMilliseconds(170));frames.Advance(TimeSpan.FromMilliseconds(170));Dispatcher.UIThread.RunJobs();
            Assert.InRange(picker.ScrollOffset,double.Epsilon,picker.TargetScrollOffset-double.Epsilon);
            EnvironmentValues.SetAccessibilityReduceMotion(window,true);picker.Search.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Down});
            Assert.Equal(picker.TargetScrollOffset,picker.ScrollOffset);
        }
        finally{window.Close();}
    });
}
