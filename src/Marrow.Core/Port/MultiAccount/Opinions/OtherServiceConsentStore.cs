// PORT-ONLY: OD-0120 user-chosen Don't ask again; scope is one action and destination service, ED-471.
using System.Text.Json;
using Marrow.Core.Models;
namespace Marrow.Core.Port.MultiAccount.Opinions;
public sealed class OtherServiceConsentStore
{
    public const string SettingsKey="multiAccountOtherServiceConsent";
    private readonly ISettingsStore settings;
    private readonly HashSet<string> grants=[];
    public bool SaveBlocked {get;}
    public OtherServiceConsentStore(ISettingsStore settings)
    {
        this.settings=settings;
        if(settings.Object(SettingsKey) is not {} value)return;
        if(value is not byte[] bytes){SaveBlocked=true;return;}
        try
        {
            var loaded=JsonSerializer.Deserialize<string[]>(bytes);
            var allowed=SwiftEnum<ProviderKind>.AllCases.SelectMany(s=>new[]{Key(ConsentPurpose.Opinions,s),Key(ConsentPurpose.Link,s)}).ToHashSet(StringComparer.Ordinal);
            if(loaded is null||loaded.Any(x=>x is null||!allowed.Contains(x))){SaveBlocked=true;return;}
            grants.UnionWith(loaded);
        }
        catch(JsonException){SaveBlocked=true;}
    }
    private static string Key(ConsentPurpose purpose,ProviderKind service)=>(purpose switch {ConsentPurpose.Opinions=>"opinions:",ConsentPurpose.Link=>"link:",_=>throw new ArgumentOutOfRangeException(nameof(purpose))})+service.Id;
    public bool Permits(ConsentPurpose purpose,ProviderKind service)=>grants.Contains(Key(purpose,service));
    public void Remember(ConsentPurpose purpose,ProviderKind service)
    {
        if(SaveBlocked)throw new InvalidDataException("Other-service consent could not be read; the original value was preserved.");
        var next=grants.Append(Key(purpose,service)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        settings.Set(SettingsKey,JsonSerializer.SerializeToUtf8Bytes(next));grants.UnionWith(next);
    }
}
