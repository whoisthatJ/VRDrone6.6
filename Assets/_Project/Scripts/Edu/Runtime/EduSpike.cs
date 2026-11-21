using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ushqyr.Edu
{
    /// <summary>
    /// ED0 spikes, run inside a player build: `-edu-spike &lt;folder&gt;` tries the database, the licence check, the
    /// hardware fingerprint and the XLSX and PDF reports and writes `spike.txt` there; with `-edu-spike-serve
    /// &lt;seconds&gt;` it then keeps a MAVLink endpoint and the Studio page alive for that long. Nothing here is product
    /// code: it answers "does this work in a build" before the real modules are written.
    /// </summary>
    public sealed class EduSpike : MonoBehaviour
    {
        private StringBuilder _log = new StringBuilder();
        private string _out;
        private float _serveUntil;
        private MavlinkEndpoint _mav;
        private MavTestVehicle _mavVehicle;
        private StudioSpike _studio;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-edu-spike");
            if (at < 0 || at + 1 >= args.Length) return;
            var go = new GameObject("EduSpike");
            DontDestroyOnLoad(go);
            var spike = go.AddComponent<EduSpike>();
            spike._out = args[at + 1];
            int serve = Array.IndexOf(args, "-edu-spike-serve");
            spike.Run(serve >= 0 && serve + 1 < args.Length && float.TryParse(args[serve + 1], out float s) ? s : 0f);
        }

        private void Run(float serveSeconds)
        {
            Directory.CreateDirectory(_out);
            Try("database", Database);
            Try("licence signature", Licence);
            Try("hardware fingerprint", Fingerprint);
            Try("reports", Reports);
            if (serveSeconds > 0)
            {
                Try("mavlink endpoint", () =>
                {
                    _mavVehicle = new MavTestVehicle(51.0893, 71.4161, 350);
                    _mav = new MavlinkEndpoint(_mavVehicle);
                    return "sending to udp 127.0.0.1:14550";
                });
                Try("studio server", () =>
                {
                    _studio = new StudioSpike(Path.Combine(_out, "studio"));
                    File.WriteAllText(Path.Combine(_out, "studio_url.txt"), _studio.Url);
                    return "listening on 127.0.0.1:" + _studio.Port;
                });
                _serveUntil = Time.realtimeSinceStartup + serveSeconds;
            }
            Flush();
            if (serveSeconds <= 0) Application.Quit(0);
        }

        private void Update()
        {
            if (_serveUntil <= 0) return;
            _mavVehicle?.Step(Time.unscaledDeltaTime);
            _mav?.Poll(Time.realtimeSinceStartupAsDouble);
            _studio?.Step(Time.unscaledDeltaTime);
            if (Time.realtimeSinceStartup < _serveUntil) return;
            _serveUntil = 0;
            _log.AppendLine("mavlink frames received: " + (_mav != null ? _mav.Received : 0) + ", bad: " + (_mav != null ? _mav.BadFrames : 0));
            _log.AppendLine("studio: " + (_studio != null ? _studio.Summary : "not started"));
            Flush();
            _studio?.Dispose();
            _mav?.Dispose();
            Application.Quit(0);
        }

        private void OnDestroy()
        {
            _studio?.Dispose();
            _mav?.Dispose();
        }

        private void Try(string name, Func<string> step)
        {
            try { _log.AppendLine("PASS " + name + ": " + step()); }
            catch (Exception e) { _log.AppendLine("FAIL " + name + ": " + e.GetType().Name + ": " + e.Message); }
        }

        private void Flush() => File.WriteAllText(Path.Combine(_out, "spike.txt"), _log.ToString(), new UTF8Encoding(false));

        private string Database()
        {
            string path = Path.Combine(_out, "мектеп.db");
            if (File.Exists(path)) File.Delete(path);
            using (var db = new SqliteDb(path))
            {
                db.Execute("create table attempts(id integer primary key, student text not null, score real)");
                db.Execute("begin");
                for (int i = 0; i < 1000; i++) db.Execute("insert into attempts(student, score) values(?1, ?2)", "Әлібек Қуанышұлы " + i, 60 + i % 40);
                db.Execute("commit");
                List<object[]> rows = db.Query("select count(*), avg(score), max(student) from attempts");
                return "SQLite " + db.LibraryVersion + ", " + rows[0][0] + " rows, average " + rows[0][1] + ", text '" + rows[0][2] + "'";
            }
        }

        private static string Licence()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"customer\":\"№1 мектеп\",\"modules\":[\"training\",\"racing\"]}");
            // The key pair is made here and thrown away: the spike checks the mathematics in a build, not a real key.
            using (RSA vendor = RSA.Create())
            {
                vendor.KeySize = 2048;
                byte[] signature = vendor.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                RSAParameters pub = vendor.ExportParameters(false);
                bool good = LicenseCheck.SignatureValid(payload, signature, pub.Modulus, pub.Exponent);
                payload[5] ^= 1;
                bool edited = LicenseCheck.SignatureValid(payload, signature, pub.Modulus, pub.Exponent);
                if (!good || edited) throw new InvalidOperationException("genuine " + good + ", edited " + edited);
                string ecdsa;
                try
                {
                    using (ECDsa ec = ECDsa.Create())
                    {
                        byte[] s = ec.SignData(payload, HashAlgorithmName.SHA256);
                        ecdsa = "ECDSA also works (" + s.Length + " byte signature)";
                    }
                }
                catch (Exception e) { ecdsa = "ECDSA not available here (" + e.GetType().Name + ")"; }
                return "RSA-2048 SHA-256: genuine accepted, edited refused; " + ecdsa;
            }
        }

        /// <summary>The identifiers of this PC a licence could be tied to. Values never leave the PC; only hashes do.</summary>
        public static Dictionary<string, string> Identifiers()
        {
            var ids = new Dictionary<string, string>
            {
                ["unity"] = SystemInfo.deviceUniqueIdentifier,
                ["cpu"] = SystemInfo.processorType,
                ["model"] = SystemInfo.deviceModel,
                ["machine_guid"] = Registry("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Cryptography", "MachineGuid"),
                ["board"] = Registry("HKEY_LOCAL_MACHINE\\HARDWARE\\DESCRIPTION\\System\\BIOS", "BaseBoardProduct"),
                ["bios"] = Registry("HKEY_LOCAL_MACHINE\\HARDWARE\\DESCRIPTION\\System\\BIOS", "BIOSVersion"),
            };
            try
            {
                var macs = new List<string>();
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    string d = nic.Description.ToLowerInvariant();
                    if (d.Contains("virtual") || d.Contains("vpn") || d.Contains("hyper-v") || d.Contains("bluetooth") || d.Contains("loopback")) continue;
                    string mac = nic.GetPhysicalAddress().ToString();
                    if (mac.Length == 12) macs.Add(mac);
                }
                macs.Sort();
                ids["mac"] = macs.Count > 0 ? macs[0] : "";
            }
            catch (Exception) { ids["mac"] = ""; }
            return ids;
        }

        private static string Registry(string key, string value)
        {
            try
            {
                Type registry = Type.GetType("Microsoft.Win32.Registry, mscorlib") ?? Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry");
                if (registry == null) return "";
                object v = registry.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) })
                    .Invoke(null, new object[] { key, value, null });
                return v != null ? v.ToString() : "";
            }
            catch (Exception) { return ""; }
        }

        private static string Fingerprint()
        {
            Dictionary<string, string> ids = Identifiers();
            Dictionary<string, string> print = LicenseCheck.Fingerprint(ids);
            var found = new List<string>();
            var empty = new List<string>();
            foreach (KeyValuePair<string, string> id in ids) (id.Value.Length > 0 ? found : empty).Add(id.Key);
            Dictionary<string, string> again = LicenseCheck.Fingerprint(Identifiers());
            return print.Count + " identifiers readable (" + string.Join(", ", found) + "), not readable: (" + string.Join(", ", empty) +
                   "); same on a second read: " + LicenseCheck.Matches(print, again) + " of " + print.Count;
        }

        private string Reports()
        {
            string kk = "Қазақша: Ұшқыр әуе жарысы, өңір, ғарыш, һәм үміт. ӘҒҚҢӨҰҮҺІ әғқңөұүһі";
            string ru = "Русский: отчёт группы 7Б, подъём, ёжик";
            string en = "English: group gradebook, pass mark 60 of 100";
            var xlsx = new XlsxWriter();
            xlsx.AddSheet("Журнал", new double[] { 34, 12, 12 });
            xlsx.AddRow("Оқушы / Ученик / Student", "Балл", "Уақыт, с");
            xlsx.AddRow("Әлібек Қуанышұлы", 87, 25.539);
            xlsx.AddRow("Өмірзақ <Үміт> & Co", 60.5, 31.2);
            xlsx.AddRow(kk, ru, en);
            xlsx.Save(Path.Combine(_out, "report.xlsx"));

            byte[] font = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));
            var pdf = new PdfWriter(font, "LiberationSans");
            pdf.AddPage();
            pdf.Text(50, 60, 18, "Ushqyr: есеп / отчёт / report", true);
            pdf.Line(50, 68, 545, 68);
            pdf.Text(50, 100, 12, kk);
            pdf.Text(50, 120, 12, ru);
            pdf.Text(50, 140, 12, en);
            pdf.Save(Path.Combine(_out, "report.pdf"));
            string missing = pdf.Missing(kk + ru + en);
            if (missing.Length > 0) throw new InvalidOperationException("the font has no glyph for: " + missing);
            return "report.xlsx " + new FileInfo(Path.Combine(_out, "report.xlsx")).Length + " bytes, report.pdf " +
                   new FileInfo(Path.Combine(_out, "report.pdf")).Length + " bytes, no missing glyphs";
        }
    }

    /// <summary>
    /// The Studio idea at its smallest: a page served on 127.0.0.1 behind a session token, a Run button that starts the
    /// student's Python program as a separate process, and a drone that the program moves through blocking commands.
    /// The drone here is a point moving at 1 m/s, not flight physics.
    /// </summary>
    public sealed class StudioSpike : IDisposable
    {
        private readonly HttpListener _http = new HttpListener();
        private readonly string _token, _folder;
        private readonly object _lock = new object();
        private readonly StringBuilder _output = new StringBuilder();
        private Process _program;
        private Vector3 _pos, _target;
        private ManualResetEventSlim _arrived;
        private int _runs, _commands;
        private volatile bool _closed;

        public int Port { get; }
        public string Url => "http://127.0.0.1:" + Port + "/?token=" + _token;
        public string Summary => _runs + " program run(s), " + _commands + " drone command(s), drone at " + _pos;

        public StudioSpike(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            _token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _http.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            _http.Start();
            File.WriteAllText(Path.Combine(folder, "gaterush.py"), Helper);
            new Thread(Listen) { IsBackground = true, Name = "StudioSpike" }.Start();
        }

        /// <summary>Called from the game loop: moves the drone and releases the program's blocking command on arrival.</summary>
        public void Step(float dt)
        {
            lock (_lock)
            {
                _pos = Vector3.MoveTowards(_pos, _target, dt * 1f);
                if (_arrived != null && (_pos - _target).sqrMagnitude < 1e-6f)
                {
                    _arrived.Set();
                    _arrived = null;
                }
            }
        }

        private void Listen()
        {
            while (!_closed)
            {
                HttpListenerContext ctx;
                try { ctx = _http.GetContext(); }
                catch (Exception) { return; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Serve(ctx); }
                    catch (Exception e) { Debug.LogWarning("[EduSpike] studio request failed: " + e.Message); }
                });
            }
        }

        private void Serve(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            string given = req.QueryString["token"] ?? req.Headers["X-Token"];
            bool local = req.RemoteEndPoint != null && IPAddress.IsLoopback(req.RemoteEndPoint.Address);
            if (!local || given != _token)
            {
                Reply(ctx, 403, "text/plain", "forbidden");
                return;
            }
            string path = req.Url.AbsolutePath;
            if (path == "/") Reply(ctx, 200, "text/html; charset=utf-8", Page.Replace("%TOKEN%", _token));
            else if (path == "/state")
            {
                lock (_lock)
                    Reply(ctx, 200, "application/json", "{\"running\":" + (_program != null && !_program.HasExited ? "true" : "false") +
                        ",\"x\":" + _pos.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"y\":" + _pos.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"z\":" + _pos.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                        ",\"output\":\"" + Json(_output.ToString()) + "\"}");
            }
            else if (path == "/run" && req.HttpMethod == "POST")
            {
                string code;
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) code = reader.ReadToEnd();
                Reply(ctx, 200, "text/plain", Start(code));
            }
            else if (path == "/stop" && req.HttpMethod == "POST")
            {
                Stop();
                Reply(ctx, 200, "text/plain", "stopped");
            }
            else if (path == "/api/move" && req.HttpMethod == "POST")
            {
                float.TryParse(req.QueryString["x"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
                float.TryParse(req.QueryString["y"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
                float.TryParse(req.QueryString["z"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
                var done = new ManualResetEventSlim(false);
                lock (_lock)
                {
                    _target = _pos + new Vector3(Mathf.Clamp(x, -50, 50), Mathf.Clamp(y, -50, 50), Mathf.Clamp(z, -50, 50));
                    _arrived = done;
                    _commands++;
                }
                bool ok = done.Wait(120000);
                lock (_lock) Reply(ctx, ok ? 200 : 504, "text/plain", ok ? _pos.x + " " + _pos.y + " " + _pos.z : "timeout");
            }
            else Reply(ctx, 404, "text/plain", "not found");
        }

        private string Start(string code)
        {
            Stop();
            string file = Path.Combine(_folder, "program.py");
            File.WriteAllText(file, code, new UTF8Encoding(false));
            var info = new ProcessStartInfo
            {
                // The product will ship its own Python; the spike uses the one on this PC.
                FileName = "python",
                Arguments = "-u -X utf8 \"" + file + "\"",
                WorkingDirectory = _folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            info.EnvironmentVariables["GATERUSH_URL"] = "http://127.0.0.1:" + Port;
            info.EnvironmentVariables["GATERUSH_TOKEN"] = _token;
            info.EnvironmentVariables["PYTHONPATH"] = _folder;
            var p = new Process { StartInfo = info, EnableRaisingEvents = true };
            DataReceivedEventHandler collect = (s, e) =>
            {
                if (e.Data == null) return;
                lock (_lock) if (_output.Length < 20000) _output.AppendLine(e.Data);
            };
            p.OutputDataReceived += collect;
            p.ErrorDataReceived += collect;
            lock (_lock)
            {
                _output.Length = 0;
                _pos = _target = Vector3.zero;
                _runs++;
            }
            try { p.Start(); }
            catch (Exception e) { return "cannot start Python: " + e.Message; }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _program = p;
            return "started";
        }

        private void Stop()
        {
            Process p = _program;
            _program = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); }
            catch (Exception) { }
            lock (_lock)
            {
                _target = _pos;
                _arrived?.Set();
                _arrived = null;
            }
        }

        public void Dispose()
        {
            _closed = true;
            Stop();
            try { _http.Close(); }
            catch (Exception) { }
        }

        private static void Reply(HttpListenerContext ctx, int status, string type, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = type;
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static string Json(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c < 0x20) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private const string Helper =
            "import os, urllib.request\n" +
            "class Drone:\n" +
            "    def _move(self, x=0, y=0, z=0):\n" +
            "        url = '%s/api/move?x=%s&y=%s&z=%s' % (os.environ['GATERUSH_URL'], x, y, z)\n" +
            "        req = urllib.request.Request(url, data=b'', headers={'X-Token': os.environ['GATERUSH_TOKEN']})\n" +
            "        return urllib.request.urlopen(req, timeout=130).read().decode()\n" +
            "    def takeoff(self, height): return self._move(y=height)\n" +
            "    def forward(self, metres): return self._move(z=metres)\n" +
            "    def right(self, metres): return self._move(x=metres)\n" +
            "    def land(self): return self._move(y=-1000)\n";

        private const string Page =
            "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>Ushqyr Studio (spike)</title><style>" +
            "body{font:15px system-ui,sans-serif;margin:0;display:grid;grid-template-rows:auto 1fr auto;height:100vh;background:#14181d;color:#e6e9ee}" +
            "header{padding:10px 14px;display:flex;gap:8px;align-items:center;border-bottom:1px solid #2a313a}" +
            "button{font:inherit;padding:6px 14px;border-radius:6px;border:1px solid #3a4450;background:#1f2630;color:inherit;cursor:pointer}" +
            "#run{background:#1f6f43;border-color:#2a8c56}main{display:grid;grid-template-columns:1fr 1fr;min-height:0}" +
            "textarea,pre{margin:0;padding:12px;font:14px ui-monospace,Consolas,monospace;border:0;background:#0f1216;color:inherit;overflow:auto}" +
            "pre{border-left:1px solid #2a313a;white-space:pre-wrap}footer{padding:8px 14px;border-top:1px solid #2a313a}" +
            "</style></head><body><header><strong>Ushqyr Studio</strong><span style=\"opacity:.6\">spike / сынақ / проба</span>" +
            "<span style=\"flex:1\"></span><button id=\"run\">▶ Іске қосу / Запуск / Run</button><button id=\"stop\">■ Stop</button></header>" +
            "<main><textarea id=\"code\" spellcheck=\"false\">from gaterush import Drone\n\ndrone = Drone()\ndrone.takeoff(2)\nprint('Ұшып көтерілді / взлетел / took off')\ndrone.forward(3)\ndrone.right(1)\nprint('done')\n</textarea>" +
            "<pre id=\"out\"></pre></main><footer id=\"pos\">drone: 0 0 0</footer><script>" +
            "const T='%TOKEN%';const q=p=>p+(p.includes('?')?'&':'?')+'token='+T;const $=i=>document.getElementById(i);" +
            "$('run').onclick=()=>fetch(q('/run'),{method:'POST',body:$('code').value});$('stop').onclick=()=>fetch(q('/stop'),{method:'POST'});" +
            "setInterval(async()=>{const s=await (await fetch(q('/state'))).json();$('out').textContent=s.output;" +
            "$('pos').textContent='drone: x '+s.x+'  y '+s.y+'  z '+s.z+(s.running?'  (running)':'');},300);" +
            "</script></body></html>";
    }
}
