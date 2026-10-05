# Unity AI UI Agent

Unity Editor'de yazdığınız bir cümleden çalışır arayüz üretir. Model **tamamen yerel**
çalışır: bulut servisi yok, internet gerekmiyor, hiçbir veri makineden çıkmıyor.

## \*\*\[▶ Demo videosu](https://youtu.be/-aj4el2zrA8)\*\* — boş sahneden çalışan ekrana, 50 dakikalık kurulum ve Play modunda sekmelerNe yapar

Sohbet penceresine ekranı tarif edersiniz. Ajan sahneye Canvas'ı kurar, panelleri
yerleştirir, kartları ve satırları oluşturur, renkleri uygular, kontrolleri bağlar ve
istenirse davranış script'i yazıp bileşen olarak ekler.

Çıktı **gerçek bir UGUI hiyerarşisi**: prefab değil, şablon değil, ekran görüntüsü değil.
Inspector'dan düzenleyebileceğiniz, Play'e basınca çalışan objeler.

**Örnek istek:**

```
Build a login screen.
One centred dialog card in the middle of the screen, titled "SIGN IN".
A text field for the username with the hint "Username".
A text field for the password with the hint "Password". It must mask the characters.
A row labelled "Remember me" with an on/off switch, start it off.
A row of two buttons: "Sign in" and "Cancel".
Dark theme. Green accent. The card gets a thin border.
```

**Üretilen:**

```
MainCanvas
└── LoginCard                    ortalanmış, içeriğine göre boyutlanmış
    ├── LoginCardTitle           "SIGN IN", 22 punto
    └── LoginCardContent
        ├── UsernameRow          etiket + TMP\_InputField (ipucu: "Username")
        ├── PasswordRow          etiket + maskeli input
        ├── RememberMeRow        etiket + çalışan Toggle, yeşil dolgu
        └── LoginButtons         "Sign in" + "Cancel", eşit genişlikte
```

Play'e bastığınızda input'lara yazabilir, toggle'a tıklayıp rengin değiştiğini
görebilirsiniz. Hiçbir elle müdahale gerekmez.

\---

## Neler üretebilir

### Ekran tipleri

||Örnek|
|-|-|
|**Sekmeli ekranlar**|Sol sidebar, üç geçişli panel, `TabController` bağlanmış|
|**Operatör konsolları**|Üst durum şeridi, iki yan sütun, ortada görsel alan, alt komut çubuğu|
|**Diyaloglar**|Ortalanmış login / onay kartı|
|**Dashboard'lar**|Yan yana KPI kartları, büyük okuma değerleri, dolum çubukları|
|**Ayar listeleri**|Açıklamalı satırlar, toggle'lar, slider'lar|

### Elemanlar

|Makro|Ne üretir|
|-|-|
|`create\_ui\_panel`|Bölge paneli — `region` ile ekranı böler, `columns` ile sütunlara ayırır|
|`create\_ui\_card`|Başlıklı kart, içerik alanıyla; `centered` ile ortalanabilir|
|`create\_ui\_row`|"Solda etiket, sağda kontrol" satırı — toggle, slider, dolum çubuğu ya da input ile|
|`create\_ui\_button` · `create\_ui\_button\_bar`|Tek buton ya da N butonu eşit genişlikte dizen çubuk|
|`create\_ui\_nav\_item`|Sidebar navigasyon satırı, seçili göstergesiyle|
|`create\_ui\_toggle`|Çalışan açma/kapama anahtarı|
|`create\_ui\_slider` · `create\_ui\_progress\_bar`|Sürüklenebilir slider / salt okunur gösterge|
|`create\_ui\_input`|Metin alanı, ipucu ve şifre maskeleme desteğiyle|
|`create\_ui\_label` · `create\_ui\_image`|Serbest yazı / görsel alanı|
|`write\_script`|C# davranış script'i yazar, görev sonunda derletir|

Her makro 7-10 MCP çağrısına açılır. Slider'ın `fillRect`'i, Toggle'ın `graphic`'i,
Button'ın `targetGraphic`'i dahil tüm referanslar bağlanır — yani kontroller gerçekten
çalışır, görünüşte değil.

### Otomatik yapılanlar

İstemenize gerek olmayan şeyler:

* **Yerleşim** — anchor vermezseniz elemanlar alt alta dizilir, aralarında doğru boşlukla
* **Çakışma engeli** — iki kardeş aynı dikdörtgeni paylaşamaz
* **Okunabilirlik** — koyu zemine koyu yazı konursa ton korunup parlaklık düzeltilir
* **Boyutlandırma** — satırlar \~52 piksel, kartlar içeriğine göre kısaltılır
* **Tema tutarlılığı** — "amber accent" dediyseniz renk verilmeyen her eleman onu kullanır
* **Köşe yuvarlama, hover/pressed renkleri, raycast ayarları**

\---

## Kurulum

```bash
ollama pull qwen3:14b
```

1. Unity 2022.3 ile projeyi açın
2. CoplayDev Unity MCP paketini kurun, `MCP CONNECTED` yazısını doğrulayın
3. `Tools > UI > Generate Rounded Sprites` komutunu bir kez çalıştırın
4. `Window > AI MCP Chat` penceresini açın, isteği yazın, `Ctrl + Enter`

İsteği İngilizce yazmak daha iyi sonuç verir. İstediğiniz yazıları **tırnak içinde**
belirtin — ajan görev sonunda hepsinin ekranda olduğunu denetler.

\---

## Çalışma ortamı

|||
|-|-|
|Unity|2022.3.17f1 LTS|
|Model|`qwen3:14b` — Ollama, yerel|
|Donanım|RTX 4060 Laptop, 8 GB VRAM|
|Köprü|CoplayDev Unity MCP|
|Bağlam penceresi|32 768 token|

Model 8 GB VRAM'e tam sığmadığı için bir ekran 20-40 dakikada kurulur. Hız hedef değildi;
**aynı isteğin aynı sonucu vermesi** hedefti.

\---

## Nasıl çalışır

```
Kullanıcı isteği
      │
      ▼
PromptBuilder ──── Kural bloklarını zorunlu/opsiyonel ayırır, token bütçesini
      │            hesaplar, aşımda düşük öncelikli blokları düşürür
      ▼
OllamaClient ───── Yerel model çağrısı
      │
      ▼
OllamaToolAgent ── Araç döngüsü; tekrarları engeller, ilerleme listesi tutar,
      │            bitiş korumalarını işletir, script'leri derletir
      ▼
UiMacroExpander ── 15 makro → doğrulanmış MCP çağrı dizisi; yerleşim, çakışma
      │            ve kontrast denetimi burada
      ▼
   Unity sahnesi
```

**Tasarım ilkesi: niyeti model söyler, hesabı kod yapar.**

Model "üç kart yan yana" demek ister; altı anchor değerini aynı anda tutturmaya çalışırsa
yanılır. Ona `"columns": 3` demeyi öğretirseniz bantları kod hesaplar ve çakışma
matematiksel olarak imkânsız hale gelir.

|Model ne der|Kod ne hesaplar|
|-|-|
|`"region": "top"`|Ekranın boş alanından üst bandı keser|
|`"columns": 3`|Eşit genişlikte üç sütun, aralarında boşluk|
|`"centered": true`|Kartı ortalar, sıkıştırırken ortada tutar|
|anchor vermez|Son kardeşin altına yerleştirir|

### İki katmanlı kurulum

Her eleman iki aşamada tamamlanır: önce `manage\_gameobject` / `manage\_components` ile
yapı, sonra `execute\_code` ile referans bağlama. İkincisi zorunlu, çünkü MCP katmanı bir
bileşen alanına başka bir **sahne objesi** atayamıyor — gelen metni asset yolu sanıyor.
Slider'ın çalışması tam olarak buna bağlı.

\---

## Güvenilirlik

Sistem, modelin hatalarını yakalayacak şekilde kurulu. Başlıca mekanizmalar:

|Alan|Mekanizmalar|
|-|-|
|**Yerleşim**|Otomatik dikey akış · sütun farkındalığı · bölge yerleştirme · çakışma denetimi (%18 eşik) · sığdırma|
|**Görünüm**|WCAG 3:1 kontrast denetimi · piksel sınırlı yükseklikler · kart sıkıştırma · metin taşma kontrolü · oturum accent'i|
|**Döngü**|Kırpılmayan inşa listesi · tekrar engeli · "zaten var" sahiplenme · kısmi geri alma · boş konteyner ve eksik metin denetimi|

Üç denetim görev bitmeden önce çalışır ve eksik varsa modeli geri iter:

* **Boş konteyner** — kurulmuş ama içi doldurulmamış bir kart varsa
* **Eksik metin** — istekte tırnak içinde geçen bir yazı ekranda yoksa
* **Eksik altyapı** — EventSystem yoksa butonlar tıklanamaz

### Regresyon testleri

Her kod değişikliği sabit beş ekran tipiyle doğrulanır. Promptlar değişmez; değişen tek
şey koddur.

|Test|Ekran|Durum|
|-|-|-|
|R1|Sekmeli araç teşhis ekranı + davranış bağlama|✓|
|R2|Beş bölgeli operatör konsolu|✓|
|R3|Ortalanmış login diyaloğu|✓|
|R4|Yan yana kartlı dashboard|✓|
|R5|Tek kartta sekiz satırlık liste|✓|

Ayrıntı: [`docs/Regresyon\_Seti.md`](docs/Regresyon_Seti.md) ·
Kayıtlar: [`docs/Regresyon\_Takip.xlsx`](docs/Regresyon_Takip.xlsx)

\---

## Teşhis günlüğü

Sistemin bugünkü haline gelmesini sağlayan yedi bulgu. Hepsi ölçümle tespit edildi.

<details>
<summary><b>1. Prompt sessizce kırpılıyordu — 710 token taşma</b></summary>

Model bazı kuralları tutarsız uyguluyordu. Prompt'u token saydırınca pencere 20 480,
prompt 21 190 çıktı. Ollama fazlasını baştan kesiyor, yani en kritik kurallar gidiyor ve
hiçbir uyarı çıkmıyordu.

Kural blokları zorunlu/opsiyonel diye ayrıldı, her bloğa öncelik verildi, bütçe aşılınca
düşük öncelikliler düşürülüyor. Taşma sıfırlandı.

</details>

<details>
<summary><b>2. Model "işim bitti" diyemiyordu</b></summary>

Her sekmeli ekran `Stuck repeating an already-completed macro call` ile bitiyordu. İş
bitmişti; model son çağrıyı tekrarlıyordu.

Sebep: her adım `forceJson: true` ile çağrılıyor ve Ollama çıktıyı dilbilgisi seviyesinde
JSON'a kilitliyor. Prompt'un onlarca yerinde yazan "düz metinle cevap ver" talimatı
uygulanamazdı — model düz metin üretemez.

JSON içinde bir bitiş yolu eklendi: `{"type":"task\_complete"}`. Görev başına \~3 dakika
kazandırdı.

</details>

<details>
<summary><b>3. Geçmiş baştan kırpılıyordu</b></summary>

Model ilk kurduğu satırları "eksik" sanıp yeniden kuruyordu. Bellek sınırı 40 mesajdı;
her adım iki mesaj ekliyor, yani 20 adım. Sekmeli ekran 28 adım sürüyor.

Çözüm: kırpılmayan bir inşa listesi. Kurulan her eleman her adımda modele kompakt bir
ağaç olarak gösteriliyor, maliyeti \~143 token.

</details>

<details>
<summary><b>4. Y ekseni ters kullanılıyordu</b></summary>

"Üstte ince bir durum çubuğu" istendi, çubuk ekranın altında çıktı. Unity'de anchor'da
`y=0` alt kenardır; model web mantığıyla hesaplıyordu.

Çözüm: `region` bölgeleri. Model "top" der, kod hesaplar.

</details>

<details>
<summary><b>5. Kapalı başlayan toggle hiç açılamıyordu</b></summary>

Tıklanıyor, değeri değişiyor, ama ekranda hiçbir zaman açık görünmüyordu. Kapalı başlayan
toggle'ın dolgusunu `SetActive(false)` ile gizliyordum; Unity'nin Toggle'ı ise yalnızca
şeffaflığı değiştirir, pasif objeyi geri açmaz.

</details>

<details>
<summary><b>6. Tekrarlanan Content eki</b></summary>

`Parent specified ('OutputCardContentContent') but not found` — üç kart birden boş kaldı.
Model, uyarıda verilen `OutputCardContent` adına bir kez daha `Content` ekliyordu.

Olmayan bir adın sonundaki fazla ek, kırpılmış hâli kayıtta gerçekten varsa kırpılıyor.

</details>

<details>
<summary><b>7. Unutulan elemanlar</b></summary>

Operatör konsolunda bir satır üç tur üst üste hiç kurulmadı. Ortada hata yoktu; model iş
listesinin bir kısmını atlıyordu.

Kullanıcı istediği her metni tırnak içinde yazdığı için, görev bitmeden önce o tırnaklar
çıkarılıp ekrandakilerle karşılaştırılıyor. İlk çalıştırmada 16 metinden yalnızca
gerçekten eksik olanı buldu.

</details>

### Ölçümler

||Önce|Sonra|
|-|-|-|
|Satır yüksekliği (büyük kartta)|150 px|**52 px**|
|Kart başlık bandı|156 px|**58 px**|
|Dört satırlık kart|972 px|**422 px**|
|Prompt taşması|710 token|**0**|

\---

## Bilinen sınırlar

* Üst üste dizili kartlarda yalnızca en alttaki içeriğine sıkıştırılır
* `ScrollRect` ve `Dropdown` makroları yok; konteyner başına 8 çocuk sınırı var
* Model bir elemana açıkça renk verdiğinde tema rengini ezebilir (bilinçli: "bataryayı
yeşil yap" gibi anlamsal istekler korunsun diye)
* Var olan bir ekranı düzenleme ("bunu büyüt", "şunu sağa al") desteklenmiyor
* Bir ekran 20-40 dakika sürer (8 GB VRAM)

## Yol haritası

* \[ ] Sütun bazlı kart sıkıştırma
* \[ ] `ScrollRect` ve `Dropdown` makroları
* \[ ] Router — isteği role ayırıp kurma ve bağlamayı tek komutta birleştirmek
* \[ ] Tek ayar dosyası (model adı, bağlam penceresi, adım sınırı)
* \[ ] Yerleşim ve kontrast fonksiyonları için birim testleri

\---

## Dosya yapısı

```
Assets/AI/
├── Agent/
│   ├── OllamaToolAgent.cs      araç döngüsü, korumalar, inşa listesi
│   └── UIMacroExpander.cs      15 makro, yerleşim, çakışma, kontrast
├── Client/
│   └── OllamaClient.cs         yerel model istemcisi
└── Prompt/
    ├── PromptBuilder.cs        bütçe farkındalıklı prompt kurulumu
    ├── PromptRulesCore.cs      kimlik ve döngü kuralları
    ├── PromptRulesUI.cs        makro tanıtımı, yerleşim kuralları
    └── PromptRulesUnity.cs     çıktı biçimi, Unity kuralları

Assets/UI/
├── TabController.cs            sekme geçişi bileşeni
└── Generated/                  modelin ürettiği davranış script'leri
```

\---

CoplayDev Unity MCP üzerine kuruludur.

