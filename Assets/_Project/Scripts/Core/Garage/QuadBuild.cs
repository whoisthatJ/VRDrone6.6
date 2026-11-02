using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GateRush.Garage
{
    /// <summary>A quad as parts (saved as JSON). Visual parts (style, colour) don't change the physics.</summary>
    [Serializable]
    public sealed class QuadBuild
    {
        public string Id = "";
        public string Name = "My quad";
        public string FrameId;
        public string MotorId;
        public string PropId;
        public string BatteryId;
        public List<string> ExtraIds = new List<string>();
        public string StyleId = "style-carbon";
        public string ColorId = "color-orange";
        /// <summary>Per-axis drag areas (true) or one averaged drag for every direction (false).</summary>
        public bool AdvancedDrag = true;

        public FrameDef Frame => PartCatalog.Find<FrameDef>(FrameId);
        public MotorDef Motor => PartCatalog.Find<MotorDef>(MotorId);
        public PropDef Prop => PartCatalog.Find<PropDef>(PropId);
        public BatteryDef Battery => PartCatalog.Find<BatteryDef>(BatteryId);
        public IEnumerable<ExtraDef> Extras => ExtraIds.Select(PartCatalog.Find<ExtraDef>).Where(e => e != null);
        public StyleDef Style => PartCatalog.Find<StyleDef>(StyleId) ?? PartCatalog.Styles[0];
        public ColorDef Color => PartCatalog.Find<ColorDef>(ColorId) ?? PartCatalog.Colors[0];
        public SizeClass Size => Frame != null ? Frame.Size : SizeClass.Five;

        public string PartId(PartSlot slot) => slot switch
        {
            PartSlot.Frame => FrameId,
            PartSlot.Motor => MotorId,
            PartSlot.Prop => PropId,
            PartSlot.Battery => BatteryId,
            PartSlot.Style => StyleId,
            PartSlot.Color => ColorId,
            _ => null,
        };

        /// <summary>A copy with one part swapped (extras toggle). Changing the frame to another size refits the rest.</summary>
        public QuadBuild With(PartDef part)
        {
            var b = Clone();
            switch (part.Slot)
            {
                case PartSlot.Frame:
                    b.FrameId = part.Id;
                    b.FitTo(((FrameDef)part).Size);
                    break;
                case PartSlot.Motor: b.MotorId = part.Id; break;
                case PartSlot.Prop: b.PropId = part.Id; break;
                case PartSlot.Battery: b.BatteryId = part.Id; break;
                case PartSlot.Style: b.StyleId = part.Id; break;
                case PartSlot.Color: b.ColorId = part.Id; break;
                case PartSlot.Extra:
                    if (!b.ExtraIds.Remove(part.Id)) b.ExtraIds.Add(part.Id);
                    break;
            }
            return b;
        }

        /// <summary>Replaces parts that don't fit <paramref name="size"/> with the first ones that do (the preset's, if any).</summary>
        public void FitTo(SizeClass size)
        {
            var preset = QuadPresets.All.FirstOrDefault(p => p.Size == size);
            string Pick<T>(string current, List<T> list, Func<QuadBuild, string> fromPreset) where T : PartDef
            {
                var c = PartCatalog.Find<T>(current);
                if (c != null && c.Fits(size)) return current;
                if (preset != null) return fromPreset(preset);
                return list.First(p => p.Fits(size)).Id;
            }
            MotorId = Pick(MotorId, PartCatalog.Motors, p => p.MotorId);
            PropId = Pick(PropId, PartCatalog.Props, p => p.PropId);
            BatteryId = Pick(BatteryId, PartCatalog.Batteries, p => p.BatteryId);
            ExtraIds.RemoveAll(id => !(PartCatalog.Find(id)?.Fits(size) ?? false));
        }

        public QuadBuild Clone() => JsonUtility.FromJson<QuadBuild>(JsonUtility.ToJson(this));
        public string ToJson() => JsonUtility.ToJson(this, true);
        public static QuadBuild FromJson(string json) => JsonUtility.FromJson<QuadBuild>(json);
    }

    /// <summary>The six shipped builds (plan §4.3).</summary>
    public static class QuadPresets
    {
        public static QuadBuild Race5 => new QuadBuild
        {
            Id = "preset-race5", Name = "5\" Race", FrameId = "frame-grx-race", MotorId = "motor-raptor2207-2400", PropId = "prop-5143-3",
            BatteryId = "lipo-4s-1300", ColorId = "color-orange", StyleId = "style-carbon",
        };

        public static QuadBuild Freestyle5 => new QuadBuild
        {
            Id = "preset-freestyle5", Name = "5\" Freestyle", FrameId = "frame-volt-free", MotorId = "motor-raptor2207-1950", PropId = "prop-5045-3",
            BatteryId = "lipo-6s-1100", ExtraIds = new List<string> { "extra-actioncam" }, ColorId = "color-gold", StyleId = "style-gold",
        };

        public static QuadBuild Toothpick3 => new QuadBuild
        {
            Id = "preset-toothpick3", Name = "3\" Toothpick", FrameId = "frame-pick3", MotorId = "motor-hornet1404", PropId = "prop-3030-3",
            BatteryId = "lipo-4s-650", ColorId = "color-green", StyleId = "style-carbon",
        };

        public static QuadBuild Cinewhoop35 => new QuadBuild
        {
            Id = "preset-cinewhoop35", Name = "3.5\" Cinewhoop", FrameId = "frame-cine35", MotorId = "motor-hornet1804", PropId = "prop-3525-5",
            BatteryId = "lipo-6s-850", ExtraIds = new List<string> { "extra-hdcam" }, ColorId = "color-blue", StyleId = "style-stealth",
        };

        public static QuadBuild Whoop65 => new QuadBuild
        {
            Id = "preset-whoop65", Name = "65 mm Whoop", FrameId = "frame-kestrel65", MotorId = "motor-gnat0702", PropId = "prop-31mm-4",
            BatteryId = "lipo-1s-300", ColorId = "color-purple", StyleId = "style-stealth",
        };

        public static QuadBuild LongRange7 => new QuadBuild
        {
            Id = "preset-lr7", Name = "7\" Long Range", FrameId = "frame-atlas7", MotorId = "motor-atlas2806", PropId = "prop-7035-3",
            BatteryId = "liion-6s-3000", ExtraIds = new List<string> { "extra-antenna" }, ColorId = "color-red", StyleId = "style-carbon",
        };

        public static IReadOnlyList<QuadBuild> All => new[] { Race5, Freestyle5, Toothpick3, Cinewhoop35, Whoop65, LongRange7 };

        public static QuadBuild Find(string id) => All.FirstOrDefault(b => b.Id == id);
    }

    /// <summary>Saved builds: one JSON file each under the user folder (persistentDataPath/builds).</summary>
    public sealed class BuildLibrary
    {
        public const string IdPrefix = "build:";
        private readonly string _dir;

        public BuildLibrary(string dir) => _dir = dir;

        public static BuildLibrary Default => new BuildLibrary(Path.Combine(Application.persistentDataPath, "builds"));

        public static bool IsBuildId(string quadId) => !string.IsNullOrEmpty(quadId) && quadId.StartsWith(IdPrefix, StringComparison.Ordinal);

        public string PathFor(string id) => Path.Combine(_dir, id + ".json");

        public void Save(QuadBuild build)
        {
            if (string.IsNullOrEmpty(build.Id) || build.Id.StartsWith("preset-", StringComparison.Ordinal))
                build.Id = "b" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(PathFor(build.Id), build.ToJson());
        }

        public QuadBuild Load(string id)
        {
            string p = PathFor(id);
            return File.Exists(p) ? QuadBuild.FromJson(File.ReadAllText(p)) : null;
        }

        public List<QuadBuild> LoadAll()
        {
            if (!Directory.Exists(_dir)) return new List<QuadBuild>();
            return Directory.GetFiles(_dir, "*.json")
                .Select(f =>
                {
                    try { return QuadBuild.FromJson(File.ReadAllText(f)); }
                    catch (ArgumentException) { return null; }
                })
                .Where(b => b != null && b.Frame != null)
                .OrderByDescending(b => b.Id)
                .ToList();
        }

        /// <summary>Resolves a settings QuadId ("build:&lt;id&gt;" or a preset id) to a build, or null.</summary>
        public QuadBuild Resolve(string quadId)
        {
            if (string.IsNullOrEmpty(quadId)) return null;
            if (IsBuildId(quadId))
            {
                string id = quadId.Substring(IdPrefix.Length);
                return QuadPresets.Find(id) ?? Load(id);
            }
            return null;
        }
    }
}
