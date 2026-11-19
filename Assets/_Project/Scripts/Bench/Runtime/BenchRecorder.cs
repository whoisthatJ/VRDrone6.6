using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ushqyr.Flight;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>Raw RGB frames into an external ffmpeg, which writes an MP4. Frames are written in order, at a fixed rate.</summary>
    public sealed class BenchVideoPipe
    {
        private readonly Process _process;
        private readonly Stream _stdin;
        private readonly StringBuilder _errors = new StringBuilder();
        public string OutputPath { get; }
        public bool Failed { get; private set; }
        public string Errors { get { lock (_errors) return _errors.ToString(); } }

        public BenchVideoPipe(string ffmpeg, int width, int height, float fps, string outputPath, bool flipVertically)
        {
            OutputPath = outputPath;
            string rate = fps.ToString("0.###", CultureInfo.InvariantCulture);
            var info = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {rate} -i - " +
                            (flipVertically ? "-vf vflip " : "") +
                            $"-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true,
            };
            _process = Process.Start(info);
            _process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_errors) _errors.AppendLine(e.Data); };
            _process.BeginErrorReadLine();
            _stdin = _process.StandardInput.BaseStream;
        }

        public void Write(byte[] frame, int length)
        {
            if (Failed) return;
            try { _stdin.Write(frame, 0, length); }
            catch (Exception) { Failed = true; }
        }

        /// <summary>Ends the stream and waits for the file to be finished.</summary>
        public void Close()
        {
            try { _stdin.Close(); } catch (Exception) { Failed = true; }
            try
            {
                if (!_process.WaitForExit(120000)) { _process.Kill(); Failed = true; }
                else if (_process.ExitCode != 0) Failed = true;
            }
            catch (Exception) { Failed = true; }
        }

        /// <summary>Runs ffmpeg once with the given arguments in <paramref name="folder"/>; returns its error text (empty = fine).</summary>
        public static string Run(string ffmpeg, string arguments, string folder)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = ffmpeg, Arguments = arguments, WorkingDirectory = folder,
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                };
                using (Process p = Process.Start(info))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0 ? "" : (string.IsNullOrEmpty(err) ? "ffmpeg exit code " + p.ExitCode : err);
                }
            }
            catch (Exception e) { return e.Message; }
        }
    }

    /// <summary>
    /// Writes one attempt's folder: result.json, events (JSON lines and CSV), telemetry.csv (what the program received and
    /// sent), truth.csv (the true trajectory, for judges only), the track file, videos, a log, and last the checksummed,
    /// signed manifest. Everything here is written by the simulator, outside the program's reach.
    /// </summary>
    public sealed class BenchRecorder
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly AttemptResult _result;
        private readonly byte[] _key;
        private readonly string _ffmpeg;
        private readonly int _repeatWindow;
        private readonly float _fps;
        private StreamWriter _events, _eventsCsv, _telemetry, _truth, _log;
        private BenchVideoPipe _onboard, _trackside;
        private readonly StringBuilder _line = new StringBuilder(256);
        private readonly List<string> _judgeLines = new List<string>();
        private Task<AttemptResult> _finishing;

        public string Folder { get; }
        public bool Video => _onboard != null;
        public bool Trackside => _trackside != null;
        /// <summary>True while the files are being closed and the judge video is made (in the background).</summary>
        public bool Finishing => _finishing != null && !_finishing.IsCompleted;
        public bool Finished => _finishing != null && _finishing.IsCompleted;
        public AttemptResult Result => Finished ? _finishing.Result : null;

        public BenchRecorder(string root, AttemptResult info, string trackJson, int repeatWindow, byte[] key, string ffmpeg,
            int cameraWidth, int cameraHeight, float fps, int tracksideWidth, int tracksideHeight)
        {
            _result = info;
            _key = key;
            _ffmpeg = ffmpeg;
            _repeatWindow = repeatWindow;
            _fps = fps;
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Inv);
            Folder = Path.Combine(root, $"{Safe(info.team)}_{Safe(info.track_id)}_a{info.attempt:00}_{stamp}");
            for (int n = 2; Directory.Exists(Folder); n++) Folder = Path.Combine(root, $"{Safe(info.team)}_{Safe(info.track_id)}_a{info.attempt:00}_{stamp}_{n}");
            Directory.CreateDirectory(Folder);

            File.WriteAllText(Path.Combine(Folder, "track.benchtrack.json"), trackJson, new UTF8Encoding(false));
            _events = Open("events.jsonl");
            _eventsCsv = Open("events.csv");
            _eventsCsv.WriteLine("t,type,gate,result,points,reason");
            _telemetry = Open("telemetry.csv");
            _telemetry.WriteLine("tick,t,rc_ok,ch1,ch2,ch3,ch4,ch5,ch6,ch7,ch8,ch9,ch10,ch11,ch12,ch13,ch14,ch15,ch16," +
                                 "armed,failsafe,gx,gy,gz,ax,ay,az,tof,tof_valid,battery,camera_seq");
            _truth = Open("truth.csv");
            // The simulator's own frame: x east, y up, z north; rotation as a quaternion in that (left-handed) frame.
            _truth.WriteLine("tick,t,x,y_up,z,qx,qy,qz,qw,vx,vy_up,vz");
            _log = Open("sim.log");
            Log($"attempt folder created; team {info.team}, track {info.track_id} ({info.track_hash}), mode {info.mode}, seed {info.seed}");

            if (!string.IsNullOrEmpty(ffmpeg))
            {
                try
                {
                    _onboard = new BenchVideoPipe(ffmpeg, cameraWidth, cameraHeight, fps, Path.Combine(Folder, "onboard.mp4"), false);
                    if (tracksideWidth > 0)
                        _trackside = new BenchVideoPipe(ffmpeg, tracksideWidth, tracksideHeight, fps, Path.Combine(Folder, "trackside.mp4"), true);
                }
                catch (Exception e)
                {
                    Log("video off: can't start ffmpeg: " + e.Message);
                    _onboard = _trackside = null;
                }
            }
            else Log("video off: no ffmpeg");
        }

        private StreamWriter Open(string name) => new StreamWriter(Path.Combine(Folder, name), false, new UTF8Encoding(false)) { NewLine = "\n" };

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in string.IsNullOrEmpty(s) ? "unnamed" : s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        }

        public void Log(string text) => _log?.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff", Inv) + " " + text);

        private static string Json(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        /// <summary>start, end, failsafe, failsafe_end, collision.</summary>
        public void Note(double t, string type, string detail)
        {
            _events.WriteLine($"{{\"t\":{t.ToString("F3", Inv)},\"type\":{Json(type)},\"detail\":{Json(detail)}}}");
            _eventsCsv.WriteLine($"{t.ToString("F3", Inv)},{type},,,,{Leaderboard.Cell(detail, ',')}");
            if (type == "end") _judgeLines.Add($"{t.ToString("F3", Inv)}|END: {detail}");
        }

        public void Gate(in GateEvent e)
        {
            string kind = GateEventText.Kind(e.Kind), reason = GateEventText.Reason(e, _repeatWindow);
            _events.WriteLine($"{{\"t\":{e.Time.ToString("F3", Inv)},\"type\":\"gate\",\"gate\":{Json(e.GateId)},\"result\":\"{kind}\"," +
                              $"\"points\":{e.Points},\"reason\":{Json(reason)}}}");
            _eventsCsv.WriteLine($"{e.Time.ToString("F3", Inv)},gate,{Leaderboard.Cell(e.GateId, ',')},{kind},{e.Points},{Leaderboard.Cell(reason, ',')}");
            _judgeLines.Add($"{e.Time.ToString("F3", Inv)}|{e.GateId}: " + (e.Kind == GateEventKind.Scored ? $"+1  ({e.Points})" : reason));
        }

        /// <summary>One control step: what the program sent, what it was given, and where the drone really was.</summary>
        public void Step(long tick, double t, IReadOnlyList<int> rc, bool armed, bool failsafe, in ImuSample imu, float tof, bool tofValid,
            float battery, long cameraSeq, in QuadState s)
        {
            _line.Clear();
            _line.Append(tick).Append(',').Append(t.ToString("F3", Inv)).Append(',').Append(rc != null ? '1' : '0');
            for (int i = 0; i < RcLink.Channels; i++)
            {
                _line.Append(',');
                if (rc != null) _line.Append(i < rc.Count ? rc[i] : i == 2 || i == 4 || i == 5 ? RcLink.MinUs : RcLink.MidUs);
            }
            _line.Append(',').Append(armed ? '1' : '0').Append(',').Append(failsafe ? '1' : '0');
            F(imu.Gx, "F4"); F(imu.Gy, "F4"); F(imu.Gz, "F4"); F(imu.Ax, "F3"); F(imu.Ay, "F3"); F(imu.Az, "F3");
            F(tof, "F3");
            _line.Append(',').Append(tofValid ? '1' : '0');
            F(battery, "F3");
            _line.Append(',').Append(cameraSeq);
            _telemetry.WriteLine(_line.ToString());

            _line.Clear();
            _line.Append(tick).Append(',').Append(t.ToString("F3", Inv));
            F(s.Position.x, "F4"); F(s.Position.y, "F4"); F(s.Position.z, "F4");
            F(s.Orientation.x, "F5"); F(s.Orientation.y, "F5"); F(s.Orientation.z, "F5"); F(s.Orientation.w, "F5");
            F(s.Velocity.x, "F3"); F(s.Velocity.y, "F3"); F(s.Velocity.z, "F3");
            _truth.WriteLine(_line.ToString());
        }

        private void F(float v, string format) => _line.Append(',').Append(v.ToString(format, Inv));

        public void OnboardFrame(byte[] rgbTopDown, int length) => _onboard?.Write(rgbTopDown, length);
        public void TracksideFrame(byte[] rgbBottomUp, int length) => _trackside?.Write(rgbBottomUp, length);

        /// <summary>
        /// Ends the attempt: the score goes into the result, then files are closed, the judge video is made and the
        /// manifest is written on a background thread. Check <see cref="Finished"/>.
        /// </summary>
        public void Finish(int points, double scoreTime, double simTime, string status, int passes)
        {
            if (_finishing != null) return;
            _result.points = points;
            _result.time = scoreTime;
            _result.sim_time = simTime;
            _result.status = status;
            _result.gate_passes = passes;
            _result.date_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
            Log($"attempt ended: {status}, points {points}, time {scoreTime.ToString("F3", Inv)}");
            _events.Close(); _eventsCsv.Close(); _telemetry.Close(); _truth.Close();
            _finishing = Task.Run(Finalise);
        }

        private AttemptResult Finalise()
        {
            try
            {
                if (_onboard != null)
                {
                    _onboard.Close();
                    _trackside?.Close();
                    if (_onboard.Failed) Log("onboard video failed: " + _onboard.Errors);
                    if (_trackside != null && _trackside.Failed) Log("trackside video failed: " + _trackside.Errors);
                    if (!_onboard.Failed && _trackside != null && !_trackside.Failed) MakeJudgeVideo();
                }
                // Replay index: which files the judge viewer reads.
                File.WriteAllText(Path.Combine(Folder, "replay.grreplay"),
                    "{\n  \"schema\": 1,\n  \"track\": \"track.benchtrack.json\",\n  \"trajectory\": \"truth.csv\",\n  \"events\": \"events.jsonl\",\n  \"result\": \"result.json\"\n}\n");
                File.WriteAllText(Path.Combine(Folder, "result.json"), JsonUtility.ToJson(_result, true) + "\n", new UTF8Encoding(false));
                Log("files closed; writing the manifest");
                _log.Close();
                _log = null;
                AttemptManifest.Write(Folder, _result, _key);
            }
            catch (Exception e)
            {
                try { File.AppendAllText(Path.Combine(Folder, "recorder-error.txt"), e + "\n"); } catch (Exception) { }
            }
            _result.Folder = Folder;
            return _result;
        }

        /// <summary>
        /// judge.mp4: the trackside picture with the onboard picture in a corner, the running clock and every scoring
        /// decision with its reason as subtitles burnt into the picture.
        /// </summary>
        private void MakeJudgeVideo()
        {
            var ass = new StringBuilder();
            ass.Append("[Script Info]\nScriptType: v4.00+\nPlayResX: 1280\nPlayResY: 720\n\n[V4+ Styles]\n");
            ass.Append("Format: Name, Fontname, Fontsize, PrimaryColour, OutlineColour, BackColour, Bold, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV\n");
            ass.Append("Style: Clock,Arial,30,&H00FFFFFF,&H00000000,&H80000000,1,1,2,0,7,24,24,18\n");
            ass.Append("Style: Event,Arial,26,&H0000E5FF,&H00000000,&H80000000,1,1,2,0,1,24,24,24\n\n[Events]\n");
            ass.Append("Format: Layer, Start, End, Style, Text\n");
            // The clock and running score, one caption per tenth of a second.
            double end = Math.Max(_result.sim_time, 0.1);
            int pointsSoFar = 0, next = 0;
            var scored = new List<(double t, int points)>();
            foreach (string l in _judgeLines)
            {
                int bar = l.IndexOf('|');
                double t = double.Parse(l.Substring(0, bar), Inv);
                int open = l.LastIndexOf('('), close = l.LastIndexOf(')');
                if (l.Contains(": +1") && open > 0 && close > open && int.TryParse(l.Substring(open + 1, close - open - 1), out int p)) scored.Add((t, p));
            }
            for (double t = 0; t < end; t += 0.1)
            {
                while (next < scored.Count && scored[next].t <= t) pointsSoFar = scored[next++].points;
                ass.Append($"Dialogue: 0,{Stamp(t)},{Stamp(Math.Min(end, t + 0.1))},Clock,{_result.team}  {_result.track_id}   t = {t.ToString("F1", Inv)} s   points {pointsSoFar}\n");
            }
            foreach (string l in _judgeLines)
            {
                int bar = l.IndexOf('|');
                double t = double.Parse(l.Substring(0, bar), Inv);
                ass.Append($"Dialogue: 1,{Stamp(t)},{Stamp(t + 2.5)},Event,{t.ToString("F2", Inv)} s   {l.Substring(bar + 1).Replace("{", "(").Replace("}", ")")}\n");
            }
            File.WriteAllText(Path.Combine(Folder, "judge.ass"), ass.ToString(), new UTF8Encoding(false));

            string filter = "[1:v]scale=400:300[pip];[0:v][pip]overlay=W-w-16:H-h-16[v];[v]subtitles=judge.ass[out]";
            string err = BenchVideoPipe.Run(_ffmpeg,
                $"-y -loglevel error -i trackside.mp4 -i onboard.mp4 -filter_complex \"{filter}\" -map \"[out]\" " +
                "-c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p -movflags +faststart judge.mp4", Folder);
            if (err.Length > 0)
            {
                Log("judge video failed: " + err.Trim());
                string broken = Path.Combine(Folder, "judge.mp4");
                if (File.Exists(broken)) File.Delete(broken);
            }
        }

        private static string Stamp(double t)
        {
            int cs = (int)Math.Round(t * 100.0);
            return $"{cs / 360000}:{cs / 6000 % 60:00}:{cs / 100 % 60:00}.{cs % 100:00}";
        }
    }
}
