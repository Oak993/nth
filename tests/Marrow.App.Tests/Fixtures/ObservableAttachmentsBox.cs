using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// An attachment-list twin of <see cref="ObservableStringBox"/>: the <c>@Binding var attachments</c> of PromptEditor is
/// read through observation in the body, so a test that changes it from outside needs a tracked backing store.
/// </summary>
public sealed class ObservableAttachmentsBox : MainActorModel
{
    private ValueList<Attachment> held = ValueList<Attachment>.Empty;

    public ValueList<Attachment> Value { get => Read(held); set => Set(ref held, value); }

    public Binding<ValueList<Attachment>> AsBinding() => new(() => Value, v => Value = v);
}
