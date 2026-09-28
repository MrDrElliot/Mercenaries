using System;
using System.Collections.Generic;
using Lumina;

namespace Mercenaries;

public sealed class Wallet
{
    public const int FuelCapacity = 3000;

    public int Cash = 30000;
    public int Fuel = 250;
    public int LifetimeEarnings;

    public void AddCash(int Amount, string Reason)
    {
        if (Amount == 0)
        {
            return;
        }

        Cash = Math.Max(0, Cash + Amount);
        if (Amount > 0)
        {
            LifetimeEarnings += Amount;
            Sfx.Ui(ESfx.Cash, 0.5f);
        }

        if (!string.IsNullOrEmpty(Reason))
        {
            Mercs.Feed.Post(Amount > 0 ? $"+${Amount:N0}  {Reason}" : $"-${-Amount:N0}  {Reason}", Amount > 0 ? ENewsTone.Money : ENewsTone.Bad);
        }
    }

    public void AddFuel(int Amount, string Reason)
    {
        int Before = Fuel;
        Fuel = Math.Clamp(Fuel + Amount, 0, FuelCapacity);
        int Delta = Fuel - Before;
        if (Delta != 0 && !string.IsNullOrEmpty(Reason))
        {
            Mercs.Feed.Post(Delta > 0 ? $"+{Delta} fuel  {Reason}" : $"{Delta} fuel  {Reason}", ENewsTone.Money);
        }
    }

    public bool CanAfford(int CashCost, int FuelCost) => Cash >= CashCost && Fuel >= FuelCost;

    public bool Spend(int CashCost, int FuelCost, string Reason)
    {
        if (!CanAfford(CashCost, FuelCost))
        {
            Mercs.Feed.Post(Cash < CashCost ? "Not enough cash." : "Not enough fuel.", ENewsTone.Bad);
            return false;
        }

        Cash -= CashCost;
        Fuel -= FuelCost;
        if (!string.IsNullOrEmpty(Reason))
        {
            Mercs.Feed.Post(FuelCost > 0 ? $"-${CashCost:N0}, -{FuelCost} fuel  {Reason}" : $"-${CashCost:N0}  {Reason}", ENewsTone.Neutral);
        }

        return true;
    }
}

public enum ENewsTone : byte
{
    Neutral,
    Good,
    Bad,
    Money,
    Faction,
    Alert,
}

public sealed class NewsFeed
{
    public sealed class FEntry
    {
        public string Text = string.Empty;
        public ENewsTone Tone;
        public float Age;
    }

    public const float EntryLife = 6.0f;
    public const int MaxEntries = 6;

    public readonly List<FEntry> Entries = new();
    public int Revision;

    public string Banner = string.Empty;
    public string BannerSub = string.Empty;
    public float BannerTime;

    public void Post(string Text, ENewsTone Tone = ENewsTone.Neutral)
    {
        Entries.Add(new FEntry { Text = Text, Tone = Tone });
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }

        ++Revision;
        LuminaSharp.Debug.Log($"[Mercs] {Text}");
    }

    public void Announce(string Title, string Subtitle, float Duration = 4.0f)
    {
        Banner = Title;
        BannerSub = Subtitle;
        BannerTime = Duration;
        ++Revision;
    }

    public void Update(float DeltaTime)
    {
        bool bChanged = false;
        for (int Index = Entries.Count - 1; Index >= 0; --Index)
        {
            Entries[Index].Age += DeltaTime;
            if (Entries[Index].Age > EntryLife)
            {
                Entries.RemoveAt(Index);
                bChanged = true;
            }
        }

        if (BannerTime > 0.0f)
        {
            BannerTime -= DeltaTime;
            if (BannerTime <= 0.0f)
            {
                Banner = string.Empty;
                BannerSub = string.Empty;
                bChanged = true;
            }
        }

        if (bChanged)
        {
            ++Revision;
        }
    }
}
