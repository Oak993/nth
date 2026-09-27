using System.Threading.Tasks;
using AvaloniaWindow = Avalonia.Controls.Window;
using Marrow.App.Port;
using Marrow.Core.Port;
namespace Marrow.App.Tests.Fixtures;
    internal sealed class ComposerDialogProbe : IComposerFileDialog
    {
        public (bool Multiple, bool Directories, string Prompt)? Options;
        public ValueList<string>? Result { get; set; }
        public System.Threading.Tasks.Task<ValueList<string>?> Choose(AvaloniaWindow? owner, bool allowsMultipleSelection, bool canChooseDirectories, string prompt)
        {
            Options = (allowsMultipleSelection, canChooseDirectories, prompt);
            return System.Threading.Tasks.Task.FromResult(Result);
        }
    }


