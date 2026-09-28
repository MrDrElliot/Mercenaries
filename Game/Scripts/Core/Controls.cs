using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EControl
{
    Jump,
    Sprint,
    Fire,
    Aim,
    Reload,
    Interact,
    SwitchWeapon,
    Grenade,
    PlaceCharge,
    Detonate,
    Melee,
    CallSupport,
    CycleSupport,
    Pda,
    Descend,
    Help,
}

// Reads an authored input action when the project has one, otherwise falls back to a fixed key.
public static class Controls
{
    private readonly struct FBinding
    {
        public readonly string Action;
        public readonly EKey Key;
        public readonly EMouseKey Mouse;
        public readonly bool bMouse;

        public FBinding(string Action, EKey Key)
        {
            this.Action = Action;
            this.Key = Key;
            Mouse = EMouseKey.Button0;
            bMouse = false;
        }

        public FBinding(string Action, EMouseKey Mouse)
        {
            this.Action = Action;
            Key = EKey.Space;
            this.Mouse = Mouse;
            bMouse = true;
        }
    }

    private static readonly Dictionary<EControl, FBinding> Bindings = new()
    {
        { EControl.Jump, new FBinding("Jump", EKey.Space) },
        { EControl.Sprint, new FBinding("Sprint", EKey.LeftShift) },
        { EControl.Fire, new FBinding("Fire", EMouseKey.ButtonLeft) },
        { EControl.Aim, new FBinding("Aim", EMouseKey.ButtonRight) },
        { EControl.Reload, new FBinding("Reload", EKey.R) },
        { EControl.Interact, new FBinding("Interact", EKey.E) },
        { EControl.SwitchWeapon, new FBinding("SwitchWeapon", EKey.Q) },
        { EControl.Grenade, new FBinding("Grenade", EKey.G) },
        { EControl.PlaceCharge, new FBinding("PlaceCharge", EKey.C) },
        { EControl.Detonate, new FBinding("Detonate", EKey.V) },
        { EControl.Melee, new FBinding("Melee", EKey.F) },
        { EControl.CallSupport, new FBinding("CallSupport", EKey.T) },
        { EControl.CycleSupport, new FBinding("CycleSupport", EKey.X) },
        { EControl.Pda, new FBinding("Pda", EKey.Tab) },
        { EControl.Descend, new FBinding("Descend", EKey.LeftControl) },
        { EControl.Help, new FBinding("Help", EKey.F1) },
    };

    private static readonly Dictionary<string, bool> Authored = new();

    public static bool Enabled = true;

    public static void Reset()
    {
        Authored.Clear();
        Enabled = true;
    }

    public static bool IsReceiving => Enabled && CInputLibrary.IsReceivingInput(Mercs.World);

    public static bool Down(EControl Control)
    {
        if (!IsReceiving)
        {
            return false;
        }

        FBinding Binding = Bindings[Control];
        if (HasAction(Binding.Action))
        {
            return CInputLibrary.IsActionDown(Mercs.World, Binding.Action);
        }

        return Binding.bMouse ? CInputLibrary.IsMouseButtonDown(Mercs.World, Binding.Mouse) : CInputLibrary.IsKeyDown(Mercs.World, Binding.Key);
    }

    public static bool Pressed(EControl Control)
    {
        if (!IsReceiving)
        {
            return false;
        }

        FBinding Binding = Bindings[Control];
        if (HasAction(Binding.Action))
        {
            return CInputLibrary.WasActionPressed(Mercs.World, Binding.Action);
        }

        return Binding.bMouse ? CInputLibrary.WasMouseButtonPressed(Mercs.World, Binding.Mouse) : CInputLibrary.WasKeyPressed(Mercs.World, Binding.Key);
    }

    public static bool Released(EControl Control)
    {
        if (!IsReceiving)
        {
            return false;
        }

        FBinding Binding = Bindings[Control];
        if (HasAction(Binding.Action))
        {
            return CInputLibrary.WasActionReleased(Mercs.World, Binding.Action);
        }

        return Binding.bMouse ? CInputLibrary.WasMouseButtonReleased(Mercs.World, Binding.Mouse) : CInputLibrary.WasKeyReleased(Mercs.World, Binding.Key);
    }

    public static bool KeyPressed(EKey Key) => IsReceiving && CInputLibrary.WasKeyPressed(Mercs.World, Key);

    public static bool KeyDown(EKey Key) => IsReceiving && CInputLibrary.IsKeyDown(Mercs.World, Key);

    // X is strafe, Y is forward.
    public static FVector2 Move()
    {
        if (!IsReceiving)
        {
            return FVector2.Zero;
        }

        if (HasAction("MoveForward") && HasAction("MoveRight"))
        {
            return new FVector2(CInputLibrary.GetActionAxis(Mercs.World, "MoveRight"), CInputLibrary.GetActionAxis(Mercs.World, "MoveForward"));
        }

        float Forward = (KeyDown(EKey.W) ? 1.0f : 0.0f) - (KeyDown(EKey.S) ? 1.0f : 0.0f);
        float Right = (KeyDown(EKey.D) ? 1.0f : 0.0f) - (KeyDown(EKey.A) ? 1.0f : 0.0f);
        return new FVector2(Right, Forward);
    }

    // Degrees of yaw and pitch this frame, positive pitch looking up.
    public static FVector2 Look(float Sensitivity)
    {
        if (!IsReceiving)
        {
            return FVector2.Zero;
        }

        FVector2 Delta = HasAction("Look") ? CInputLibrary.GetActionAxis2D(Mercs.World, "Look") : CInputLibrary.GetMouseDelta(Mercs.World) * new FVector2(1.0f, -1.0f);
        return Delta * Sensitivity;
    }

    public static float Wheel() => IsReceiving ? CInputLibrary.GetMouseWheel(Mercs.World) : 0.0f;

    private static bool HasAction(string Action)
    {
        if (!Authored.TryGetValue(Action, out bool bFound))
        {
            bFound = CInputLibrary.FindActionIndex(Action) >= 0;
            Authored[Action] = bFound;
            if (!bFound)
            {
                Debug.LogWarning($"Mercenaries: input action '{Action}' is not authored, using its default key.");
            }
        }

        return bFound;
    }
}
