using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
#endif

namespace AI.Context
{
    public enum ConsoleMessageType
    {
        Log,
        Warning,
        Error
    }

    /// <summary>
    /// Unity Console durumunu temsil eder ve modele verilecek kompakt hata özetini üretir.
    ///
    /// Tasarým kararlarý:
    /// - Halka tampon (ring buffer): 150 adýmlýk ReAct döngüsünde mesaj listesi sýnýrsýz
    ///   büyüyordu; artýk en yeni N mesaj tutulur.
    /// - Dedupe: ayný derleme hatasýnýn 40 kopyasý yerine tek satýr + tekrar sayýsý.
    /// - StackTrace tam saklanmaz; sadece kullanýcý kodundaki ilk kare (dosya:satýr).
    /// - GetSummary() artýk sadece sayý deðil, hatanýn METNÝNÝ de veriyor. Eski hâlinde
    ///   model "Errors: 3" görüyordu ve hiçbir þey düzeltemiyordu.
    /// </summary>
    [Serializable]
    public class ConsoleContext
    {
        public const int DefaultCapacity = 200;

        // ==============================
        // COUNTS
        // ==============================

        public int ErrorCount;
        public int WarningCount;
        public int LogCount;

        /// <summary>Kapasite aþýmý nedeniyle düþürülmüþ mesaj oldu mu.</summary>
        public bool Truncated;

        /// <summary>Halka tamponun kapasitesi. 0 veya altý = sýnýrsýz (önerilmez).</summary>
        public int Capacity = DefaultCapacity;

        // ==============================
        // MESSAGES
        // ==============================

        public List<ConsoleMessage> Messages = new List<ConsoleMessage>();

        /// <summary>Signature -> mesaj, dedupe için.</summary>
        [NonSerialized]
        private Dictionary<string, ConsoleMessage> _bySignature =
            new Dictionary<string, ConsoleMessage>(StringComparer.Ordinal);

        // ==============================
        // ADD MESSAGE
        // ==============================

        public void AddMessage(ConsoleMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Message))
                return;

            EnsureIndex();

            if (message.Time == default(DateTime))
                message.Time = DateTime.Now;

            if (string.IsNullOrEmpty(message.Signature))
                message.Signature = ConsoleMessage.BuildSignature(message);

            // Ayný hata tekrar geldiyse yeni satýr açma, sayacý artýr. Derleme hatalarý
            // her yeniden derlemede aynen tekrar geldiði için bu tek baþýna büyük tasarruf.
            if (_bySignature.TryGetValue(message.Signature, out ConsoleMessage existing))
            {
                existing.Occurrences++;
                existing.Time = message.Time;
                return;
            }

            Messages.Add(message);
            _bySignature[message.Signature] = message;

            switch (message.Type)
            {
                case ConsoleMessageType.Error:
                    ErrorCount++;
                    break;
                case ConsoleMessageType.Warning:
                    WarningCount++;
                    break;
                default:
                    LogCount++;
                    break;
            }

            TrimToCapacity();
        }

        public void AddMessage(
            string message,
            ConsoleMessageType type,
            string stackTrace = null,
            string file = null,
            int line = 0)
        {
            AddMessage(new ConsoleMessage
            {
                Message = message,
                Type = type,
                StackTrace = ConsoleMessage.CondenseStackTrace(stackTrace),
                File = file,
                Line = line,
                Time = DateTime.Now
            });
        }

        private void TrimToCapacity()
        {
            if (Capacity <= 0 || Messages.Count <= Capacity)
                return;

            // Kýrparken önce Log'larý at, Error'larý en sona sakla: baðlam dolduðunda
            // feda edilecek son þey hata mesajýdýr.
            int overflow = Messages.Count - Capacity;

            for (int pass = 0; pass < 3 && overflow > 0; pass++)
            {
                ConsoleMessageType target = pass == 0 ? ConsoleMessageType.Log
                                          : pass == 1 ? ConsoleMessageType.Warning
                                          : ConsoleMessageType.Error;

                for (int i = 0; i < Messages.Count && overflow > 0;)
                {
                    if (Messages[i].Type == target)
                    {
                        _bySignature.Remove(Messages[i].Signature);
                        Messages.RemoveAt(i);
                        overflow--;
                        Truncated = true;
                    }
                    else
                    {
                        i++;
                    }
                }
            }

            RecountFromMessages();
        }

        private void RecountFromMessages()
        {
            ErrorCount = 0;
            WarningCount = 0;
            LogCount = 0;

            foreach (ConsoleMessage m in Messages)
            {
                switch (m.Type)
                {
                    case ConsoleMessageType.Error: ErrorCount++; break;
                    case ConsoleMessageType.Warning: WarningCount++; break;
                    default: LogCount++; break;
                }
            }
        }

        private void EnsureIndex()
        {
            if (_bySignature != null)
                return;

            // [NonSerialized] alan domain reload sonrasý null döner; yeniden kuruyoruz.
            _bySignature = new Dictionary<string, ConsoleMessage>(StringComparer.Ordinal);

            foreach (ConsoleMessage m in Messages)
            {
                if (m == null)
                    continue;

                if (string.IsNullOrEmpty(m.Signature))
                    m.Signature = ConsoleMessage.BuildSignature(m);

                _bySignature[m.Signature] = m;
            }
        }

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            Messages.Clear();
            EnsureIndex();
            _bySignature.Clear();

            ErrorCount = 0;
            WarningCount = 0;
            LogCount = 0;
            Truncated = false;
        }

        /// <summary>Sadece hatalarý temizler (rollback sonrasý kullanýþlý).</summary>
        public void ClearErrors()
        {
            EnsureIndex();

            foreach (ConsoleMessage m in Messages.Where(x => x.Type == ConsoleMessageType.Error).ToList())
            {
                _bySignature.Remove(m.Signature);
                Messages.Remove(m);
            }

            RecountFromMessages();
        }

        public bool HasErrors => ErrorCount > 0;

        public IEnumerable<ConsoleMessage> Errors =>
            Messages.Where(m => m.Type == ConsoleMessageType.Error);

        // ==============================
        // RENDERING
        // ==============================

        /// <summary>
        /// Geriye dönük uyumlu özet. Artýk sadece sayýlarý deðil, hata metinlerini de
        /// içeriyor - modelin hatayý düzeltebilmesi için gereken asgari bilgi.
        /// </summary>
        public string GetSummary()
        {
            return Render(maxErrors: 5, maxWarnings: 2, charBudget: 0);
        }

        /// <summary>
        /// Baðlam bütçesine sýðan hata raporu. Önce hatalar, sonra uyarýlar; loglar
        /// yalnýzca hiç hata yoksa ve bütçe artarsa.
        /// </summary>
        public string Render(int maxErrors = 5, int maxWarnings = 2, int charBudget = 0)
        {
            if (Messages.Count == 0)
                return "Console: temiz (hata/uyari yok).";

            var sb = new StringBuilder();

            sb.Append("Console: ").Append(ErrorCount).Append(" error, ")
              .Append(WarningCount).Append(" warning, ")
              .Append(LogCount).Append(" log");

            if (Truncated)
                sb.Append(" (liste kirpildi)");

            sb.AppendLine();

            AppendGroup(sb, ConsoleMessageType.Error, "ERROR", maxErrors);
            AppendGroup(sb, ConsoleMessageType.Warning, "WARN", maxWarnings);

            string result = sb.ToString();

            if (charBudget > 0 && result.Length > charBudget)
            {
                result = result.Substring(0, Math.Max(0, charBudget - 30)).TrimEnd()
                         + "\n... (baglam butcesi doldu)";
            }

            return result;
        }

        private void AppendGroup(StringBuilder sb, ConsoleMessageType type, string label, int max)
        {
            if (max <= 0)
                return;

            List<ConsoleMessage> group = Messages
                .Where(m => m.Type == type)
                .OrderByDescending(m => m.Time)
                .Take(max)
                .ToList();

            foreach (ConsoleMessage m in group)
            {
                sb.Append("  ").Append(label).Append(": ").Append(m.ShortMessage());

                if (!string.IsNullOrEmpty(m.File))
                {
                    sb.Append("  @ ").Append(FileNameOnly(m.File));

                    if (m.Line > 0)
                        sb.Append(':').Append(m.Line);
                }
                else if (!string.IsNullOrEmpty(m.StackTrace))
                {
                    sb.Append("  @ ").Append(m.StackTrace);
                }

                if (m.Occurrences > 1)
                    sb.Append("  (x").Append(m.Occurrences).Append(')');

                sb.AppendLine();
            }

            int total = Messages.Count(m => m.Type == type);
            if (total > group.Count)
                sb.Append("  ... (+").Append(total - group.Count).Append(' ').Append(label).AppendLine(" daha)");
        }

        private static string FileNameOnly(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            int slash = path.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }

    // =====================================
    // MESSAGE MODEL
    // =====================================

    [Serializable]
    public class ConsoleMessage
    {
        public string Message;

        /// <summary>Sadece anlamlý ilk kare. Tam stack trace saklanmaz (mesaj baþýna kilobaytlar).</summary>
        public string StackTrace;

        public ConsoleMessageType Type;
        public DateTime Time;

        /// <summary>Derleme hatalarýnda kaynak dosya yolu.</summary>
        public string File;

        /// <summary>Derleme hatalarýnda satýr numarasý.</summary>
        public int Line;

        /// <summary>Ayný mesajýn kaç kez geldiði.</summary>
        public int Occurrences = 1;

        /// <summary>Dedupe anahtarý.</summary>
        public string Signature;

        private const int MaxMessageLength = 240;

        public string ShortMessage()
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;

            // Çok satýrlý mesajlarý tek satýra indir; konsol formatý token yiyor.
            string flat = Message.Replace("\r", " ").Replace("\n", " ").Trim();
            flat = Regex.Replace(flat, @"\s{2,}", " ");

            return flat.Length <= MaxMessageLength
                ? flat
                : flat.Substring(0, MaxMessageLength) + "…";
        }

        public static string BuildSignature(ConsoleMessage message)
        {
            if (message == null)
                return string.Empty;

            string core = message.ShortMessage();

            // Sayýlarý normalize et: "Object #4821 destroyed" ile "#5017" ayný hatadýr.
            core = Regex.Replace(core, @"\d+", "#");

            return message.Type + "|" + FileKey(message.File) + "|" + core;
        }

        private static string FileKey(string file)
        {
            return string.IsNullOrEmpty(file) ? string.Empty : file;
        }

        /// <summary>
        /// Stack trace'ten yalnýzca kullanýcý kodundaki ilk kareyi çýkarýr.
        /// UnityEngine/System kareleri modele hiçbir þey söylemiyor, sadece token yiyor.
        /// </summary>
        public static string CondenseStackTrace(string stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
                return null;

            string[] lines = stackTrace.Split('\n');

            foreach (string raw in lines)
            {
                string line = raw.Trim();

                if (line.Length == 0)
                    continue;

                if (line.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                    line.StartsWith("UnityEditor.", StringComparison.Ordinal) ||
                    line.StartsWith("System.", StringComparison.Ordinal))
                {
                    continue;
                }

                return line.Length > 160 ? line.Substring(0, 160) + "…" : line;
            }

            string first = lines.Length > 0 ? lines[0].Trim() : null;
            return string.IsNullOrEmpty(first) ? null
                 : (first.Length > 160 ? first.Substring(0, 160) + "…" : first);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editör tarafý toplayýcý.
    ///
    /// Ýki kaynak kullanýr:
    /// 1. Application.logMessageReceived - çalýþma zamaný ve editör loglarý.
    /// 2. CompilationPipeline.assemblyCompilationFinished - DERLEME hatalarý. Bu, kamuya
    ///    açýk ve güvenilir API'dir; UnityEditor.LogEntries gibi internal API'lere
    ///    reflection ile girmeye gerek yoktur.
    ///
    /// Derleme hatalarý domain reload'dan ÖNCE gelir, o yüzden SessionState'e yazýlýr ve
    /// reload sonrasý geri okunur. GeneratedScriptWatchdog'un ihtiyacý olan tam da budur:
    /// rollback yerine hatayý modele verip düzelttirebilirsiniz.
    /// </summary>
    [InitializeOnLoad]
    public static class ConsoleContextRecorder
    {
        private const string SessionKey = "AI.Context.CompileErrors";
        private const int MaxStoredCompileErrors = 20;

        private static readonly ConsoleContext Buffer = new ConsoleContext { Capacity = 300 };
        private static readonly object Gate = new object();

        static ConsoleContextRecorder()
        {
            Application.logMessageReceived -= OnLogMessage;
            Application.logMessageReceived += OnLogMessage;

            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;

            RestoreCompileErrors();
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            ConsoleMessageType mapped;

            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    mapped = ConsoleMessageType.Error;
                    break;
                case LogType.Warning:
                    mapped = ConsoleMessageType.Warning;
                    break;
                default:
                    mapped = ConsoleMessageType.Log;
                    break;
            }

            lock (Gate)
            {
                Buffer.AddMessage(condition, mapped, stackTrace);
            }
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null || messages.Length == 0)
                return;

            var persisted = new List<string>();

            lock (Gate)
            {
                foreach (CompilerMessage cm in messages)
                {
                    ConsoleMessageType type = cm.type == CompilerMessageType.Error
                        ? ConsoleMessageType.Error
                        : ConsoleMessageType.Warning;

                    Buffer.AddMessage(cm.message, type, null, cm.file, cm.line);

                    if (type == ConsoleMessageType.Error && persisted.Count < MaxStoredCompileErrors)
                        persisted.Add(Encode(cm));
                }
            }

            if (persisted.Count > 0)
            {
                // Domain reload bellek içi tamponu siler; derleme hatalarýný taþýyoruz.
                string existing = SessionState.GetString(SessionKey, string.Empty);
                string combined = string.IsNullOrEmpty(existing)
                    ? string.Join("\u241E", persisted)
                    : existing + "\u241E" + string.Join("\u241E", persisted);

                SessionState.SetString(SessionKey, combined);
            }
        }

        private static string Encode(CompilerMessage cm)
        {
            string message = (cm.message ?? string.Empty).Replace("\u241F", " ").Replace("\u241E", " ");
            return message + "\u241F" + (cm.file ?? string.Empty) + "\u241F" + cm.line;
        }

        private static void RestoreCompileErrors()
        {
            string stored = SessionState.GetString(SessionKey, string.Empty);

            if (string.IsNullOrEmpty(stored))
                return;

            foreach (string entry in stored.Split('\u241E'))
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                string[] parts = entry.Split('\u241F');
                if (parts.Length < 3)
                    continue;

                int.TryParse(parts[2], out int line);

                lock (Gate)
                {
                    Buffer.AddMessage(parts[0], ConsoleMessageType.Error, null, parts[1], line);
                }
            }
        }

        /// <summary>Toplanan konsol durumunun anlýk kopyasýný verir.</summary>
        public static ConsoleContext Snapshot()
        {
            lock (Gate)
            {
                var copy = new ConsoleContext { Capacity = Buffer.Capacity };

                foreach (ConsoleMessage m in Buffer.Messages)
                {
                    copy.AddMessage(new ConsoleMessage
                    {
                        Message = m.Message,
                        StackTrace = m.StackTrace,
                        Type = m.Type,
                        Time = m.Time,
                        File = m.File,
                        Line = m.Line,
                        Occurrences = m.Occurrences,
                        Signature = m.Signature
                    });
                }

                copy.Truncated = Buffer.Truncated;
                return copy;
            }
        }

        /// <summary>Yeni bir göreve baþlarken çaðýrýn: eski hatalar modele karýþmasýn.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Buffer.Clear();
            }

            SessionState.EraseString(SessionKey);
        }
    }
#endif
}