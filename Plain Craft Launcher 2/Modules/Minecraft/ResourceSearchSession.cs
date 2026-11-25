namespace PCL;

public readonly record struct ResourceSearchOrigin(ModComp.CompType Type, string? Version, ModComp.CompLoaderType Loader);
internal sealed record CategoryFilter(string Label, string? Tag, int Indent = 0, bool Enabled = true,
    string? Tooltip = null, int TooltipOffset = 0);

internal sealed record ResourceQuery(string Text, string? Version, string? Tag, ModComp.CompLoaderType Loader,
    ModComp.CompSourceType Source, ModComp.CompSortType Sort);

internal sealed class ResourceSearchSession
{
    internal const int PageSize = 40;
    internal static McInstance? SuggestedInstance { get; set; }
    internal ModComp.CompType Type { get; }
    internal ModComp.CompProjectStorage Storage { get; private set; } = new();
    internal int Page { get; private set; }
    internal ModLoader.LoaderTask<ModComp.CompProjectRequest, int> Loader { get; }
    internal ResourceQuery Query { get; set; } = new("", null, "", ModComp.CompLoaderType.Any, ModComp.CompSourceType.Any, default);

    internal ResourceSearchSession(ModComp.CompType type)
    {
        Type = type;
        Loader = new(PCL.Core.App.Localization.Lang.Text("Download.Comp.List.Source.ResourceFetch", ModComp.GetCompTypeName(type)),
            ModComp.CompProjectsGet, Request) { reloadTimeout = 60 * 1000 };
    }

    internal ModComp.CompProjectRequest Request() => new(Type, Storage, (Page + 1) * PageSize)
    {
        searchText = Query.Text, gameVersion = Query.Version, tag = Query.Tag, modLoader = Query.Loader,
        source = Query.Source, sort = Query.Sort
    };

    internal void Search(ResourceQuery query)
    {
        Query = query;
        Page = 0;
        object input = Request();
        if (Loader.ShouldStart(ref input)) Storage = new ModComp.CompProjectStorage();
        Loader.Start(Request());
    }

    internal void ChangePage(int page, ResourceQuery query)
    {
        if (Query != query) { Search(query); return; }
        Page = Math.Max(0, page);
        Loader.Start(Request());
    }

    internal void Refresh()
    {
        Page = 0;
        Storage = new ModComp.CompProjectStorage();
        Loader.Start(Request(), true);
    }

    internal static IReadOnlyList<CategoryFilter> Filters(ModComp.CompType type) => type switch
    {
        ModComp.CompType.DataPack =>
        [
            new("Common.Option.All", ""),
            new("Download.Comp.Category.WorldGen", "/worldgen"),
            new("Download.Comp.Category.Technology", "6951/technology"),
            new("Download.Comp.Category.GameMechanics", "/game-mechanics"),
            new("Download.Comp.Category.Transportation", "/transportation"),
            new("Download.Comp.Category.Storage", "/storage"),
            new("Download.Comp.Category.Magic", "6952/magic"),
            new("Download.Comp.Category.Adventure", "6948/adventure"),
            new("Download.Comp.Category.DataPack.Fantasy", "6949/"),
            new("Download.Comp.Category.Decoration", "/decoration"),
            new("Download.Comp.Category.Mobs", "/mobs"),
            new("Download.Comp.Category.Utility", "6953/utility"),
            new("Download.Comp.Category.Equipment", "/equipment"),
            new("Download.Comp.Category.Optimization", "/optimization"),
            new("Download.Comp.Category.Server", "/social"),
            new("Download.Comp.Category.Library", "6950/library"),
            new("Download.Comp.Category.Mod.ModRelated", "6946/")
        ],
        ModComp.CompType.Mod =>
        [
            new("Common.Option.All", ""),
            new("Download.Comp.Category.WorldGen", "406/worldgen"),
            new("Download.Comp.Category.Biomes", "407/", Indent: 15),
            new("Download.Comp.Category.Dimensions", "410/", Indent: 15),
            new("Download.Comp.Category.OresResources", "408/", Indent: 15),
            new("Download.Comp.Category.Structures", "409/", Indent: 15),
            new("Download.Comp.Category.Technology", "412/technology"),
            new("Download.Comp.Category.PipesLogistics", "415/", Indent: 15),
            new("Download.Comp.Category.Automation", "4843/", Indent: 15),
            new("Download.Comp.Category.Energy", "417/", Indent: 15),
            new("Download.Comp.Category.Redstone", "4558/", Indent: 15),
            new("Download.Comp.Category.FoodCooking", "436/food"),
            new("Download.Comp.Category.Farming", "416/", Indent: 15),
            new("Download.Comp.Category.GameMechanics", "/game-mechanics"),
            new("Download.Comp.Category.Transportation", "414/transportation"),
            new("Download.Comp.Category.Storage", "420/storage"),
            new("Download.Comp.Category.Magic", "419/magic"),
            new("Download.Comp.Category.Adventure", "422/adventure"),
            new("Download.Comp.Category.Decoration", "424/decoration"),
            new("Download.Comp.Category.Mobs", "411/mobs"),
            new("Download.Comp.Category.Utility", "5191/utility"),
            new("Download.Comp.Category.Equipment", "434/equipment"),
            new("Download.Comp.Category.Creative", "9026/"),
            new("Download.Comp.Category.Optimization", "6814/optimization"),
            new("Download.Comp.Category.Display", "423/"),
            new("Download.Comp.Category.Server", "435/social"),
            new("Download.Comp.Category.Library", "421/library")
        ],
        ModComp.CompType.ModPack =>
        [
            new("Common.Option.All", ""),
            new("Download.Comp.Category.Multiplayer", "4484/"),
            new("Download.Comp.Category.Optimization", "/optimization"),
            new("Download.Comp.Category.Modpack.Hardcore", "4479/challenging"),
            new("Download.Comp.Category.Combat", "4483/combat"),
            new("Download.Comp.Category.Modpack.Quests", "4478/quests"),
            new("Download.Comp.Category.Technology", "4472/technology"),
            new("Download.Comp.Category.Magic", "4473/magic"),
            new("Download.Comp.Category.Adventure", "4475/adventure"),
            new("Download.Comp.Category.Modpack.KitchenSink", "/kitchen-sink"),
            new("Download.Comp.Category.Modpack.Exploration", "4476/"),
            new("Download.Comp.Category.Modpack.MiniGame", "4477/"),
            new("Download.Comp.Category.Modpack.SciFi", "4471/"),
            new("Download.Comp.Category.Modpack.Skyblock", "4736/"),
            new("Download.Comp.Category.Modpack.VanillaPlus", "5128/"),
            new("Download.Comp.Category.Modpack.Ftb", "4487/", Tooltip: "Download.Comp.Category.Modpack.Ftb.ToolTip", TooltipOffset: 70),
            new("Download.Comp.Category.Modpack.MapBased", "4480/"),
            new("Download.Comp.Category.Modpack.SmallLight", "4481/lightweight"),
            new("Download.Comp.Category.Modpack.ExtraLarge", "4482/")
        ],
        ModComp.CompType.ResourcePack =>
        [
            new("Common.Option.All", ""),
            new("Download.Comp.Category.Style", null, Enabled: false),
            new("Download.Comp.Category.VanillaLike", "403/vanilla-like", Indent: 15),
            new("Download.Comp.Category.Realistic", "400/realistic", Indent: 15),
            new("Download.Comp.Category.Modern", "401/", Indent: 15),
            new("Download.Comp.Category.Medieval", "402/", Indent: 15),
            new("Download.Comp.Category.Steampunk", "399/", Indent: 15),
            new("Download.Comp.Category.Themed", "/themed", Indent: 15),
            new("Download.Comp.Category.Simplistic", "/simplistic", Indent: 15),
            new("Download.Comp.Category.Decoration", "/decoration", Indent: 15),
            new("Download.Comp.Category.Combat", "/combat", Indent: 15),
            new("Download.Comp.Category.Utility", "/utility", Indent: 15),
            new("Download.Comp.Category.Tweaks", "/tweaks", Indent: 15),
            new("Download.Comp.Category.Cursed", "/cursed", Indent: 15),
            new("Download.Comp.Category.Feature", null, Enabled: false),
            new("Download.Comp.Category.Entities", "/entities", Indent: 15),
            new("Download.Comp.Category.Audio", "/audio", Indent: 15),
            new("Download.Comp.Category.Fonts", "5244/fonts", Indent: 15),
            new("Download.Comp.Category.Models", "/models", Indent: 15),
            new("Download.Comp.Category.Locale", "/locale", Indent: 15),
            new("Download.Comp.Category.Gui", "/gui", Indent: 15),
            new("Download.Comp.Category.CoreShaders", "/core-shaders", Indent: 15),
            new("Download.Comp.Category.Animated", "404/", Indent: 15),
            new("Download.Comp.Category.ModSupport", "4465/modded", Indent: 15),
            new("Download.Comp.Category.Resolution", null, Enabled: false),
            new("Download.Comp.Category.ResourcePack.Resolution8xOrLower", "/8x-", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution16x", "393/16x", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution32x", "394/32x", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution48x", "/48x", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution64x", "395/64x", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution128x", "396/128x", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution256x", "397/256x", Indent: 15),
            new("Download.Comp.Category.ResourcePack.Resolution512xOrHigher", "398/512x+", Indent: 15)
        ],
        ModComp.CompType.Shader =>
        [
            new("Common.Option.All", ""),
            new("Download.Comp.Category.Style", null, Enabled: false),
            new("Download.Comp.Category.VanillaLike", "6555/vanilla-like", Indent: 15),
            new("Download.Comp.Category.Fantasy", "6554/fantasy", Indent: 15),
            new("Download.Comp.Category.Realistic", "6553/realistic", Indent: 15),
            new("Download.Comp.Category.SemiRealistic", "/semi-realistic", Indent: 15),
            new("Download.Comp.Category.Cartoon", "/cartoon", Indent: 15),
            new("Download.Comp.Category.Feature", null, Enabled: false),
            new("Download.Comp.Category.ColoredLighting", "/colored-lighting", Indent: 15),
            new("Download.Comp.Category.PathTracing", "/path-tracing", Indent: 15),
            new("Download.Comp.Category.Pbr", "/pbr", Indent: 15),
            new("Download.Comp.Category.Reflections", "/reflections", Indent: 15),
            new("Download.Comp.Category.Performance", null, Enabled: false),
            new("Download.Comp.Category.PerformancePotato", "/potato", Indent: 15),
            new("Download.Comp.Category.PerformanceLow", "/low", Indent: 15),
            new("Download.Comp.Category.PerformanceMedium", "/medium", Indent: 15),
            new("Download.Comp.Category.PerformanceHigh", "/high", Indent: 15)
        ],
        ModComp.CompType.World =>
        [
            new("Common.Option.All", ""),
            new("Download.Comp.Category.World.Adventure", "248/"),
            new("Download.Comp.Category.World.Creative", "249/"),
            new("Download.Comp.Category.Modpack.MiniGame", "250/"),
            new("Download.Comp.Category.World.Parkour", "251/"),
            new("Download.Comp.Category.World.Puzzle", "252/"),
            new("Download.Comp.Category.World.Survival", "253/"),
            new("Download.Comp.Category.World.ModWorld", "4464/")
        ],
        _ => [new("Common.Option.All", "")]
    };
}
