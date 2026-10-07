using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeShaderGraphTests
{
    [Fact]
    [Trait("Decision","ED-248")]
    public void Ink_shader_graph_binds_the_installed_skia_blend_and_colour_operations()
    {
        var api=typeof(SKCanvas).Assembly.GetType("SkiaSharp.SkiaApi",true)!;
        foreach(var name in new[]{"sk_shader_new_blend","sk_shader_new_color"}) {
            var ours=SkiaShaders.Bindings.SingleOrDefault(m=>m.Name==name);Assert.NotNull(ours);
            var theirs=api.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);Assert.NotNull(theirs);
            var a=ours.GetParameters();var b=theirs.GetParameters();Assert.Equal(b.Length,a.Length);
            for(var i=0;i<a.Length;i++)Assert.Equal(Size(b[i].ParameterType),Size(a[i].ParameterType));
            Assert.Equal(Size(theirs.ReturnType),Size(ours.ReturnType));
        }
        static int Size(Type t)=>t.IsPointer||t.IsByRef||t.IsArray||t==typeof(nint)?IntPtr.Size:Marshal.SizeOf(t.IsEnum?Enum.GetUnderlyingType(t):t);
    }
}
