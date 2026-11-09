using System.Windows;
using ReLight.Core;

namespace ReLight.Services;

/// <summary>Puts every interface text into application resources as "Loc.key" for DynamicResource.</summary>
public static class LocResources
{
    public static void Attach(ResourceDictionary resources)
    {
        Fill(resources);
        Loc.Changed += () => Fill(resources);
    }

    static void Fill(ResourceDictionary resources)
    {
        foreach (var key in Loc.Keys)
            resources[Loc.ResourcePrefix + key] = Loc.T(key);
    }
}
