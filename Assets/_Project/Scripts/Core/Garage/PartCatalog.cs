using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GateRush.Garage
{
    public enum PartSlot { Frame = 0, Motor = 1, Prop = 2, Battery = 3, Extra = 4, Style = 5, Color = 6 }

    /// <summary>Quad size classes (the garage's size filter).</summary>
    public enum SizeClass { Whoop = 0, TwoFive = 1, Three = 2, Five = 3, Seven = 4 }

    public enum FrameGeometry { TrueX, StretchX, Deadcat, Ducted }

    public enum ExtraKind { ActionCam, LedKit, LongAntenna, HdCamera }

    public enum FrameFinish { Carbon, GoldEdge, Tiger, Stealth }

    /// <summary>Base of every part: identity, which sizes it fits, mass and the text on its card.</summary>
    [Serializable]
    public class PartDef
    {
        public string Id;
        public string Name;
        /// <summary>Our fictional brands only.</summary>
        public string Brand;
        public string Description;
        public PartSlot Slot;
        public SizeClass[] Sizes = new SizeClass[0];
        public float MassG;

        public bool Fits(SizeClass s) => Sizes.Length == 0 || Array.IndexOf(Sizes, s) >= 0;
        /// <summary>One-line key stat for the part card.</summary>
        public virtual string KeyStat => MassG > 0f ? $"{MassG:0.#} g" : "";
    }

    [Serializable]
    public sealed class FrameDef : PartDef
    {
        public FrameGeometry Geometry;
        public SizeClass Size;
        /// <summary>Motor-to-motor diagonal (mm).</summary>
        public float WheelbaseMm;
        public float ArmWidthMm;
        public float PlateMm;
        public float MaxPropIn;
        /// <summary>Drag area (Cd·A, cm²) seen from the front, top and side, with electronics but no battery.</summary>
        public float CdAFront, CdATop, CdASide;
        public float BodyLengthMm, BodyWidthMm, StackMm;
        /// <summary>Front/rear motor spacing over left/right spacing (1 = true X; &gt; 1 = stretched).</summary>
        public float Stretch = 1f;

        public override string KeyStat => $"{WheelbaseMm:0} mm  {MassG:0} g";
    }

    [Serializable]
    public sealed class MotorDef : PartDef
    {
        public float StatorD, StatorH;
        public float Kv;
        /// <summary>Winding resistance (Ω) and no-load current (A).</summary>
        public float Rm, I0;
        public float MaxCurrent;

        public string StatorCode => $"{StatorD:00}{StatorH:00}";
        public override string KeyStat => $"{Kv:0} KV  {MassG:0.#} g";
    }

    [Serializable]
    public sealed class PropDef : PartDef
    {
        public float DiameterIn, PitchIn;
        public int Blades = 3;
        /// <summary>Static thrust and torque coefficients (n in rev/s, D in m).</summary>
        public float Ct, Cq;

        public override string KeyStat => $"{DiameterIn:0.##}x{PitchIn:0.##}x{Blades}";
    }

    [Serializable]
    public sealed class BatteryDef : PartDef
    {
        public int Cells;
        public float CapacityMah;
        /// <summary>Pack internal resistance (mΩ).</summary>
        public float ResistanceMilliOhm;
        public float CRating;

        public override string KeyStat => $"{Cells}S {CapacityMah:0} mAh";
    }

    [Serializable]
    public sealed class ExtraDef : PartDef
    {
        public ExtraKind Kind;
        public float CdAFront, CdATop;

        public override string KeyStat => MassG > 0f ? $"+{MassG:0} g" : "visual";
    }

    [Serializable]
    public sealed class StyleDef : PartDef
    {
        public FrameFinish Finish;
        public override string KeyStat => "visual";
    }

    [Serializable]
    public sealed class ColorDef : PartDef
    {
        public Color Accent = Color.white;
        public override string KeyStat => "visual";
    }

    /// <summary>
    /// Every part the garage offers (T7/T8). Values are realistic for the class (stator, KV, winding resistance,
    /// prop coefficients, pack resistance) so <see cref="BuildComposer"/> derives thrust, speed and flight time from
    /// physics. Brands are fictional.
    /// </summary>
    public static class PartCatalog
    {
        private static readonly SizeClass[] W = { SizeClass.Whoop };
        private static readonly SizeClass[] S25 = { SizeClass.TwoFive };
        private static readonly SizeClass[] S3 = { SizeClass.Three };
        private static readonly SizeClass[] S5 = { SizeClass.Five };
        private static readonly SizeClass[] S7 = { SizeClass.Seven };
        private static readonly SizeClass[] S57 = { SizeClass.Five, SizeClass.Seven };
        private static readonly SizeClass[] S253 = { SizeClass.TwoFive, SizeClass.Three };

        public static readonly List<FrameDef> Frames = new List<FrameDef>
        {
            Frame("frame-kestrel65", "Kestrel 65", "Kestrel", "65 mm ducted whoop. Bumps off walls, flies indoors.", SizeClass.Whoop, FrameGeometry.Ducted,
                65f, 4f, 1f, 1.3f, 4.2f, 6.5f, 4.2f, 5.8f, 30f, 30f, 14f),
            Frame("frame-kestrel25", "Kestrel 2.5 Cine", "Kestrel", "2.5\" ducted cinewhoop: soft, stable, slow.", SizeClass.TwoFive, FrameGeometry.Ducted,
                112f, 6f, 2f, 2.5f, 12f, 16f, 12f, 31f, 44f, 44f, 18f),
            Frame("frame-pick3", "Needle 3 Toothpick", "Halcyon", "Featherweight 3\" toothpick. Light, nimble, fragile.", SizeClass.Three, FrameGeometry.TrueX,
                128f, 6f, 2f, 3f, 10f, 15f, 10f, 22f, 40f, 34f, 16f),
            Frame("frame-cine35", "Halo 3.5 Cinewhoop", "Halcyon", "3.5\" ducted cine rig on 6S: lifts a small HD camera.", SizeClass.Three, FrameGeometry.Ducted,
                151f, 8f, 3f, 3.5f, 21f, 30f, 21f, 72f, 58f, 58f, 22f),
            Frame("frame-grx-race", "GRX Race 5", "GRX", "Race-proven true-X. Low drag, slim arms, light.", SizeClass.Five, FrameGeometry.TrueX,
                220f, 13f, 5f, 5.1f, 24f, 40f, 24f, 105f, 88f, 36f, 26f),
            Frame("frame-grx-slim", "GRX Slim 5", "GRX", "Ultralight race frame. Thinner arms: fast, but bends.", SizeClass.Five, FrameGeometry.TrueX,
                215f, 11f, 4f, 5.1f, 21f, 37f, 21f, 88f, 84f, 32f, 24f),
            Frame("frame-volt-free", "Volt Freestyle 5", "Volt", "Tough stretched-X for freestyle. Thick 6 mm arms.", SizeClass.Five, FrameGeometry.StretchX,
                226f, 16f, 6f, 5.1f, 28f, 46f, 28f, 128f, 96f, 40f, 30f, 1.12f),
            Frame("frame-volt-deadcat", "Volt Deadcat 5", "Volt", "Deadcat geometry: props out of the camera view.", SizeClass.Five, FrameGeometry.Deadcat,
                230f, 15f, 5f, 5.1f, 29f, 47f, 29f, 125f, 100f, 40f, 30f, 1.08f),
            Frame("frame-atlas7", "Atlas 7 Long Range", "Atlas", "7\" long-range cruiser. Efficient, stable, far.", SizeClass.Seven, FrameGeometry.StretchX,
                315f, 16f, 5f, 7.5f, 36f, 64f, 36f, 165f, 120f, 44f, 34f, 1.1f),
        };

        public static readonly List<MotorDef> Motors = new List<MotorDef>
        {
            Motor("motor-gnat0702", "Gnat 0702", "Gnat", "Whoop motor for 1S.", W, 7f, 2f, 25000f, 0.55f, 0.25f, 5f, 2.0f),
            Motor("motor-gnat1103", "Gnat 1103", "Gnat", "Tiny 2S motor for 2.5\" ducts.", S25, 11f, 3f, 11000f, 0.38f, 0.3f, 8f, 3.4f),
            Motor("motor-hornet1404", "Hornet 1404 3800KV", "Hornet", "3\" toothpick motor for 4S.", S3, 14f, 4f, 3800f, 0.22f, 0.4f, 14f, 8.7f),
            Motor("motor-hornet1804", "Hornet 1804 2450KV", "Hornet", "Torquey 6S motor for 3.5\" ducts.", S3, 18f, 4f, 2450f, 0.14f, 0.5f, 20f, 15f),
            Motor("motor-raptor2207-2400", "Raptor 2207 2400KV", "Raptor", "4S race motor: punchy and fast.", S5, 22f, 7f, 2400f, 0.055f, 1.2f, 45f, 33f),
            Motor("motor-raptor2207-1950", "Raptor 2207 1950KV", "Raptor", "6S freestyle motor: torque and top end.", S5, 22f, 7f, 1950f, 0.06f, 1.0f, 40f, 33f),
            Motor("motor-swift2306", "Swift 2306 2450KV", "Swift", "Lighter 4S motor, smooth and efficient.", S5, 23f, 6f, 2450f, 0.065f, 1.0f, 40f, 30f),
            Motor("motor-atlas2806", "Atlas 2806.5 1300KV", "Atlas", "Long-range 6S motor for 7\" props.", S7, 28f, 6.5f, 1300f, 0.085f, 0.8f, 38f, 46f),
        };

        public static readonly List<PropDef> Props = new List<PropDef>
        {
            Prop("prop-31mm-4", "Gnat 31 mm quad-blade", "Gnat", "Whoop props.", W, 1.22f, 0.9f, 4, 0.17f, 0.012f, 0.3f),
            Prop("prop-2525-3", "Kestrel 2.5x2.5x3", "Kestrel", "Ducted tri-blade.", S25, 2.5f, 2.5f, 3, 0.17f, 0.011f, 1.1f),
            Prop("prop-3030-3", "Halcyon 3x3x3", "Halcyon", "Toothpick tri-blade.", S3, 3f, 3f, 3, 0.18f, 0.012f, 1.6f),
            Prop("prop-3525-5", "Halcyon 3.5x2.5x5", "Halcyon", "Five blades for ducts: thrust in a small disc.", S3, 3.5f, 2.5f, 5, 0.20f, 0.012f, 2.6f),
            Prop("prop-5143-3", "GRX 5.1x4.3x3", "GRX", "Race tri-blade: grip and top speed.", S5, 5.1f, 4.3f, 3, 0.20f, 0.0125f, 4.4f),
            Prop("prop-5045-3", "Volt 5x4.5x3", "Volt", "Freestyle tri-blade, durable.", S5, 5f, 4.5f, 3, 0.20f, 0.0135f, 4.6f),
            Prop("prop-5136-3", "GRX 5.1x3.6x3", "GRX", "Low pitch: efficient and smooth, less top speed.", S5, 5.1f, 3.6f, 3, 0.18f, 0.0105f, 4.0f),
            Prop("prop-7035-3", "Atlas 7x3.5x3", "Atlas", "Long-range tri-blade.", S7, 7f, 3.5f, 3, 0.15f, 0.0095f, 8.2f),
        };

        public static readonly List<BatteryDef> Batteries = new List<BatteryDef>
        {
            Battery("lipo-1s-300", "Spark 1S 300 mAh", W, 1, 300f, 60f, 60f, 8.5f),
            Battery("lipo-2s-450", "Spark 2S 450 mAh", S25, 2, 450f, 50f, 80f, 25f),
            Battery("lipo-4s-650", "Spark 4S 650 mAh", S3, 4, 650f, 40f, 95f, 78f),
            Battery("lipo-6s-850", "Spark 6S 850 mAh", S3, 6, 850f, 35f, 100f, 140f),
            Battery("lipo-4s-1300", "Volt 4S 1300 mAh", S5, 4, 1300f, 18f, 120f, 160f),
            Battery("lipo-4s-1500", "Volt 4S 1500 mAh", S5, 4, 1500f, 17f, 120f, 185f),
            Battery("lipo-6s-1100", "Volt 6S 1100 mAh", S5, 6, 1100f, 25f, 120f, 190f),
            Battery("lipo-6s-1300", "Volt 6S 1300 mAh", S57, 6, 1300f, 22f, 120f, 215f),
            Battery("liion-6s-3000", "Atlas 6S 3000 mAh Li-ion", S7, 6, 3000f, 45f, 15f, 300f),
        };

        public static readonly List<ExtraDef> Extras = new List<ExtraDef>
        {
            new ExtraDef { Id = "extra-actioncam", Name = "Action camera", Brand = "GateRush", Description = "Full-size action camera on the top plate: cinematic footage, more weight and drag.",
                Slot = PartSlot.Extra, Sizes = S57, MassG = 121f, Kind = ExtraKind.ActionCam, CdAFront = 9f, CdATop = 12f },
            new ExtraDef { Id = "extra-hdcam", Name = "HD camera", Brand = "GateRush", Description = "Small HD camera for 3\" rigs.",
                Slot = PartSlot.Extra, Sizes = S253, MassG = 28f, Kind = ExtraKind.HdCamera, CdAFront = 3f, CdATop = 3f },
            new ExtraDef { Id = "extra-leds", Name = "LED kit", Brand = "GateRush", Description = "Bright LED strips along the frame. Night racing look.",
                Slot = PartSlot.Extra, MassG = 6f, Kind = ExtraKind.LedKit },
            new ExtraDef { Id = "extra-antenna", Name = "Long-range antenna", Brand = "GateRush", Description = "Tall stubby-free antenna: better range, a little drag.",
                Slot = PartSlot.Extra, Sizes = S57, MassG = 8f, Kind = ExtraKind.LongAntenna, CdAFront = 0.6f },
        };

        public static readonly List<StyleDef> Styles = new List<StyleDef>
        {
            new StyleDef { Id = "style-carbon", Name = "Raw carbon", Brand = "GateRush", Description = "Plain 3K carbon weave.", Slot = PartSlot.Style, Finish = FrameFinish.Carbon },
            new StyleDef { Id = "style-gold", Name = "Gold edge", Brand = "GateRush", Description = "Carbon with gold-anodised plate edges and standoffs.", Slot = PartSlot.Style, Finish = FrameFinish.GoldEdge },
            new StyleDef { Id = "style-tiger", Name = "Tiger wrap", Brand = "GateRush", Description = "Printed tiger-stripe skin on the plates.", Slot = PartSlot.Style, Finish = FrameFinish.Tiger },
            new StyleDef { Id = "style-stealth", Name = "Stealth", Brand = "GateRush", Description = "Matte black everything.", Slot = PartSlot.Style, Finish = FrameFinish.Stealth },
        };

        public static readonly List<ColorDef> Colors = new List<ColorDef>
        {
            Col("color-orange", "Rush orange", new Color(1f, 0.45f, 0.05f)),
            Col("color-gold", "Gold", new Color(0.95f, 0.75f, 0.15f)),
            Col("color-red", "Signal red", new Color(0.9f, 0.08f, 0.06f)),
            Col("color-green", "Gate green", new Color(0.15f, 0.8f, 0.3f)),
            Col("color-blue", "Electric blue", new Color(0.1f, 0.45f, 1f)),
            Col("color-purple", "Purple", new Color(0.55f, 0.15f, 0.85f)),
            Col("color-white", "White", new Color(0.92f, 0.92f, 0.9f)),
            Col("color-black", "Black", new Color(0.08f, 0.08f, 0.09f)),
        };

        public static IEnumerable<PartDef> All =>
            Frames.Cast<PartDef>().Concat(Motors).Concat(Props).Concat(Batteries).Concat(Extras).Concat(Styles).Concat(Colors);

        public static IEnumerable<PartDef> InSlot(PartSlot slot) => All.Where(p => p.Slot == slot);

        public static PartDef Find(string id) => All.FirstOrDefault(p => p.Id == id);
        public static T Find<T>(string id) where T : PartDef => Find(id) as T;

        public static string SizeLabel(SizeClass s) => s switch
        {
            SizeClass.Whoop => "Whoop",
            SizeClass.TwoFive => "2.5 inch",
            SizeClass.Three => "3 inch",
            SizeClass.Five => "5 inch",
            _ => "7 inch",
        };

        // ---------- helpers ----------

        private static FrameDef Frame(string id, string name, string brand, string desc, SizeClass size, FrameGeometry g, float wheelbase, float armW, float plate,
            float maxProp, float cdaFront, float cdaTop, float cdaSide, float mass, float bodyL, float bodyW, float stack, float stretch = 1f) =>
            new FrameDef
            {
                Id = id, Name = name, Brand = brand, Description = desc, Slot = PartSlot.Frame, Sizes = new[] { size }, Size = size, Geometry = g,
                WheelbaseMm = wheelbase, ArmWidthMm = armW, PlateMm = plate, MaxPropIn = maxProp, CdAFront = cdaFront, CdATop = cdaTop, CdASide = cdaSide,
                MassG = mass, BodyLengthMm = bodyL, BodyWidthMm = bodyW, StackMm = stack, Stretch = stretch,
            };

        private static MotorDef Motor(string id, string name, string brand, string desc, SizeClass[] sizes, float d, float h, float kv, float rm, float i0, float maxI, float mass) =>
            new MotorDef { Id = id, Name = name, Brand = brand, Description = desc, Slot = PartSlot.Motor, Sizes = sizes, StatorD = d, StatorH = h, Kv = kv, Rm = rm, I0 = i0, MaxCurrent = maxI, MassG = mass };

        private static PropDef Prop(string id, string name, string brand, string desc, SizeClass[] sizes, float dia, float pitch, int blades, float ct, float cq, float mass) =>
            new PropDef { Id = id, Name = name, Brand = brand, Description = desc, Slot = PartSlot.Prop, Sizes = sizes, DiameterIn = dia, PitchIn = pitch, Blades = blades, Ct = ct, Cq = cq, MassG = mass };

        private static BatteryDef Battery(string id, string name, SizeClass[] sizes, int cells, float mah, float mOhm, float c, float mass) =>
            new BatteryDef
            {
                Id = id, Name = name, Brand = name.Split(' ')[0], Description = $"{cells}S {mah:0} mAh, {c:0}C.", Slot = PartSlot.Battery, Sizes = sizes,
                Cells = cells, CapacityMah = mah, ResistanceMilliOhm = mOhm, CRating = c, MassG = mass,
            };

        private static ColorDef Col(string id, string name, Color c) =>
            new ColorDef { Id = id, Name = name, Brand = "GateRush", Description = "Accent colour for motors, props and LEDs.", Slot = PartSlot.Color, Accent = c };
    }
}
