using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;

namespace GameData.Loading;

public class IndexService
{
    private string? _streamingAssetsRoot;
    private LangIndex? _langIndex;

    public DbIndex? DbIndex { get; private set; }
    public HeroesIndex? HeroesIndex { get; private set; }
    public SpellsIndex? SpellsIndex { get; private set; }
    public SkillsIndex? SkillsIndex { get; private set; }
    public ArtifactsIndex? ArtifactsIndex { get; private set; }
    public ItemSetsIndex? ItemSetsIndex { get; private set; }
    public BuildingsIndex? BuildingsIndex { get; private set; }
    public SubclassesIndex? SubclassesIndex { get; private set; }
    public MapObjectsIndex? MapObjectsIndex { get; private set; }
    public FactionLawIndex? FactionLawIndex { get; private set; }
    public AbilityIndex? AbilityIndex { get; private set; }
    public HeroSpecializationsIndex? HeroSpecializationsIndex { get; private set; }
    public DifficultiesIndex? DifficultiesIndex { get; private set; }
    public DbAccessor? DbAccessor { get; private set; }
    public LangIndex? LangIndex => _langIndex;
    public string? StreamingAssetsRoot => _streamingAssetsRoot;

    public void SetDbIndex(DbIndex? dbIndex) => DbIndex = dbIndex;
    public void SetDbAccessor(DbAccessor? dbAccessor) => DbAccessor = dbAccessor;
    public void SetLangIndex(LangIndex? langIndex) => _langIndex = langIndex;
    public void SetStreamingAssetsRoot(string? root) => _streamingAssetsRoot = root;

    public void CreateIndexes()
    {
        CreateSpellsIndex();
        CreateSkillsIndex();
        CreateHeroSpecializationsIndex();
        CreateHeroesIndex();
        if (HeroesIndex != null && HeroSpecializationsIndex != null)
            HeroesIndex.ApplySpecializationIcons(HeroSpecializationsIndex);
        CreateArtifactsIndex();
        CreateItemSetsIndex();
        CreateBuildingsIndex();
        CreateSubclassesIndex();
        CreateMapObjectsIndex();
        CreateFactionLawIndex();
        CreateAbilityIndex();
        CreateDifficultiesIndex();
    }

    public void CreateSpellsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var spellsIndex = new SpellsIndex();
        spellsIndex.Scan(_streamingAssetsRoot);
        SpellsIndex = spellsIndex;
    }

    public void CreateSkillsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var skillsIndex = new SkillsIndex();
        skillsIndex.ScanSkills(_streamingAssetsRoot);
        SkillsIndex = skillsIndex;
    }

    public void CreateHeroesIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var heroesIndex = new HeroesIndex();
        heroesIndex.ScanHeroes(_streamingAssetsRoot);
        if (_langIndex != null)
            heroesIndex.SupplementFromLang(_langIndex);
        HeroesIndex = heroesIndex;
    }

    public void CreateArtifactsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var artifactsIndex = new ArtifactsIndex();
        artifactsIndex.Scan(_streamingAssetsRoot);
        ArtifactsIndex = artifactsIndex;
    }

    public void CreateItemSetsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var itemSetsIndex = new ItemSetsIndex();
        itemSetsIndex.Scan(_streamingAssetsRoot);
        ItemSetsIndex = itemSetsIndex;
    }

    public void CreateBuildingsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var buildingsIndex = new BuildingsIndex();
        buildingsIndex.Scan(_streamingAssetsRoot);

        if (_langIndex != null)
        {
            buildingsIndex.SupplementFromLang(_langIndex);
        }

        BuildingsIndex = buildingsIndex;
    }

    public void CreateSubclassesIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var subclassesIndex = new SubclassesIndex();
        subclassesIndex.Scan(_streamingAssetsRoot);
        SubclassesIndex = subclassesIndex;
    }

    public void CreateMapObjectsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var mapObjectsIndex = new MapObjectsIndex();
        mapObjectsIndex.Scan(_streamingAssetsRoot);
        MapObjectsIndex = mapObjectsIndex;
    }

    public void CreateFactionLawIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var factionLawIndex = new FactionLawIndex();
        factionLawIndex.Scan(_streamingAssetsRoot);

        if (_langIndex != null)
        {
            factionLawIndex.SupplementFromLang(_langIndex);
        }

        FactionLawIndex = factionLawIndex;
    }

    public void CreateAbilityIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var abilityIndex = new AbilityIndex();
        abilityIndex.ScanAbilities(_streamingAssetsRoot);
        AbilityIndex = abilityIndex;
    }

    public void CreateHeroSpecializationsIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var heroSpecializationsIndex = new HeroSpecializationsIndex();
        heroSpecializationsIndex.Scan(_streamingAssetsRoot);
        HeroSpecializationsIndex = heroSpecializationsIndex;
    }

    public void CreateDifficultiesIndex()
    {
        if (string.IsNullOrEmpty(_streamingAssetsRoot)) return;
        var difficultiesIndex = new DifficultiesIndex();
        difficultiesIndex.Scan(_streamingAssetsRoot);
        DifficultiesIndex = difficultiesIndex;
    }

    public void SupplementHeroesFromLang()
    {
        if (HeroesIndex != null && _langIndex != null)
            HeroesIndex.SupplementFromLang(_langIndex);
    }

    public void Clear()
    {
        DbIndex = null;
        HeroesIndex = null;
        SpellsIndex = null;
        SkillsIndex = null;
        ArtifactsIndex = null;
        ItemSetsIndex = null;
        BuildingsIndex = null;
        SubclassesIndex = null;
        MapObjectsIndex = null;
        FactionLawIndex = null;
        AbilityIndex = null;
        HeroSpecializationsIndex = null;
        DifficultiesIndex = null;
        _langIndex = null;
        _streamingAssetsRoot = null;
    }
}
