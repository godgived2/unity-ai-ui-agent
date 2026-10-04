using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEngine;

namespace MCPForUnity.Editor.AI
{
    /// <summary>
    /// Native MCP Unity Client
    ///
    /// AI Layer -> MCPUnityClient -> TransportCommandDispatcher
    ///   -> CommandRegistry -> MCP Tools
    ///
    /// NOTE: The dispatcher expects commands shaped as:
    ///   { "type": "&lt;tool_name&gt;", "params": { ... } }
    ///
    /// ============ NEDEN REFLECTION KULLANIYORUZ ============
    /// TransportCommandDispatcher, MCP for Unity paketinin bir sürümünde 'public'
    /// iken sonraki bir sürümde 'internal' yapýldý. Doðrudan çaðrý o andan itibaren
    /// derlenmiyor:
    ///
    ///   error CS0122: 'TransportCommandDispatcher' is inaccessible
    ///                 due to its protection level
    ///
    /// Bu bizim kodumuzun hatasý deðil - paketin dýþ yüzeyi deðiþti. Paketi eski bir
    /// sürüme sabitlemek çözüm gibi görünse de kýrýlgan: paket her güncellendiðinde
    /// ayný sorun geri gelir ve projeyi baþka bir makinede açan kiþi de ayný duvara
    /// çarpar.
    ///
    /// Reflection ile çaðýrmak bu baðýmlýlýðý derleme zamanýndan ÇALIÞMA zamanýna
    /// taþýyor: eriþim belirteci ne olursa olsun metot bulunur ve çaðrýlýr. Metot
    /// gerçekten kaybolursa, derleme kýrýlmak yerine anlaþýlýr bir hata mesajý
    /// veriyoruz.
    ///
    /// Maliyeti: ilk çaðrýda bir tip aramasý (sonuç önbelleðe alýnýyor) ve çaðrý
    /// baþýna bir Invoke. Bu katmanda çaðrý sýklýðý saniyede birkaç taneyi geçmiyor,
    /// yani ölçülebilir bir yavaþlama yok.
    /// =======================================================
    /// </summary>
    public sealed class MCPUnityClient
    {
        private static MCPUnityClient _instance;

        public static MCPUnityClient Instance =>
            _instance ??= new MCPUnityClient();

        private readonly TransportManager _transportManager;

        // Cached connection state — avoids sync-over-async deadlocks on the Editor main thread.
        private volatile bool _cachedConnected;
        private DateTime _lastVerifiedUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

        private MCPUnityClient()
        {
            _transportManager = MCPServiceLocator.TransportManager;
        }

        public static MCPUnityClient GetClient() => Instance;

        // =====================================================
        // TRANSPORT DISPATCH (REFLECTION BRIDGE)
        // =====================================================

        private const string DispatcherTypeName =
            "MCPForUnity.Editor.Services.Transport.TransportCommandDispatcher";

        private const string DispatchMethodName = "ExecuteCommandJsonAsync";

        // Bir kez çözülüp önbelleðe alýnýyor. Domain reload'da sýfýrlanýr, sorun deðil.
        private static MethodInfo _dispatchMethod;
        private static object _dispatchTarget;
        private static bool _dispatchResolved;

        /// <summary>
        /// TransportCommandDispatcher.ExecuteCommandJsonAsync metodunu bulur.
        ///
        /// Üç aþamalý arama yapýyor çünkü paket hem tip adýný hem namespace'i hem de
        /// metodun static/instance oluþunu sürümler arasýnda deðiþtirebiliyor:
        ///   1. Tam nitelikli ad ile doðrudan arama (en hýzlý yol)
        ///   2. Tüm assembly'lerde basit ada göre arama (namespace deðiþtiyse)
        ///   3. Metot instance ise, tipin Instance/Default singleton'ýný bulma
        /// </summary>
        private static void ResolveDispatchMethod()
        {
            if (_dispatchResolved)
                return;

            _dispatchResolved = true;

            Type dispatcherType = FindType(DispatcherTypeName, "TransportCommandDispatcher");

            if (dispatcherType == null)
            {
                Debug.LogError(
                    "[MCP CLIENT] TransportCommandDispatcher type could not be found. " +
                    "The MCP for Unity package may have been updated in a way that removed it. " +
                    "Check Window > MCP for Unity and the package version in Packages/manifest.json.");
                return;
            }

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance;

            // Ayný adda birden fazla overload olabilir; (string, CancellationToken)
            // imzasýný tercih ediyoruz, yoksa (string) yeterli.
            MethodInfo best = null;

            foreach (MethodInfo m in dispatcherType.GetMethods(flags))
            {
                if (!string.Equals(m.Name, DispatchMethodName, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] ps = m.GetParameters();

                if (ps.Length == 0 || ps[0].ParameterType != typeof(string))
                    continue;

                if (ps.Length == 2 && ps[1].ParameterType == typeof(CancellationToken))
                {
                    best = m;
                    break;
                }

                if (ps.Length == 1 && best == null)
                {
                    best = m;
                }
            }

            if (best == null)
            {
                Debug.LogError(
                    $"[MCP CLIENT] '{DispatchMethodName}' was not found on {dispatcherType.FullName}. " +
                    "The MCP for Unity package API has changed.");
                return;
            }

            _dispatchMethod = best;

            if (best.IsStatic)
                return;

            // Metot instance ise bir örnek bulmamýz gerekiyor.
            _dispatchTarget = FindSingletonInstance(dispatcherType);

            if (_dispatchTarget == null)
            {
                Debug.LogError(
                    $"[MCP CLIENT] {dispatcherType.FullName}.{DispatchMethodName} is an instance method " +
                    "but no singleton instance could be located.");
                _dispatchMethod = null;
            }
        }

        /// <summary>
        /// Tipi önce tam nitelikli adla, bulamazsa tüm yüklü assembly'lerde basit adla
        /// arar. Basit ad aramasý pahalý ama yalnýzca bir kez yapýlýyor.
        /// </summary>
        private static Type FindType(string fullName, string simpleName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null)
                    return t;
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;

                try
                {
                    types = asm.GetTypes();
                }
                catch
                {
                    // Bazý assembly'ler yansýtýlamaz (yükleme hatasý olanlar). Atla.
                    continue;
                }

                foreach (Type t in types)
                {
                    if (string.Equals(t.Name, simpleName, StringComparison.Ordinal))
                        return t;
                }
            }

            return null;
        }

        /// <summary>
        /// Bir tipin singleton örneðini yaygýn isimlerden bulmaya çalýþýr; bulamazsa
        /// parametresiz kurucu ile yeni bir örnek üretir.
        /// </summary>
        private static object FindSingletonInstance(Type type)
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            string[] candidates = { "Instance", "Default", "Current", "Shared" };

            foreach (string name in candidates)
            {
                PropertyInfo prop = type.GetProperty(name, flags);
                if (prop != null)
                {
                    object value = prop.GetValue(null);
                    if (value != null)
                        return value;
                }

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    if (value != null)
                        return value;
                }
            }

            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Komut JSON'unu dispatcher'a gönderir ve sonucu string olarak döndürür.
        ///
        /// Dispatcher'ýn dönüþ tipi Task&lt;string&gt; olabileceði gibi düz Task ya da
        /// doðrudan string de olabilir - üçü de ele alýnýyor.
        /// </summary>
        private static async Task<string> DispatchAsync(string payload, CancellationToken cancellationToken)
        {
            ResolveDispatchMethod();

            if (_dispatchMethod == null)
            {
                throw new Exception(
                    "MCP transport dispatcher is unavailable. The MCP for Unity package API has changed - " +
                    "see the Console for details.");
            }

            ParameterInfo[] ps = _dispatchMethod.GetParameters();

            object[] args = ps.Length >= 2
                ? new object[] { payload, cancellationToken }
                : new object[] { payload };

            object raw;

            try
            {
                raw = _dispatchMethod.Invoke(_dispatchTarget, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Reflection, gerçek hatayý TargetInvocationException içine sarar.
                // Sarmalayýcýyý açmazsak Console'da anlamsýz bir yýðýn izi görünür.
                throw tie.InnerException;
            }

            switch (raw)
            {
                case Task<string> typedTask:
                    return await typedTask;

                case Task plainTask:
                    {
                        await plainTask;

                        PropertyInfo resultProp = plainTask.GetType().GetProperty("Result");
                        return resultProp?.GetValue(plainTask)?.ToString() ?? string.Empty;
                    }

                default:
                    return raw?.ToString() ?? string.Empty;
            }
        }

        // =====================================================
        // CONNECTION
        // =====================================================

        public async Task<bool> ConnectAsync()
        {
            try
            {
                bool result = await _transportManager.StartAsync(TransportMode.Stdio);

                _cachedConnected = result;
                _lastVerifiedUtc = DateTime.UtcNow;

                if (result)
                    Debug.Log("MCP STDIO Connected");
                else
                    Debug.LogError("MCP STDIO Connection Failed");

                return result;
            }
            catch (Exception ex)
            {
                _cachedConnected = false;
                _lastVerifiedUtc = DateTime.UtcNow;

                Debug.LogError($"MCP Connect Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> Connect() => await ConnectAsync();

        public async Task<bool> IsConnectedAsync()
        {
            try
            {
                bool connected = await _transportManager.VerifyAsync(TransportMode.Stdio);

                _cachedConnected = connected;
                _lastVerifiedUtc = DateTime.UtcNow;

                return connected;
            }
            catch
            {
                _cachedConnected = false;
                _lastVerifiedUtc = DateTime.UtcNow;
                return false;
            }
        }

        /// <summary>
        /// Non-blocking connection check for UI / legacy sync callers.
        ///
        /// IMPORTANT: this returns the CACHED state and never blocks. The old
        /// implementation used IsConnectedAsync().GetAwaiter().GetResult(), which can
        /// deadlock and freeze the Unity Editor when called from the main thread.
        ///
        /// If the cache is stale, a refresh is kicked off in the background and the
        /// last known value is returned immediately.
        /// </summary>
        public bool IsConnected()
        {
            if (DateTime.UtcNow - _lastVerifiedUtc > CacheTtl)
            {
                // Fire-and-forget refresh; never block the caller.
                _ = RefreshConnectionStateAsync();
            }

            return _cachedConnected;
        }

        private async Task RefreshConnectionStateAsync()
        {
            try
            {
                await IsConnectedAsync();
            }
            catch
            {
                // Best-effort only.
            }
        }

        public TransportState GetState()
        {
            return _transportManager.GetState(TransportMode.Stdio);
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (await IsConnectedAsync())
                return true;

            return await ConnectAsync();
        }

        // =====================================================
        // TOOL EXECUTION
        // =====================================================

        /// <summary>
        /// Calls a tool by name with parameters.
        /// Builds the {"type": ..., "params": ...} envelope the dispatcher expects.
        /// </summary>
        public async Task<string> CallToolAsync(
            string toolName,
            JObject parameters,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name cannot be empty", nameof(toolName));

            Debug.Log("[MCP CLIENT] CallTool: " + toolName);

            if (!await EnsureConnectedAsync())
                throw new Exception("MCP STDIO bridge is not connected");

            // FIXED: was { command = toolName, @params = ... }.
            // TransportCommandDispatcher deserializes into Command { type, params },
            // so "command" was never read and every call failed validation.
            var command = new JObject
            {
                ["type"] = toolName,
                ["params"] = parameters ?? new JObject()
            };

            string payload = command.ToString(Formatting.None);
            Debug.Log("[MCP CLIENT] PAYLOAD: " + payload);

            return await DispatchAsync(payload, cancellationToken);
        }

        /// <summary>
        /// Sends a pre-built command JSON straight through, without adding an envelope.
        /// The JSON must already be {"type": ..., "params": {...}}.
        /// This is what MCPExecutor uses.
        /// </summary>
        public async Task<string> ExecuteRawAsync(
            string json,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Command JSON empty", nameof(json));

            if (!await EnsureConnectedAsync())
                throw new Exception("MCP connection failed");

            return await DispatchAsync(json, cancellationToken);
        }

        // =====================================================
        // COMPATIBILITY
        // =====================================================

        public async Task<string> SendRequest(
            string toolName,
            JObject parameters,
            CancellationToken cancellationToken = default)
        {
            return await CallToolAsync(toolName, parameters, cancellationToken);
        }

        // NOTE: the old SendRequest(string, CancellationToken) overload was REMOVED.
        // It was the source of a silent bug: MCPExecutor called
        // SendRequest(json, ct) and C# resolved it to that overload, passing the whole
        // JSON command as the tool NAME and dropping all parameters.
        // Use ExecuteRawAsync(json, ct) for raw JSON, or
        // SendRequest(toolName, parameters, ct) for name+params.

        public async Task Disconnect()
        {
            await _transportManager.StopAsync(TransportMode.Stdio);

            _cachedConnected = false;
            _lastVerifiedUtc = DateTime.UtcNow;
        }
    }
}