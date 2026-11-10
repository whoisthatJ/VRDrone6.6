using Ushqyr.Garage;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>Bot names, colours and builds (P4). Bots fly a preset build of the player's size class, in their own colour.</summary>
    public static class BotRoster
    {
        public static readonly string[] Names =
        {
            "Aibek", "Dana", "Timur", "Aruzhan", "Nursultan", "Madina", "Yerlan", "Zhanna", "Bolat", "Saule", "Arman", "Aigerim",
        };

        public static readonly string[] Colors =
        {
            "color-red", "color-blue", "color-green", "color-gold", "color-purple", "color-orange", "color-white", "color-black",
        };

        /// <summary>A distinct LED / marker colour per slot.</summary>
        public static readonly Color[] Tints =
        {
            new Color(1f, 0.25f, 0.2f), new Color(0.25f, 0.55f, 1f), new Color(0.3f, 1f, 0.35f), new Color(1f, 0.8f, 0.2f),
            new Color(0.75f, 0.35f, 1f), new Color(1f, 0.55f, 0.15f), new Color(0.9f, 0.9f, 0.95f), new Color(0.2f, 0.95f, 0.95f),
        };

        /// <summary>The build every line of a size class is baked for.</summary>
        public static QuadBuild ReferenceBuild(SizeClass size) => size switch
        {
            SizeClass.Whoop => QuadPresets.Whoop65,
            SizeClass.TwoFive => QuadPresets.Whoop65,
            SizeClass.Three => QuadPresets.Toothpick3,
            SizeClass.Seven => QuadPresets.LongRange7,
            _ => QuadPresets.Race5,
        };

        /// <summary>Bot <paramref name="slot"/>'s build: the reference build in the slot's colour.</summary>
        public static QuadBuild BuildFor(SizeClass size, int slot)
        {
            var b = ReferenceBuild(size).Clone();
            b.Id = "bot-" + slot;
            b.Name = NameFor(slot, 0);
            b.ColorId = Colors[slot % Colors.Length];
            return b;
        }

        /// <summary>A name per slot; <paramref name="seed"/> shuffles which names a race gets.</summary>
        // Long arithmetic: round seeds go up to int.MaxValue, and seed * 5 would overflow to a negative index.
        public static string NameFor(int slot, int seed) => Names[(int)(((long)slot + (long)seed * 5) % Names.Length + Names.Length) % Names.Length];
    }
}
