using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Video files through an external ffmpeg (EDU_PLAN F2: "export a replay as a video"). ffmpeg is a separate
    /// program that this one starts and feeds with pictures; none of its code is part of the simulator.
    /// </summary>
    public sealed class EduVideo
    {
        /// <summary>
        /// H.264 encoders in order of preference. The LGPL ffmpeg shipped for schools has no libx264; "h264_mf" is
        /// Windows' own encoder and "libopenh264" is built into that ffmpeg.
        /// </summary>
        private static readonly string[] Encoders =
        {
            "libx264 -preset veryfast -crf 21",
            "h264_mf -b:v 8M",
            "libopenh264 -b:v 8M",
            "mpeg4 -q:v 4",
        };

        private static string _found, _encoder;
        private static bool _looked;

        private readonly Process _process;
        private readonly Stream _stdin;
        private readonly StringBuilder _errors = new StringBuilder();

        public string OutputPath { get; }
        public bool Failed { get; private set; }
        public string Errors { get { lock (_errors) return _errors.ToString().Trim(); } }

        /// <summary>
        /// ffmpeg on this PC, or null: the file named by USHQYR_FFMPEG, the one shipped with the program
        /// (StreamingAssets/Edu or Bench), the developer's copy (ThirdParty/edu/ffmpeg), or one on the PATH.
        /// </summary>
        public static string Find()
        {
            if (_looked) return _found;
            _looked = true;
            bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            string exe = windows ? "ffmpeg.exe" : "ffmpeg";
            string project = Path.GetDirectoryName(Application.dataPath) ?? "";
            foreach (string candidate in new[]
                     {
                         Environment.GetEnvironmentVariable("USHQYR_FFMPEG"),
                         Path.Combine(Application.streamingAssetsPath, "Edu", exe),
                         Path.Combine(Application.streamingAssetsPath, "Bench", exe),
                         Path.Combine(project, "ThirdParty", "edu", "ffmpeg", exe),
                     })
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return _found = candidate;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(p)) return _found = p;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        /// <summary>The first encoder of <see cref="Encoders"/> that this ffmpeg can really run here (tried once, on a tiny picture).</summary>
        public static string Encoder(string ffmpeg)
        {
            if (_encoder != null) return _encoder;
            foreach (string encoder in Encoders)
            {
                string error = Run(ffmpeg, "-y -loglevel error -f lavfi -i color=c=black:s=320x240:r=30:d=0.2 -c:v " + encoder + " -pix_fmt yuv420p -f null -");
                if (error.Length == 0) return _encoder = encoder;
            }
            return _encoder = Encoders[Encoders.Length - 1];
        }

        /// <summary>
        /// Starts ffmpeg waiting for raw RGB pictures of the given size; it writes an MP4 that ordinary players open.
        /// Odd sizes are cut by one pixel (H.264 needs even ones).
        /// </summary>
        public EduVideo(string ffmpeg, int width, int height, float fps, string outputPath, bool flipVertically)
        {
            OutputPath = outputPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
            string rate = fps.ToString("0.###", CultureInfo.InvariantCulture);
            var info = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {rate} -i - " +
                            "-vf \"" + (flipVertically ? "vflip," : "") + "crop=trunc(iw/2)*2:trunc(ih/2)*2\" " +
                            $"-c:v {Encoder(ffmpeg)} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"",
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

        /// <summary>Ends the stream and waits for the file to be finished. True when the file is good.</summary>
        public bool Close()
        {
            try { _stdin.Close(); } catch (Exception) { Failed = true; }
            try
            {
                if (!_process.WaitForExit(120000)) { _process.Kill(); Failed = true; }
                else if (_process.ExitCode != 0) Failed = true;
            }
            catch (Exception) { Failed = true; }
            return !Failed && File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 0;
        }

        /// <summary>Stops without finishing and removes the half-written file.</summary>
        public void Abort()
        {
            Failed = true;
            try { _stdin.Close(); } catch (Exception) { }
            try { if (!_process.WaitForExit(5000)) _process.Kill(); } catch (Exception) { }
            try { if (File.Exists(OutputPath)) File.Delete(OutputPath); } catch (IOException) { }
        }

        /// <summary>Runs ffmpeg once; returns its error text (empty = fine).</summary>
        public static string Run(string ffmpeg, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo { FileName = ffmpeg, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                using (Process p = Process.Start(info))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0 ? "" : string.IsNullOrWhiteSpace(err) ? "ffmpeg exit code " + p.ExitCode : err.Trim();
                }
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
