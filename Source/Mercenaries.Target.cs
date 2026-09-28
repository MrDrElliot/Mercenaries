using LuminaBuildTool.Configuration;

public class MercenariesTarget : LuminaGameTargetRules
{
    public MercenariesTarget(TargetInfo Target)
        : base(Target)
    {
        LaunchModuleName = "Mercenaries";

        // This target builds a library the editor loads, so Run and Debug launch the editor with
        // this project already open rather than trying to execute the library.
        SetProjectFileToOpen("../Mercenaries.lproject");
    }
}
