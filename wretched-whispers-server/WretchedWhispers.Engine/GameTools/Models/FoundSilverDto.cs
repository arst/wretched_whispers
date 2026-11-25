using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

[Description("What the find held, as the dice rolled it. It is ALREADY in the purse: tell this sum, no other.")]
public sealed record FoundSilverDto(int SilverFound, int SilverNow);
