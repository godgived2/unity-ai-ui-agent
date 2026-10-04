using System.IO;
using UnityEditor;
using UnityEngine;

namespace UI.EditorTools
{
    /// <summary>
    /// Yuvarlatýlmýþ köþeli, 9-slice'a uygun beyaz sprite'lar üretir.
    ///
    /// NEDEN GEREKLÝ: Unity'nin built-in 'UISprite' kaynaðýna MCP üzerinden eriþilemiyor.
    /// Converter AssetDatabase.LoadAssetAtPath kullanýyor ve bu sadece Assets/ altýndaki
    /// gerçek dosyalarý bulabiliyor; built-in kaynaklar diskte o yolda deðil
    /// ("Could not load asset at path 'UI/Skin/UISprite.psd'").
    ///
    /// Bu üretici, Assets/UI/Sprites/ altýna gerçek PNG dosyalarý koyuyor - onlara
    /// eriþilebiliyor.
    ///
    /// Menü: Tools > UI > Generate Rounded Sprites
    /// </summary>
    public static class RoundedSpriteGenerator
    {
        private const string OutputFolder = "Assets/UI/Sprites";

        [MenuItem("Tools/UI/Generate Rounded Sprites")]
        public static void GenerateAll()
        {
            EnsureFolder();

            // Köþe yarýçapý, sprite boyutunun oraný olarak deðil PÝKSEL olarak veriliyor -
            // 9-slice border'ý da ayný piksel deðerine ayarlanacak, böylece sprite hangi
            // boyuta esnetilirse esnetilsin köþeler bozulmuyor.
            CreateRoundedSprite("UIRounded8", 64, 8);
            CreateRoundedSprite("UIRounded16", 64, 16);
            CreateRoundedSprite("UIRoundedPill", 64, 32); // tam yuvarlak uçlar - toggle track için
            CreateCircleSprite("UICircle", 64);

            AssetDatabase.Refresh();
            Debug.Log($"[RoundedSpriteGenerator] Sprites written to {OutputFolder}. Use them with manage_components: properties {{\"sprite\":\"{OutputFolder}/UIRounded16.png\",\"type\":\"Sliced\"}}");
        }

        private static void EnsureFolder()
        {
            if (!Directory.Exists(OutputFolder))
            {
                Directory.CreateDirectory(OutputFolder);
            }
        }

        /// <summary>
        /// Yuvarlatýlmýþ köþeli kare üretir. Beyaz ve opak; renk Image.color'dan gelecek,
        /// bu yüzden sprite'ýn kendisi renksiz olmalý.
        /// </summary>
        private static void CreateRoundedSprite(string name, int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = RoundedRectAlpha(x, y, size, size, radius);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            WritePng(tex, name, radius);
            Object.DestroyImmediate(tex);
        }

        private static void CreateCircleSprite(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f;
            Vector2 center = new Vector2(r, r);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    // 1 piksellik yumuþak kenar - aksi halde daire týrtýklý görünüyor.
                    float alpha = Mathf.Clamp01(r - dist);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            WritePng(tex, name, 0);
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// Bir pikselin yuvarlatýlmýþ dikdörtgenin içinde olup olmadýðýný, kenarlarda
        /// yumuþak geçiþle hesaplar. Anti-aliasing olmadan köþeler merdiven gibi görünüyor.
        /// </summary>
        private static float RoundedRectAlpha(int x, int y, int width, int height, int radius)
        {
            float px = x + 0.5f;
            float py = y + 0.5f;

            float left = radius;
            float right = width - radius;
            float bottom = radius;
            float top = height - radius;

            // Merkez bandlarda tamamen opak.
            if ((px >= left && px <= right) || (py >= bottom && py <= top))
                return 1f;

            // Köþe bölgesi: en yakýn köþe merkezine olan mesafeye bak.
            float cx = px < left ? left : right;
            float cy = py < bottom ? bottom : top;

            float dist = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
            return Mathf.Clamp01(radius - dist + 0.5f);
        }

        /// <summary>
        /// PNG'yi diske yazar ve import ayarlarýný Sprite olarak yapýlandýrýr.
        /// 9-slice border'ý köþe yarýçapýna eþitleniyor - bu olmadan sprite esnetildiðinde
        /// köþeler de esniyor ve yuvarlaklýk bozuluyor.
        /// </summary>
        private static void WritePng(Texture2D tex, string name, int border)
        {
            string path = $"{OutputFolder}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[RoundedSpriteGenerator] Could not configure importer for {path}.");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            if (border > 0)
            {
                importer.spriteBorder = new Vector4(border, border, border, border);
            }

            importer.SaveAndReimport();
        }
    }
}