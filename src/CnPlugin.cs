using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace NOcn
{
    [BepInPlugin("com.bluefish.nocn", "Nuclear Option Chinese", "1.3.0")]
    public class CnPlugin : BaseUnityPlugin
    {
        public static CnPlugin Inst;
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        public static readonly Dictionary<string, string> MapUpper = new Dictionary<string, string>(StringComparer.Ordinal);
        public static readonly Dictionary<string, string> Seen = new Dictionary<string, string>();
        static readonly List<Rule> Rules = new List<Rule>();
        static readonly Dictionary<string, List<Rule>> Buckets = new Dictionary<string, List<Rule>>();
        static readonly List<Rule> ShortPrefixRules = new List<Rule>();

        public static string DumpPath;
        public static string MapPath;
        public static string FontPath;
        public static Font FakeFont;
        public static TMP_FontAsset Cjk;
        static byte[] fontBytes;
        static bool cjkBroken;
        public static bool DebugMode;

        public static readonly string[] OsFonts = new string[]
        {
            @"C:\Windows\Fonts\msyh.ttc",
            @"C:\Windows\Fonts\msyhbd.ttc",
            @"C:\Windows\Fonts\simhei.ttf",
            @"C:\Windows\Fonts\simsun.ttc",
            @"C:\Windows\Fonts\Deng.ttf",
            @"C:\Windows\Fonts\msjh.ttc",
        };

        void Awake()
        {
            Inst = this;
            DumpPath = Path.Combine(Paths.GameRootPath, "nocn_texts.txt");
            MapPath = Path.Combine(Paths.PluginPath, "NuclearOptionCn", "zh_cn.tsv");
            FontPath = Path.Combine(Paths.PluginPath, "NuclearOptionCn", "font.ttf");
            DebugMode = File.Exists(Path.Combine(Paths.GameRootPath, "nocn_debug.txt"));
            LoadConfig();
            LoadMap();

            var h = new Harmony("com.bluefish.nocn");
            if (Enable)
            {
                PatchOne(h, typeof(PatchLoadFontFace), "FontEngine.LoadFontFace(Font,int)");
                PatchOne(h, typeof(PatchLoadFontFace3), "FontEngine.LoadFontFace(Font,int,int)");
                PatchOne(h, typeof(PatchSetText), "TMP_Text.set_text");
                PatchOne(h, typeof(PatchSetTextStr), "TMP_Text.SetText(string,bool)");
                PatchOne(h, typeof(PatchOnEnableUGUI), "TextMeshProUGUI.OnEnable");
                PatchOne(h, typeof(PatchOnEnableTMP), "TextMeshPro.OnEnable");
                PatchOne(h, typeof(PatchLegacySetText), "UI.Text.set_text");
                PatchOne(h, typeof(PatchLegacyOnEnable), "UI.Text.OnEnable");
                try { SetupFont(); } catch (Exception e) { Logger.LogError("font setup: " + e); }
            }
            else Logger.LogWarning("translation disabled by config (enable = false)");

            var go = new GameObject("NOCN_Ticker");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<Ticker>();
        }

        void PatchOne(Harmony h, Type t, string what)
        {
            try { h.CreateClassProcessor(t).Patch(); Logger.LogInfo("patched " + what); }
            catch (Exception e) { Logger.LogError("patch " + what + " failed: " + e.Message); }
        }

        public static void LoadMap()
        {
            Map.Clear();
            MapUpper.Clear();
            Rules.Clear();
            Buckets.Clear();
            ShortPrefixRules.Clear();
            try
            {
                if (!File.Exists(MapPath)) { if (Inst != null) Inst.Logger.LogWarning("no map file: " + MapPath); return; }
                int n = 0;
                foreach (var raw in File.ReadAllLines(MapPath, Encoding.UTF8))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    int i = raw.IndexOf('\t');
                    if (i <= 0) continue;
                    string k = Unescape(raw.Substring(0, i));
                    string v = Unescape(raw.Substring(i + 1));
                    if (k.Length == 0 || v.Length == 0) continue;
                    Map[k] = v;
                    string up = k.ToUpperInvariant();
                    if (up != k && !MapUpper.ContainsKey(up)) MapUpper[up] = v;
                    n++;
                }
                BuildRules();
                if (Inst != null) Inst.Logger.LogInfo("loaded " + n + " entries, " + Rules.Count + " pattern rules");
            }
            catch (Exception e) { if (Inst != null) Inst.Logger.LogError("load map: " + e); }
        }

        static void BuildRules()
        {
            foreach (var kv in Map)
            {
                string k = kv.Key;
                if (k.IndexOf('{') < 0 || k.IndexOf('}') < 0) continue;
                var literals = new List<string>();
                var placeholders = new List<string>();
                var sb = new StringBuilder();
                int i = 0;
                bool ok = true;
                while (i < k.Length)
                {
                    int a = k.IndexOf('{', i);
                    if (a < 0) { sb.Append(Regex.Escape(k.Substring(i))); break; }
                    int b = k.IndexOf('}', a);
                    if (b < 0) { ok = false; break; }
                    string token = k.Substring(a + 1, b - a - 1);
                    string name = token;
                    int colon = token.IndexOf(':');
                    if (colon >= 0) name = token.Substring(0, colon);
                    int num;
                    if (!int.TryParse(name, out num)) { ok = false; break; }
                    sb.Append(Regex.Escape(k.Substring(i, a - i)));
                    sb.Append("(.+?)");
                    literals.Add(k.Substring(i, a - i));
                    placeholders.Add(name);
                    i = b + 1;
                }
                if (!ok) continue;
                var rule = new Rule();
                try { rule.Rx = new Regex("^" + sb.ToString() + "$", RegexOptions.Compiled); }
                catch { continue; }
                rule.Template = kv.Value;
                rule.Placeholders = placeholders.ToArray();
                string longest = "";
                for (int q = 0; q < literals.Count; q++) if (literals[q].Length > longest.Length) longest = literals[q];
                rule.Token = longest;
                Rules.Add(rule);
                string first = literals.Count > 0 ? literals[0] : "";
                string key = first.Length >= 3 ? first.Substring(0, 3) : "";
                if (key.Length == 0) ShortPrefixRules.Add(rule);
                else
                {
                    List<Rule> list;
                    if (!Buckets.TryGetValue(key, out list)) { list = new List<Rule>(); Buckets[key] = list; }
                    list.Add(rule);
                }
            }
        }

        static string Unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
        }

        public static void SetupFont()
        {
            if (File.Exists(FontPath)) { fontBytes = File.ReadAllBytes(FontPath); }
            else
            {
                foreach (var p in OsFonts)
                {
                    if (File.Exists(p)) { fontBytes = File.ReadAllBytes(p); if (Inst != null) Inst.Logger.LogInfo("using os font " + p); break; }
                }
            }
            if (fontBytes == null) { if (Inst != null) Inst.Logger.LogError("no CJK font found"); cjkBroken = true; return; }

            FakeFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 90);
            if (FakeFont == null) FakeFont = Font.CreateDynamicFontFromOSFont("SimHei", 90);
            if (FakeFont == null) { if (Inst != null) Inst.Logger.LogError("CreateDynamicFontFromOSFont failed"); cjkBroken = true; return; }
            FakeFont.name = "NOCN_CJK";

            FontEngine.InitializeFontEngine();
            var defFont = TMP_Settings.defaultFontAsset;
            int ps = 90;
            if (defFont != null) { try { if (defFont.faceInfo.pointSize > 0) ps = defFont.faceInfo.pointSize; } catch { } }
            Cjk = TMP_FontAsset.CreateFontAsset(FakeFont, ps, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (Cjk == null) { if (Inst != null) Inst.Logger.LogError("CreateFontAsset failed"); cjkBroken = true; return; }
            Cjk.name = "NOCN CJK";
            // the atlas is generated at the same point size the game's own font
            // uses, so TMP's fontSize -> scale math yields identical glyph height;
            // faceInfo itself is left as the CJK font reports it

            var def = TMP_Settings.defaultFontAsset;
            if (def != null)
            {
                if (def.fallbackFontAssetTable == null) def.fallbackFontAssetTable = new List<TMP_FontAsset>();
                def.fallbackFontAssetTable.Add(Cjk);
            }
            try
            {
                var sf = TMP_Settings.fallbackFontAssets;
                if (sf != null) sf.Add(Cjk);
            }
            catch { }
            if (Inst != null) Inst.Logger.LogInfo("cjk font ready, default=" + (def == null ? "null" : def.name));
        }

        // 0 = 保留原版拉丁字体，仅中文走回退字体
        // 1 = 全部文本统一使用中文字体（推荐，字形统一）
        // 2 = 仅已翻译的文本统一使用中文字体
        public static int FontMode = 0;
        public static bool Enable = true;
        public static bool FpsLog;
        public static bool Bench;
        public static bool SweepOn = false;

        public static void LoadConfig()
        {
            try
            {
                string p = Path.Combine(Paths.PluginPath, "NuclearOptionCn", "config.ini");
                if (!File.Exists(p)) return;
                foreach (var raw in File.ReadAllLines(p, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line.IndexOf('=') < 0) continue;
                    string k = line.Substring(0, line.IndexOf('=')).Trim().ToLowerInvariant();
                    string v = line.Substring(line.IndexOf('=') + 1).Trim().ToLowerInvariant();
                    if (k == "font_mode")
                    {
                        if (v == "fallback") FontMode = 0;
                        else if (v == "translated") FontMode = 2;
                        else FontMode = 1;
                    }
                    else if (k == "enable" || k == "enabled")
                    {
                        Enable = !(v == "false" || v == "0" || v == "off" || v == "no");
                    }
                    else if (k == "debug_fps")
                    {
                        FpsLog = (v == "true" || v == "1" || v == "on" || v == "yes");
                    }
                    else if (k == "sweep")
                    {
                        SweepOn = !(v == "false" || v == "0" || v == "off" || v == "no");
                    }
                    else if (k == "bench")
                    {
                        Bench = (v == "true" || v == "1" || v == "on" || v == "yes");
                    }
                }
                if (Inst != null) Inst.Logger.LogInfo("font_mode = " + FontMode);
            }
            catch (Exception e) { if (Inst != null) Inst.Logger.LogWarning("config: " + e.Message); }
        }

        public static void EnsureFont(TMP_Text t)
        {
            if (Cjk == null || t == null) return;
            try
            {
                if (t.font != Cjk) t.font = Cjk;
            }
            catch { }
        }

        public static bool IsFake(Font f)
        {
            return f != null && FakeFont != null && ReferenceEquals(f, FakeFont);
        }

        public static FontEngineError LoadBytes(int pointSize, int faceIndex)
        {
            if (cjkBroken || fontBytes == null) return FontEngineError.Invalid_File;
            return FontEngine.LoadFontFace(fontBytes, pointSize, faceIndex);
        }

        static readonly char[] QuoteTrim = { ' ', '\t', '\r', '\n', '•', '·', '-', '\'', '"', '\u2018', '\u2019', '\u201c', '\u201d' };

        public static string Translate(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string v;
            if (Map.TryGetValue(s, out v)) return v;
            // workshop liveries are shown as "<author name> (workshop)"; keep the
            // author's name but localise the suffix
            if (s.EndsWith(" (workshop)", StringComparison.Ordinal) || s.EndsWith("(workshop)", StringComparison.Ordinal))
            {
                string whead = s.Substring(0, s.LastIndexOf('(')).TrimEnd();
                string wz = Translate(whead);
                if (!string.Equals(wz, whead)) return wz + "（创意工坊）";
                return whead + "（创意工坊）";
            }
            // HUD toggles are authored with <b>…</b> emphasis; match the plain form too
            if (s.IndexOf("<b>", StringComparison.Ordinal) >= 0 || s.IndexOf("</b>", StringComparison.Ordinal) >= 0)
            {
                string plain = s.Replace("<b>", "").Replace("</b>", "");
                if (plain.Length > 0)
                {
                    if (Map.TryGetValue(plain, out v)) return v;
                    string pz = Translate(plain);
                    if (!string.Equals(pz, plain)) return pz;
                }
            }
            if (s.Length > 1)
            {
                string t = s.Trim();
                if (t.Length != s.Length && t.Length > 0 && Map.TryGetValue(t, out v)) return v;
            }
            if (s.IndexOf('\r') >= 0 && s.Length < 4000)
            {
                string n = s.Replace("\r\n", "\n");
                if (Map.TryGetValue(n, out v)) return v;
                string nt = n.Trim();
                if (nt.Length > 0 && Map.TryGetValue(nt, out v)) return v;
            }

            // multi-line blocks (hover tooltips, platoon lists) are usually a
            // template plus live values; translate them line by line.
            // strip the trailing newlines first so the per-line calls can never
            // re-enter this branch (that recursion used to overflow the stack)
            if (s.IndexOf('\n') >= 0 && s.Length < 1200)
            {
                string body = s.TrimEnd('\r', '\n');
                string tail = s.Substring(body.Length);
                string[] parts = body.Split('\n');
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string one = Translate(parts[i]);
                    if (string.Equals(one, parts[i]) && parts[i].Length > 0)
                    {
                        string vv;
                        if (Map.TryGetValue(parts[i] + "\n", out vv)) one = vv.TrimEnd('\r', '\n');
                    }
                    if (!string.Equals(one, parts[i])) { parts[i] = one; any = true; }
                }
                if (any) return string.Join("\n", parts) + tail;
            }
            if (s.Length < 4000)
            {
                string q = s.Trim(QuoteTrim);
                if (q.Length != s.Length && q.Length > 0)
                {
                    if (Map.TryGetValue(q, out v)) return v;
                    string qn = q.Replace("\r\n", "\n");
                    if (Map.TryGetValue(qn, out v)) return v;
                }
            }
            if (AllCaps(s) && MapUpper.TryGetValue(s, out v)) return v;
            if (s.Length < 2000)
            {
                char last = s[s.Length - 1];
                if (last == '.' || last == '!' || last == '?')
                {
                    if (Map.TryGetValue(s.Substring(0, s.Length - 1), out v)) return v;
                }
                else if (Map.TryGetValue(s + ".", out v)) return v;
            }
            if (s.Length > 2000) return s;
            // "Stability Assist Enabled" / "Nav Lights On" -> translated label + Chinese state
            if (s.Length > 4 && s.Length < 120)
            {
                for (int i = 0; i < StatusSuffix.Length; i++)
                {
                    if (!s.EndsWith(StatusSuffix[i], StringComparison.Ordinal)) continue;
                    string head = s.Substring(0, s.Length - StatusSuffix[i].Length);
                    string hv;
                    if (head.Length > 0 && Map.TryGetValue(head, out hv)) return hv + StatusZh[i];
                }
            }
            // "AAM-29 Scythe x4" -> "AAM-29 镰刀 x4"
            if (s.Length > 4 && s.Length < 400)
            {
                int sp = s.LastIndexOf(" x", StringComparison.Ordinal);
                if (sp > 0 && sp + 2 < s.Length)
                {
                    bool digits = true;
                    for (int i = sp + 2; i < s.Length; i++)
                    {
                        char c = s[i];
                        if (c < '0' || c > '9') { digits = false; break; }
                    }
                    if (digits)
                    {
                        string head = s.Substring(0, sp);
                        string hv;
                        if (Map.TryGetValue(head, out hv)) return hv + s.Substring(sp);
                    }
                }
            }
            // "Escape 1.0km" / "CAP 5.2km" -> "逃离 1.0km"
            if (s.Length > 4 && s.Length < 120)
            {
                var md = DistSuffix.Match(s);
                if (md.Success)
                {
                    string dh = md.Groups["h"].Value;
                    string dhv;
                    if (dh.Length > 0 && Map.TryGetValue(dh, out dhv))
                        return dhv + " " + md.Groups["n"].Value + md.Groups["u"].Value;
                }
            }
            // "Heartland (38)" -> "腹地 (38)"
            if (s.Length > 4 && s.Length < 120)
            {
                var mc = CountSuffix.Match(s);
                if (mc.Success)
                {
                    string ch = mc.Groups["h"].Value;
                    string chv;
                    if (ch.Length > 0 && Map.TryGetValue(ch, out chv))
                        return chv + " (" + mc.Groups["n"].Value + ")";
                }
            }
            // "12.7mm Machine Gun (500 rounds)" -> "12.7mm 机枪（500 发）"
            if (s.Length > 4 && s.Length < 160)
            {
                var mp = ParenSuffix.Match(s);
                if (mp.Success)
                {
                    string ph = mp.Groups["h"].Value;
                    string phv;
                    if (ph.Length > 0 && Map.TryGetValue(ph, out phv))
                    {
                        string inner = mp.Groups["n"].Value;
                        if (inner.EndsWith(" rounds", StringComparison.Ordinal) && inner.Length > 8)
                        {
                            string num = inner.Substring(0, inner.Length - 7).Trim();
                            if (IsDigits(num)) inner = num + " 发";
                        }
                        return phv + " (" + inner + ")";
                    }
                }
            }
            Rule r = FindRule(s);
            if (r != null) return r.Apply(s);
            if (DebugMode && s.Length > 3 && s.Length < 400 && !HasCjk(s) && HasLatin(s)) MissLog(s);
            return s;
        }

        static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        static readonly string[] StatusSuffix = { " Enabled", " Disabled", " On", " Off", " Deployed", " Retracted", " Raised", " Lowered" };
        static readonly string[] StatusZh = { " 已启用", " 已禁用", " 已开启", " 已关闭", " 已展开", " 已收起", " 已放下", " 已收起" };
        static readonly Regex DistSuffix = new Regex(@"^(?<h>.+?)\s+(?<n>[0-9]+(?:\.[0-9]+)?)\s*(?<u>km|m|mi|nm|ft|kt|km/h|m/s|s)?$", RegexOptions.Compiled);
        static readonly Regex CountSuffix = new Regex(@"^(?<h>.+?)\s*\((?<n>[0-9]+)\)$", RegexOptions.Compiled);
        static readonly Regex ParenSuffix = new Regex(@"^(?<h>.+?)\s*\((?<n>[^()]*)\)$", RegexOptions.Compiled);

        static bool AllCaps(string s)
        {
            bool hasLetter = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 'a' && c <= 'z') return false;
                if (c >= 'A' && c <= 'Z') hasLetter = true;
            }
            return hasLetter;
        }

        public static bool HasLatin(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
            }
            return false;
        }

        static readonly HashSet<string> missSeen = new HashSet<string>();
        public static void MissLog(string s)
        {
            try
            {
                if (!missSeen.Add(s)) return;
                File.AppendAllText(Path.Combine(Paths.GameRootPath, "nocn_miss.txt"), s.Replace("\r", "\\r").Replace("\n", "\\n") + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static bool HasCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u4e00' && c <= '\u9fff') return true;
            }
            return false;
        }

        static Rule FindRule(string s)
        {
            List<Rule> list;
            if (s.Length >= 3 && Buckets.TryGetValue(s.Substring(0, 3), out list))
            {
                for (int i = 0; i < list.Count; i++) if (list[i].Rx.IsMatch(s)) return list[i];
            }
            for (int i = 0; i < ShortPrefixRules.Count; i++)
            {
                var rr = ShortPrefixRules[i];
                if (rr.Token.Length > 0 && s.IndexOf(rr.Token, StringComparison.Ordinal) < 0) continue;
                if (rr.Rx.IsMatch(s)) return rr;
            }
            return null;
        }

        void Update()
        {
            if (inputBroken) return;
            try
            {
                if (Input.GetKeyDown(KeyCode.F10)) { LoadMap(); Logger.LogInfo("map reloaded (F10)"); }
            }
            catch { inputBroken = true; }
        }

        void OnApplicationQuit() { if (DebugMode) Dump(); }

        public static void Record(TMP_Text t, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (s.Length > 4000) s = s.Substring(0, 4000);
            if (Seen.ContainsKey(s)) return;
            string font = "?";
            try { font = t.font == null ? "null" : t.font.name; } catch { }
            string comp = "?";
            try { comp = t.GetType().Name; } catch { }
            Seen[s] = comp + "|" + font + "|" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            recCount++;
            if (recCount % 400 == 0) Dump();
        }

        static int recCount;
        static bool inputBroken;

        public static void Sweep()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_Text>();
                for (int i = 0; i < all.Length; i++)
                {
                    try { Record(all[i], all[i].text); } catch { }
                }
                Dump();
            }
            catch (Exception e) { if (Inst != null) Inst.Logger.LogWarning("sweep fail " + e.Message); }
        }

        public static Font LegacyFont;
        static double SweepTotalMs;
        static int SweepCalls;

        /// <summary>
        /// Periodic pass over every live TMP text: catch texts that were assigned through
        /// a path we do not patch (serialized m_text, SetText overloads, ForceMeshUpdate).
        /// </summary>
        public static void SweepTranslate()
        {
            int changed = 0, scanned = 0;
            var swSweep = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    TMP_Text t = all[i];
                    if (t == null) continue;
                    string s;
                    try { s = t.text; } catch { continue; }
                    if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                    scanned++;
                    if (HasCjk(s)) continue;
                    string zh = Translate(s);
                    if (!ReferenceEquals(zh, s))
                    {
                        changed++;
                        try { t.text = zh; } catch { }
                    }
                }
                var all2 = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(true);
                for (int i = 0; i < all2.Length; i++)
                {
                    if (all2[i] != null) TranslateLegacy(all2[i]);
                }
                if (DebugMode)
                {
                    swSweep.Stop();
                    SweepTotalMs += swSweep.Elapsed.TotalMilliseconds;
                    SweepCalls++;
                    if (SweepCalls == 1 || SweepCalls % 20 == 0)
                        BepInEx.Logging.Logger.CreateLogSource("NOCN").LogInfo(string.Format("sweep {0:F2} ms (last) / {1:F2} ms avg, scanned {2}, changed {3}, calls {4}",
                            swSweep.Elapsed.TotalMilliseconds, SweepTotalMs / SweepCalls, scanned, changed, SweepCalls));
                }
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("NOCN").LogWarning("sweep translate fail " + e); }
        }

        static TMP_Text[] sliceBuf;
        static int sliceIdx;
        static float sliceRefreshAt;

        /// <summary>
        /// Incremental translator pass: the component list is refreshed every 15 s, but only
        /// a small slice is handled per frame, so this never costs a visible frame hitch.
        /// </summary>
        public static void SweepSlice()
        {
            try
            {
                float now = Time.unscaledTime;
                if (sliceBuf == null || now > sliceRefreshAt)
                {
                    sliceBuf = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
                    sliceIdx = 0;
                    sliceRefreshAt = now + 15f;
                    return;
                }
                if (sliceBuf.Length == 0) return;
                int n = Math.Min(48, sliceBuf.Length - sliceIdx);
                for (int k = 0; k < n; k++)
                {
                    TMP_Text t = sliceBuf[sliceIdx++];
                    if (t == null) continue;
                    string s;
                    try { s = t.text; } catch { continue; }
                    if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                    if (DebugMode) Record(t, s);
                    if (HasCjk(s)) continue;
                    string zh = Translate(s);
                    if (!ReferenceEquals(zh, s)) { try { t.text = zh; } catch { } }
                }
                if (sliceIdx >= sliceBuf.Length) sliceIdx = 0;
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("NOCN").LogWarning("slice fail " + e.Message); }
        }

        public static void EnsureLegacyFont(UnityEngine.UI.Text t)
        {
            try
            {
                if (LegacyFont == null)
                {
                    LegacyFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 24);
                    if (LegacyFont == null) LegacyFont = Font.CreateDynamicFontFromOSFont("SimHei", 24);
                }
                if (LegacyFont != null && t.font != LegacyFont) t.font = LegacyFont;
            }
            catch { }
        }

        public static void TranslateLegacy(UnityEngine.UI.Text t)
        {
            try
            {
                string s = t.text;
                if (string.IsNullOrEmpty(s)) return;
                string zh = Translate(s);
                if (!string.Equals(zh, s))
                {
                    if (HasCjk(zh)) EnsureLegacyFont(t);
                    t.text = zh;
                }
            }
            catch { }
        }

        public static void TranslateAll()
        {
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int n = 0;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_Text>();
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    try
                    {
                        if (FontMode == 1) EnsureFont(t);
                        string s = t.text;
                        if (string.IsNullOrEmpty(s)) continue;
                        string zh = Translate(s);
                        if (!ReferenceEquals(zh, s)) { if (FontMode == 2) EnsureFont(t); t.text = zh; n++; }
                    }
                    catch { }
                }
            }
            catch (Exception e) { if (Inst != null) Inst.Logger.LogWarning("translate all fail " + e.Message); }
            try
            {
                var all2 = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>();
                for (int i = 0; i < all2.Length; i++)
                {
                    var t = all2[i];
                    if (t == null) continue;
                    try
                    {
                        string s = t.text;
                        if (string.IsNullOrEmpty(s)) continue;
                        string zh = Translate(s);
                        if (!string.Equals(zh, s)) { if (HasCjk(zh)) EnsureLegacyFont(t); t.text = zh; n++; }
                    }
                    catch { }
                }
            }
            catch { }
            if (Inst != null && n > 0) Inst.Logger.LogInfo("sweep translated " + n + " texts in " + Math.Round(swAll.Elapsed.TotalMilliseconds, 1) + " ms");
        }

        public static void Dump()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# component\tfont\tscene\ttext");
                foreach (var kv in Seen) sb.AppendLine(kv.Value + "\t" + kv.Key.Replace("\r", "").Replace("\n", "\\n"));
                File.WriteAllText(DumpPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { if (Inst != null) Inst.Logger.LogWarning("dump fail " + e.Message); }
        }

        static readonly HashSet<string> FieldSeen = new HashSet<string>();

        static void TakeString(string s)
        {
            if (s == null) return;
            s = s.Trim();
            if (s.Length < 20 || s.Length > 800) return;
            if (s.IndexOf(' ') < 0) return;
            bool hasLetter = false, hasLower = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetter(c)) hasLetter = true;
                if (c >= 'a' && c <= 'z') hasLower = true;
                if (c > 126) return;
            }
            if (!hasLetter || !hasLower) return;
            if (s.Contains("://") || s.Contains(".png") || s.Contains("Assets/")) return;
            FieldSeen.Add(s);
        }

        static void Walk(object v, int depth)
        {
            if (v == null || depth > 3) return;
            string s = v as string;
            if (s != null) { TakeString(s); return; }
            System.Collections.IEnumerable en = v as System.Collections.IEnumerable;
            if (en != null)
            {
                int n = 0;
                try { foreach (var it in en) { Walk(it, depth + 1); if (++n > 3000) break; } } catch { }
                return;
            }
        }

        public static void DumpStringFields()
        {
            try
            {
                var t0 = Time.realtimeSinceStartup;
                var objs = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                int scanned = 0;
                foreach (var o in objs)
                {
                    if (o == null) continue;
                    if (!(o is MonoBehaviour) && !(o is ScriptableObject)) continue;
                    scanned++;
                    Type t = o.GetType();
                    for (Type tt = t; tt != null && tt != typeof(object) && tt != typeof(UnityEngine.Object); tt = tt.BaseType)
                    {
                        System.Reflection.FieldInfo[] fs;
                        try { fs = tt.GetFields(flags | System.Reflection.BindingFlags.DeclaredOnly); } catch { continue; }
                        foreach (var f in fs)
                        {
                            object v;
                            try { v = f.GetValue(o); } catch { continue; }
                            Walk(v, 0);
                        }
                    }
                }
                var sb = new StringBuilder();
                foreach (var s in FieldSeen) sb.AppendLine(s.Replace("\r", "").Replace("\n", "\\n"));

                // static fields across Assembly-CSharp (tip lists often live here)
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        string an = asm.GetName().Name;
                        if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass") continue;
                        Type[] types;
                        try { types = asm.GetTypes(); } catch { continue; }
                        foreach (var t in types)
                        {
                            System.Reflection.FieldInfo[] fs;
                            try { fs = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); } catch { continue; }
                            foreach (var f in fs)
                            {
                                object v;
                                try { v = f.GetValue(null); } catch { continue; }
                                Walk(v, 0);
                            }
                        }
                    }
                }
                catch { }

                foreach (var o in objs)
                {
                    var ta = o as TextAsset;
                    if (ta == null) continue;
                    try { Walk(ta.text, 0); } catch { }
                }
                sb.Clear();
                foreach (var s in FieldSeen) sb.AppendLine(s.Replace("\r", "").Replace("\n", "\\n"));
                File.WriteAllText(Path.Combine(Paths.GameRootPath, "nocn_fields.txt"), sb.ToString(), new UTF8Encoding(false));
                if (Inst != null) Inst.Logger.LogInfo("field dump: " + FieldSeen.Count + " strings from " + scanned + " objects in " + Math.Round(Time.realtimeSinceStartup - t0, 2) + "s");
            }
            catch (Exception e) { if (Inst != null) Inst.Logger.LogWarning("field dump fail " + e.Message); }
        }
    }

    public class Rule
    {
        public Regex Rx;
        public string Template;
        public string[] Placeholders;
        public string Token;

        public string Apply(string input)
        {
            var m = Rx.Match(input);
            if (!m.Success) return input;
            string outp = Template;
            for (int i = 0; i < Placeholders.Length; i++)
            {
                string val = m.Groups[i + 1].Value;
                // key glyphs often arrive as "V." — the Chinese sentence already
                // carries its own punctuation, so drop the stray tail.
                if (val.Length > 0 && val.Length < 24) val = val.TrimEnd('.', ' ', '\u00a0');
                // templates may carry a format specifier ("{0:F2}"), which must be
                // replaced as well or the raw placeholder leaks into the UI
                outp = Regex.Replace(outp,
                    "\\{" + Regex.Escape(Placeholders[i]) + "(?::[^}]*)?\\}",
                    val.Replace("$", "$$"));
            }
            return outp;
        }
    }

    [HarmonyPatch]
    public static class PatchLoadFontFace
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(FontEngine), "LoadFontFace", new Type[] { typeof(Font), typeof(int) });
        }
        public static bool Prefix(Font font, int pointSize, ref FontEngineError __result)
        {
            if (CnPlugin.IsFake(font))
            {
                __result = CnPlugin.LoadBytes(pointSize, 0);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class PatchLoadFontFace3
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(FontEngine), "LoadFontFace", new Type[] { typeof(Font), typeof(int), typeof(int) });
        }
        public static bool Prefix(Font font, int pointSize, int faceIndex, ref FontEngineError __result)
        {
            if (CnPlugin.IsFake(font))
            {
                __result = CnPlugin.LoadBytes(pointSize, faceIndex);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    public static class PatchSetText
    {
        public static void Prefix(TMP_Text __instance, ref string value)
        {
            if (CnPlugin.DebugMode) CnPlugin.Record(__instance, value);
            if (CnPlugin.FontMode == 1) CnPlugin.EnsureFont(__instance);
            if (value != null)
            {
                string zh = CnPlugin.Translate(value);
                if (CnPlugin.FontMode == 2 && !string.Equals(zh, value) && CnPlugin.HasCjk(zh)) CnPlugin.EnsureFont(__instance);
                value = zh;
            }
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "SetText")]
    [HarmonyPatch(new Type[] { typeof(string), typeof(bool) })]
    public static class PatchSetTextStr
    {
        public static void Prefix(TMP_Text __instance, ref string sourceText)
        {
            if (CnPlugin.DebugMode) CnPlugin.Record(__instance, sourceText);
            if (CnPlugin.FontMode == 1) CnPlugin.EnsureFont(__instance);
            if (sourceText != null)
            {
                string zh2 = CnPlugin.Translate(sourceText);
                if (CnPlugin.FontMode == 2 && !string.Equals(zh2, sourceText) && CnPlugin.HasCjk(zh2)) CnPlugin.EnsureFont(__instance);
                sourceText = zh2;
            }
        }
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    public static class PatchOnEnableUGUI
    {
        public static void Postfix(TextMeshProUGUI __instance)
        {
            try
            {
                if (CnPlugin.FontMode == 1) CnPlugin.EnsureFont(__instance);
                string t = __instance.text;
                if (CnPlugin.DebugMode) CnPlugin.Record(__instance, t);
                string zh = CnPlugin.Translate(t);
                if (!string.Equals(zh, t)) { if (CnPlugin.HasCjk(zh) && CnPlugin.FontMode > 0) CnPlugin.EnsureFont(__instance); __instance.text = zh; }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
    public static class PatchOnEnableTMP
    {
        public static void Postfix(TextMeshPro __instance)
        {
            try
            {
                if (CnPlugin.FontMode == 1) CnPlugin.EnsureFont(__instance);
                string t = __instance.text;
                if (CnPlugin.DebugMode) CnPlugin.Record(__instance, t);
                string zh = CnPlugin.Translate(t);
                if (!string.Equals(zh, t)) { if (CnPlugin.HasCjk(zh) && CnPlugin.FontMode > 0) CnPlugin.EnsureFont(__instance); __instance.text = zh; }
            }
            catch { }
        }
    }

    public class Ticker : MonoBehaviour
    {
        float next;
        void Start()
        {
            StartCoroutine(Boot());
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            StartCoroutine(Boot());
        }
        System.Collections.IEnumerator Boot()
        {
            yield return new WaitForSeconds(0.5f);
            CnPlugin.TranslateAll();
            yield return new WaitForSeconds(2f);
            CnPlugin.TranslateAll();
            if (CnPlugin.Bench) RunBench();
        }

        void RunBench()
        {
            try
            {
                var keys = new List<string>();
                foreach (var k in CnPlugin.Map.Keys) { if (keys.Count >= 1000) break; keys.Add(k); }
                if (keys.Count == 0) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int n = 0;
                for (int round = 0; round < 20; round++)
                {
                    for (int i = 0; i < keys.Count; i++) { CnPlugin.Translate(keys[i]); n++; }
                }
                sw.Stop();
                double usHit = sw.Elapsed.TotalMilliseconds * 1000.0 / n;

                string[] miss = { "SOME UNTRANSLATED TEXT 12345", "Engine 3 fire", "12:34:56", "650 km/h", "AGM-88 HARM" };
                sw.Restart();
                int m = 0;
                for (int round = 0; round < 5000; round++)
                {
                    for (int i = 0; i < miss.Length; i++) { CnPlugin.Translate(miss[i]); m++; }
                }
                sw.Stop();
                double usMiss = sw.Elapsed.TotalMilliseconds * 1000.0 / m;

                BepInEx.Logging.Logger.CreateLogSource("NOCN").LogInfo(
                    "bench: hit=" + Math.Round(usHit, 3) + "us/call, miss=" + Math.Round(usMiss, 3) + "us/call, entries=" + CnPlugin.Map.Count);
            }
            catch (Exception e) { BepInEx.Logging.Logger.CreateLogSource("NOCN").LogWarning("bench fail " + e.Message); }
        }
        void Update()
        {
            if (CnPlugin.FpsLog)
            {
                fpsAcc += Time.unscaledDeltaTime;
                fpsFrames++;
                if (fpsAcc >= 5f)
                {
                    double fps = fpsFrames / fpsAcc;
                    BepInEx.Logging.Logger.CreateLogSource("NOCN").LogInfo("fps=" + Math.Round(fps, 1) + " frame_ms=" + Math.Round(1000.0 / fps, 2));
                    fpsAcc = 0; fpsFrames = 0;
                }
            }
            if (CnPlugin.DebugMode && !fieldDumped && Time.unscaledTime > 12f)
            {
                fieldDumped = true;
                CnPlugin.DumpStringFields();
            }
            if (Time.unscaledTime > next)
            {
                next = Time.unscaledTime + 15f;
                if (CnPlugin.DebugMode && CnPlugin.SweepOn) CnPlugin.Sweep();
            }
            if (CnPlugin.SweepOn) CnPlugin.SweepSlice();
        }
        bool fieldDumped;
        float fpsAcc;
        int fpsFrames;
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "set_text")]
    public static class PatchLegacySetText
    {
        public static void Prefix(UnityEngine.UI.Text __instance, ref string value)
        {
            if (value == null) return;
            string zh = CnPlugin.Translate(value);
            if (!string.Equals(zh, value))
            {
                if (CnPlugin.HasCjk(zh)) CnPlugin.EnsureLegacyFont(__instance);
                value = zh;
            }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "OnEnable")]
    public static class PatchLegacyOnEnable
    {
        public static void Postfix(UnityEngine.UI.Text __instance)
        {
            CnPlugin.TranslateLegacy(__instance);
        }
    }
}
