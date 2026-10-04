#if UNITY_EDITOR
using System;
using UnityEditor;

namespace AI.Core
{
    /// <summary>
    /// AI servislerini AIServiceLocator'a kaydeder.
    ///
    /// ============ NEDEN BU DOSYA VAR ============
    /// AIServiceLocator.Register PROJEDE HÝÇBÝR YERDEN ÇAÐRILMIYORDU. Locator
    /// kusursuz çalýþýyordu, sadece içi boþtu.
    ///
    /// Sonuç zinciri, her adýmda sessiz:
    ///   1. ContextBuilder.ResolveServices üç servisi de bulamýyor
    ///   2. Konsola "Service Not Found" uyarýsý düþüyor - ama LogWarning, hata deðil
    ///   3. ContextBuilder null servislerle devam ediyor
    ///   4. <world_state> bloðu boþ ya da eksik geliyor
    ///   5. MODEL SAHNEDE NE OLDUÐUNU GÖREMÝYOR
    ///
    /// Beþinci adým pahalý olan. Gerçek testte model 'BottomCommandBar'ý 12 kez
    /// üst üste 'TopStatusBar'ýn üstüne koymaya çalýþtý ve görev erken bitti. Model
    /// aptal olduðu için deðil - orada zaten bir panel olduðunu göremediði için.
    ///
    /// Tek bir çalýþmada bu uyarý 118 kez düþtü: her adýmda ContextBuilder yeniden
    /// kuruluyor ve üç servisi de yeniden arýyor.
    /// ============================================
    ///
    /// DOMAIN RELOAD: AIServiceLocator'ýn sözlüðü STATÝK, yani her derlemede ve her
    /// play-mode geçiþinde siliniyor. [InitializeOnLoadMethod] reload sonrasý
    /// otomatik çalýþtýðý için kayýt kendini yeniliyor.
    /// </summary>
    public static class AIServiceBootstrap
    {
        private static bool _registered;

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            // delayCall þart: InitializeOnLoadMethod, asset veritabaný henüz hazýr
            // deðilken çalýþabiliyor. Servislerden biri kurucusunda sahneye veya
            // asset'lere bakýyorsa erken çaðrý sessizce boþ sonuç üretir.
            EditorApplication.delayCall += RegisterAll;
        }

        /// <summary>
        /// Servisleri kaydeder. Birden fazla kez çaðrýlmasý güvenli.
        ///
        /// Her servis AYRI AYRI try/catch içinde: biri kurucusunda patlarsa diðer
        /// ikisi yine kaydedilsin. Hepsini tek bloða koymak, tek bir arýzalý servisin
        /// bütün baðlamý düþürmesi demekti.
        /// </summary>
        public static void RegisterAll()
        {
            if (_registered)
                return;

            _registered = true;

            int ok = 0;

            ok += TryRegister<AI.Services.UnityContextService>() ? 1 : 0;
            ok += TryRegister<AI.Services.UnityProjectScanner>() ? 1 : 0;
            ok += TryRegister<AI.Services.UnityToolDiscovery>() ? 1 : 0;

            if (ok == 3)
            {
                AIConstants.Log("All context services registered - the model can see the scene.");
            }
            else
            {
                AIConstants.LogError(
                    $"Only {ok}/3 context services registered. The <world_state> block will be incomplete, " +
                    "which means the model cannot see what already exists in the scene and will place " +
                    "elements on top of each other. Check the errors above.");
            }
        }

        /// <summary>
        /// Tek bir servisi kaydeder.
        ///
        /// Parametresiz kurucu bekleniyor. Servislerden biri kurucu argümaný
        /// istiyorsa burasý derlenmez - o durumda o satýrý elle, doðru argümanlarla
        /// yazýn; kalan iki servis etkilenmez.
        /// </summary>
        private static bool TryRegister<T>() where T : class, new()
        {
            if (AIServiceLocator.Has<T>())
                return true;

            try
            {
                AIServiceLocator.Register(new T());
                return true;
            }
            catch (Exception ex)
            {
                AIConstants.LogError(
                    $"Could not create {typeof(T).Name}: {ex.Message}. " +
                    "Anything that depends on this service will degrade silently, so fix this rather than ignoring it.");
                return false;
            }
        }

        /// <summary>
        /// Kaydý sýfýrlar ve yeniden kurar. Servisleri düzenledikten sonra editörü
        /// kapatmadan yenilemek için: Tools > AI Agent > Re-register Services.
        /// </summary>
        [MenuItem("Tools/AI Agent/Re-register Services")]
        public static void ForceReregister()
        {
            AIServiceLocator.Clear();
            _registered = false;
            RegisterAll();

            AIConstants.Log(
                "Registered services: " +
                string.Join(", ", AIServiceLocator.GetRegisteredServices()));
        }
    }
}
#endif