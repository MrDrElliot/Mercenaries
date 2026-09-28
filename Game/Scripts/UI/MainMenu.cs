using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public sealed class MainMenuModel : ViewModel
{
    [Bind] public string Status { get; set; } = string.Empty;
    [Bind] public bool Loading { get; set; }

    [BindCommand]
    public void Play(int Merc) => MainMenu.Instance?.StartGame((EMercenary)Merc);

    [BindCommand]
    public void Quit() => Game.Quit();
}

// The front end, which travels into the island at runtime rather than starting there.
public sealed class MainMenu : EntityScript
{
    public const string MenuMap = "/Game/Content/Maps/MainMenu";
    public const string IslandMap = "/Game/Content/Maps/Venezuela";

    // Read by GameDirector once the island loads, since a static outlives the travel.
    public static EMercenary? ChosenMercenary;

    public static MainMenu? Instance;

    // MERCS_SOAK set to a number of seconds cycles menu and island unattended, one visit of that length each, for stress runs.
    public static readonly float SoakSeconds = float.TryParse(Environment.GetEnvironmentVariable("MERCS_SOAK"), out float Seconds) ? Seconds : 0.0f;
    public static int SoakCycle;
    private float MenuTime;

    [Property(Category = "UI")]
    public string Document = "/Game/Content/UI/MainMenu.rml";

    private readonly MainMenuModel Model = new();
    private UIDataModel? Binding;
    private UIDocument Screen;

    public override void OnReady()
    {
        Instance = this;
        Binding = World.UI.AddModel("menu", Model);
        Screen = World.UI.LoadDocument(Document);
        if (!Screen.IsValid)
        {
            Debug.LogError($"MainMenu: failed to load '{Document}'.");
            return;
        }

        Screen.Show(false, true);
        World.UI.EnableCursor();
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (Model.Loading)
        {
            return;
        }

        MenuTime += DeltaTime;
        if (CInputLibrary.WasKeyPressed(World, EKey.Enter) || (SoakSeconds > 0.0f && MenuTime > 2.0f))
        {
            StartGame((EMercenary)(SoakCycle % 3));
        }
    }

    public override void OnDetach()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Screen.Close();
        Binding?.Dispose();
    }

    public void StartGame(EMercenary Merc)
    {
        if (Model.Loading)
        {
            return;
        }

        ChosenMercenary = Merc;
        Model.Loading = true;
        Model.Status = $"Deploying {MercPlayer.MercName(Merc)}...";
        Game.OpenLevel(IslandMap);
    }

    public static void ReturnToMenu() => Game.OpenLevel(MenuMap);
}
