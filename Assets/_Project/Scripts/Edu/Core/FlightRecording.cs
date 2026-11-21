using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>One moment of a flight: where the drone was and what the pilot's sticks were doing. Y is up, metres.</summary>
    public struct FlightSample
    {
        public float T;
        public float X, Y, Z;
        public float Vx, Vy, Vz;
        public float Qx, Qy, Qz, Qw;
        /// <summary>
        /// Sticks, throttle 0..1 and the others -1..1: as the pilot held them, or, in a flight with nobody at the
        /// sticks, the commands the flight controller was given (the note "sticks" says which: "pilot" or "commands").
        /// </summary>
        public float Throttle, Yaw, Pitch, Roll;
        public byte Mode;
        public bool Armed;
        /// <summary>Battery left, 0..1.</summary>
        public float Battery;

        public float Speed => (float)Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);

        /// <summary>Heading in degrees, 0 = +Z (forward), growing clockwise seen from above.</summary>
        public float Heading
        {
            get
            {
                // The forward axis (0, 0, 1) turned by the orientation, flattened to the ground.
                float fx = 2f * (Qx * Qz + Qw * Qy), fz = 1f - 2f * (Qx * Qx + Qy * Qy);
                double deg = Math.Atan2(fx, fz) * 180.0 / Math.PI;
                return (float)(deg < 0 ? deg + 360.0 : deg);
            }
        }
    }

    /// <summary>Something that happened at a moment of the flight: "gate", "crash", "contact", "breach", "photo", …</summary>
    public struct FlightEvent
    {
        public float T;
        public string Type;
        public string Data;
    }

    /// <summary>
    /// A recorded flight (EDU_PLAN F2): samples at a fixed rate, events, and notes about what was flown. Scores are
    /// computed from this and nothing else, so a grade can be checked again later. The file form (`.grreplay`) is
    /// compressed and carries a hash of its content.
    /// </summary>
    public sealed class FlightRecording
    {
        public const int FileVersion = 1;
        private static readonly byte[] Magic = { (byte)'G', (byte)'R', (byte)'R', (byte)'P' };

        public float Rate = 100f;
        public readonly Dictionary<string, string> Notes = new Dictionary<string, string>();
        public readonly List<FlightSample> Samples = new List<FlightSample>();
        public readonly List<FlightEvent> Events = new List<FlightEvent>();

        public float Duration => Samples.Count > 0 ? Samples[Samples.Count - 1].T - Samples[0].T : 0f;

        public void Event(float t, string type, string data = "") => Events.Add(new FlightEvent { T = t, Type = type, Data = data ?? "" });

        /// <summary>Events whose data begins with a place "x;y;z": they move with the flight when it is put into another frame.</summary>
        private static readonly string[] PlacedEvents = { "photo", "drop", "payload_landed" };

        /// <summary>The frame this flight was flown in (the note "frame"), or the world itself when it has none.</summary>
        public FlightFrame Frame => Notes.TryGetValue("frame", out string text) && FlightFrame.TryParse(text, out FlightFrame f) ? f : FlightFrame.Identity;

        /// <summary>
        /// The same flight seen from its activity's frame: positions, speeds, attitudes and the places of events are
        /// those the activity's own numbers talk about. A flight with no frame is returned as it is.
        /// </summary>
        public FlightRecording InOwnFrame()
        {
            FlightFrame frame = Frame;
            return frame.IsIdentity ? this : Moved(frame, false);
        }

        /// <summary>
        /// This flight (in its own frame's numbers or the world's) placed into the world at <paramref name="frame"/>:
        /// used to show a reference flight in a scene other than the one it was flown in.
        /// </summary>
        public FlightRecording PlacedAt(FlightFrame frame)
        {
            FlightRecording local = InOwnFrame();
            return frame.IsIdentity ? local : local.Moved(frame, true);
        }

        private FlightRecording Moved(FlightFrame frame, bool toWorld)
        {
            var moved = new FlightRecording { Rate = Rate };
            foreach (KeyValuePair<string, string> note in Notes)
                if (note.Key != "frame") moved.Notes[note.Key] = note.Value;
            if (toWorld) moved.Notes["frame"] = frame.ToString();
            // The attitude turns about the upright axis by the frame's heading.
            double half = (toWorld ? frame.HeadingDeg : -frame.HeadingDeg) * Math.PI / 360.0;
            float ry = (float)Math.Sin(half), rw = (float)Math.Cos(half);
            moved.Samples.Capacity = Samples.Count;
            foreach (FlightSample s in Samples)
            {
                FlightSample m = s;
                if (toWorld) frame.ToWorld(s.X, s.Y, s.Z, out m.X, out m.Y, out m.Z);
                else frame.ToLocal(s.X, s.Y, s.Z, out m.X, out m.Y, out m.Z);
                frame.Turn(s.Vx, s.Vz, toWorld, out m.Vx, out m.Vz);
                // (0, ry, 0, rw) × q
                m.Qx = rw * s.Qx + ry * s.Qz;
                m.Qy = rw * s.Qy + ry * s.Qw;
                m.Qz = rw * s.Qz - ry * s.Qx;
                m.Qw = rw * s.Qw - ry * s.Qy;
                moved.Samples.Add(m);
            }
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (FlightEvent e in Events)
            {
                FlightEvent m = e;
                string[] parts = (e.Data ?? "").Split(';');
                if (Array.IndexOf(PlacedEvents, e.Type) >= 0 && parts.Length >= 3
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out float x)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out float y)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out float z))
                {
                    if (toWorld) frame.ToWorld(x, y, z, out x, out y, out z); else frame.ToLocal(x, y, z, out x, out y, out z);
                    parts[0] = x.ToString("0.00", inv);
                    parts[1] = y.ToString("0.00", inv);
                    parts[2] = z.ToString("0.00", inv);
                    // A heading right after the place (a photo's) turns with the frame too.
                    if (e.Type == "photo" && parts.Length >= 4 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, inv, out float heading))
                    {
                        heading += toWorld ? frame.HeadingDeg : -frame.HeadingDeg;
                        heading = ((heading % 360f) + 360f) % 360f;
                        parts[3] = heading.ToString("0.0", inv);
                    }
                    m.Data = string.Join(";", parts);
                }
                moved.Events.Add(m);
            }
            return moved;
        }

        public void Save(string path)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write)) Save(file);
        }

        public void Save(Stream output)
        {
            byte[] body = Body();
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(body);
            output.Write(Magic, 0, 4);
            output.WriteByte(FileVersion);
            output.WriteByte(0);
            output.Write(hash, 0, 32);
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(body, 0, body.Length);
        }

        /// <summary>The hash stored in the file: the identity of this flight.</summary>
        public string Hash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(Body())) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private byte[] Body()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Rate);
                var keys = new List<string>(Notes.Keys);
                keys.Sort(string.CompareOrdinal);
                w.Write(keys.Count);
                foreach (string k in keys)
                {
                    w.Write(k);
                    w.Write(Notes[k] ?? "");
                }
                w.Write(Samples.Count);
                foreach (FlightSample s in Samples)
                {
                    w.Write(s.T); w.Write(s.X); w.Write(s.Y); w.Write(s.Z);
                    w.Write(s.Vx); w.Write(s.Vy); w.Write(s.Vz);
                    w.Write(s.Qx); w.Write(s.Qy); w.Write(s.Qz); w.Write(s.Qw);
                    w.Write(s.Throttle); w.Write(s.Yaw); w.Write(s.Pitch); w.Write(s.Roll);
                    w.Write(s.Mode); w.Write(s.Armed); w.Write(s.Battery);
                }
                w.Write(Events.Count);
                foreach (FlightEvent e in Events)
                {
                    w.Write(e.T);
                    w.Write(e.Type ?? "");
                    w.Write(e.Data ?? "");
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static FlightRecording Load(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read)) return Load(file);
        }

        /// <summary>Reads a recording; a damaged, cut or edited file is refused with a plain reason.</summary>
        public static FlightRecording Load(Stream input)
        {
            var head = new byte[38];
            if (ReadAll(input, head, 38) != 38 || head[0] != Magic[0] || head[1] != Magic[1] || head[2] != Magic[2] || head[3] != Magic[3])
                throw new InvalidDataException("this is not a flight recording");
            if (head[4] > FileVersion)
                throw new InvalidDataException("the recording was made by a newer version of the program (format " + head[4] + ")");
            byte[] body;
            try
            {
                using (var gzip = new GZipStream(input, CompressionMode.Decompress, true))
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[65536];
                    int n;
                    while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, n);
                        if (ms.Length > 512L * 1024 * 1024) throw new OverflowException();
                    }
                    body = ms.ToArray();
                }
            }
            catch (OverflowException) { throw new InvalidDataException("the recording is too large"); }
            catch (Exception) { throw new InvalidDataException("the recording is damaged (it cannot be unpacked)"); }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(body);
                for (int i = 0; i < 32; i++)
                    if (hash[i] != head[6 + i]) throw new InvalidDataException("the recording is damaged or was edited (its hash does not match)");
            }
            var rec = new FlightRecording();
            try
            {
                using (var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8))
                {
                    rec.Rate = r.ReadSingle();
                    int notes = r.ReadInt32();
                    for (int i = 0; i < notes; i++)
                    {
                        string k = r.ReadString();
                        rec.Notes[k] = r.ReadString();
                    }
                    int samples = r.ReadInt32();
                    if (samples < 0 || (long)samples * 66 > body.Length) throw new InvalidDataException("bad sample count");
                    rec.Samples.Capacity = samples;
                    for (int i = 0; i < samples; i++)
                    {
                        var s = new FlightSample
                        {
                            T = r.ReadSingle(), X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(),
                            Vx = r.ReadSingle(), Vy = r.ReadSingle(), Vz = r.ReadSingle(),
                            Qx = r.ReadSingle(), Qy = r.ReadSingle(), Qz = r.ReadSingle(), Qw = r.ReadSingle(),
                            Throttle = r.ReadSingle(), Yaw = r.ReadSingle(), Pitch = r.ReadSingle(), Roll = r.ReadSingle(),
                            Mode = r.ReadByte(), Armed = r.ReadBoolean(), Battery = r.ReadSingle(),
                        };
                        rec.Samples.Add(s);
                    }
                    int events = r.ReadInt32();
                    if (events < 0 || events > body.Length) throw new InvalidDataException("bad event count");
                    for (int i = 0; i < events; i++)
                        rec.Events.Add(new FlightEvent { T = r.ReadSingle(), Type = r.ReadString(), Data = r.ReadString() });
                }
            }
            catch (EndOfStreamException) { throw new InvalidDataException("the recording is cut short"); }
            return rec;
        }

        private static int ReadAll(Stream s, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
    }
}
