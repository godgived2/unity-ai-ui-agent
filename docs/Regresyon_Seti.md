# Unity AI UI Ajanı — Regresyon Seti

Her kod değişikliğinden sonra bu beş test çalıştırılır. Amaç tartışmak değil, işaretlemek: her maddenin karşısına ✓ ya da ✗.

Bir test bugün geçip yarın düşüyorsa, aradaki değişiklik o testi bozmuştur. Bu setin tek işi bunu yakalamak.

---

## Her testten önce

1. Hierarchy'de `MainCanvas`'ı ve sahne kökündeki `TabController` objesini sil. `EventSystem`, `Main Camera`, `Directional Light` kalsın.
2. `Assets/UI/Generated/` içindeki, önceki testlerden kalan scriptleri sil.
3. Console'u temizle.
4. Game view: **Full HD (1920x1080)**, Scale **1x**.

## Her testten sonra kaydet

- Game view ekran görüntüsü
- Console'un son 10 satırı
- Sohbet penceresindeki final mesaj
- Toplam süre (yaklaşık)

## Her testte geçerli genel ölçütler

- [ ] Kırmızı hata yok
- [ ] Sonda `Duplicate macro call blocked` / `Stuck repeating` yok
- [ ] Görev `task_complete` ile bitti, final mesaj modelin kendi özeti
- [ ] Hiçbir eleman Canvas dışında değil
- [ ] Hiçbir iki kardeş üst üste binmiyor
- [ ] Bütün yazılar okunuyor (kontrast düzeltmesi Console'da görünebilir, bu normal)
- [ ] Prompt accent rengi veriyorsa Console'da `Accent colour '...' taken from the request` satırı var

---

## R1 — Sekmeli ayarlar ekranı

**Sınadığı:** sekme panelleri, kart yönlendirmesi, piksel sınırlı satırlar, accent kilidi, kart sıkıştırma, bitiş sinyali.

**Beklenen durum:** tamamen geçmeli. Bugünkü referans test bu.

```
Build a vehicle diagnostics screen with tabs.

LAYOUT:
A narrow navigation sidebar pinned to the LEFT side, full height.
A content area filling the rest of the screen, to the right of the sidebar.

SIDEBAR:
A title label at the top reading "DIAGNOSTICS".
Below it three navigation items: "Engine", "Brakes", "Electrical".
Mark "Engine" as the selected one.

CONTENT AREA - three switchable tab panels, all sharing the same rectangle:

EnginePanel contains one card titled "ENGINE". Rows inside it:
a row labelled "Coolant temp" with the value "92 C",
a row labelled "Oil pressure" with the value "4.1 bar",
a row labelled "RPM" with a read-only fill bar at 45 percent,
a row labelled "Idle smoothing" with an on/off switch, start it on.

BrakesPanel contains one card titled "BRAKES". Rows inside it:
a row labelled "Pad wear front" with a read-only fill bar at 72 percent,
a row labelled "Pad wear rear" with a read-only fill bar at 58 percent,
a row labelled "ABS" with the value "Ready",
a row labelled "Assist level" with a slider from 0 to 100 set to 40.

ElectricalPanel contains one card titled "ELECTRICAL". Rows inside it:
a row labelled "Battery" with a read-only fill bar at 88 percent,
a row labelled "Alternator" with the value "14.2 V",
a row labelled "Fault codes" with the value "None",
a row labelled "Night mode" with an on/off switch, start it off.

STYLING:
Dark theme. Amber accent. Each card gets a thin border.
Card titles 18 point. Row labels 15 point. Values 15 point.
The sidebar title is 22 point.
```

**Ölçütler:**
- [ ] `ContentArea` altında `EnginePanel`, `BrakesPanel`, `ElectricalPanel` var
- [ ] Game view'da yalnızca ENGINE kartı görünüyor
- [ ] 12 satırın 12'si doğru kartta
- [ ] Toggle, dolum çubukları ve nav göstergesi amber
- [ ] Satırlar kartın üstünde sıkı (~52 px), aralarında dev boşluk yok
- [ ] Console'da `Compacted 3 card(s)`: kartlar son satırın hemen altında bitiyor
- [ ] Sidebar öğeleri birbirine yakın

**İkinci komut (bağlama):**

```
Wire the sidebar tabs to the content panels so clicking a tab shows that panel
and hides the other two.
NavEngine controls EnginePanel.
NavBrakes controls BrakesPanel.
NavElectrical controls ElectricalPanel.
Set the TabController's selected colours to match the amber accent used on the
screen, and keep the unselected label colour readable.
```

- [ ] Sahne kökünde `TabController`, Inspector'da 3 sekme, her birinde 4 alan dolu
- [ ] **Play modda** üç sekme de doğru paneli açıyor
- [ ] `AssistLevel` slider'ı sürüklenebiliyor, `RPM` çubuğu sürüklenemiyor (doğru davranış)

---

## R2 — Operatör konsolu

**Sınadığı:** beş bölgeli ekran bölme, iki yan sütun aynı anda, üst üste dizilmiş kartlar, görsel alan, buton çubuğu, farklı renkte tek buton.

**Beklenen durum:** büyük ölçüde geçmeli. **Bilinen sınır:** durum çubuğunda yan yana iki etiket çakışabilir (yatay yerleşim henüz yok, Aşama 4'te gelecek). Üst üste kartlarda yalnızca en alttaki sıkıştırılır.

```
Build an air defence operator console screen.

SCREEN REGIONS - create these five panels first, then fill them:
A thin status strip across the top.
A narrow status column pinned to the LEFT side, below the strip.
A narrow target column pinned to the RIGHT side, below the strip.
A large radar display area in the middle, between the two columns.
A command bar across the bottom, full width.

TOP STATUS STRIP:
On the left the text "BATTERY-04 / SECTOR NORTH".
On the right the text "OPERATIONAL".

LEFT STATUS COLUMN - three cards stacked:
Card one titled "SYSTEM". Rows inside it: a row labelled "Radar" with the
value "Active", a row labelled "Datalink" with the value "Connected", and a
row labelled "Power" with a read-only fill bar at 94 percent.
Card two titled "READINESS". Rows inside it: a row labelled "Launchers" with
the value "4 / 4", a row labelled "Interceptors" with the value "28", and a
row labelled "Reload" with a read-only fill bar at 67 percent.
Card three titled "MODE". Rows inside it: a row labelled "Auto engage" with
an on/off switch, start it off. A row labelled "Silent tracking" with an
on/off switch, start it on.

CENTRE RADAR AREA:
One large image area with the caption "RADAR SWEEP - NO SIGNAL" in the middle.
Give it a thin border.

RIGHT TARGET COLUMN - two cards stacked:
Card one titled "TRACK 0117". Rows inside it: a row labelled "Bearing" with
the value "284 deg", a row labelled "Range" with the value "41.2 km", a row
labelled "Altitude" with the value "8 400 m", and a row labelled "Speed" with
the value "310 m/s".
Card two titled "FILTERS". Rows inside it: a row labelled "Minimum range"
with a slider from 0 to 100 set to 15, a row labelled "Track threshold" with
a slider from 0 to 100 set to 60, and a text field for a callsign filter with
the hint "Filter by callsign".

BOTTOM COMMAND BAR:
Four buttons in one row: "Track", "Designate", "Hold Fire", "Reset Filters".
Make "Hold Fire" amber. Make the other three dark grey.

STYLING:
Dark theme. Cyan accent. Every card gets a thin border.
Card titles 18 point. Row labels 15 point. Values 15 point.
The status strip text is 16 point.
Cards must NOT stretch to fill their column - size each card to the number of
rows it holds and leave the empty space below the last card.
Keep every label readable - no text may be dimmer than mid grey.
```

**Ölçütler:**
- [ ] Beş bölge var ve hiçbiri diğerinin üstüne binmiyor
- [ ] Radar alanı iki sütunun **arasında**, ekranın tamamında değil
- [ ] Sol sütunda 3 kart, sağ sütunda 2 kart, hepsi dolu
- [ ] `TRACK 0117` kartının 4 satırı var (daha önce boş kalmıştı)
- [ ] Komut çubuğunda 4 buton, `Hold Fire` amber, diğerleri koyu gri
- [ ] Accent **cyan** (Console satırı: `'cyan' taken from the request`), `Hold Fire`'ın amberi tema sanılmamış
- [ ] Durum çubuğunda iki etiket — *bilinen sınır, çakışırsa not al*

---

## R3 — Login ekranı

**Sınadığı:** ortalanmış tek kart, iki input (biri şifre), toggle satırı, iki butonlu çubuk, farklı bir accent.

**Beklenen durum:** tamamen geçmeli.

```
Build a login screen.

LAYOUT:
A full-screen dark background panel.
One centred dialog card in the middle of the screen, titled "SIGN IN".

INSIDE THE CARD, stacked top to bottom:
A text field for the username with the hint "Username".
A text field for the password with the hint "Password". It must mask the
characters.
A row labelled "Remember me" with an on/off switch, start it off.
A row of two buttons: "Sign in" and "Cancel". "Sign in" uses the accent
colour, "Cancel" is dark grey.

STYLING:
Dark theme. Green accent. The card gets a thin border.
Card title 22 point. Text fields and labels 16 point.
```

**Ölçütler:**
- [ ] Kart ekranın ortasında, köşeye ya da tam ekrana yayılmamış
- [ ] İki input da içine yazılabiliyor (Play modda dene), şifre alanı `***` gösteriyor
- [ ] Toggle kapalı başlıyor ve tıklanınca değişiyor
- [ ] İki buton yan yana, eşit genişlikte; `Sign in` yeşil
- [ ] Accent **green** (Console satırı)
- [ ] Kart içeriğine sıkıştırılmış, altta büyük boşluk yok

---

## R4 — Dashboard

**Sınadığı:** yan yana üç kart (yatay yerleşim), büyük punto okuma değerleri, altta geniş bir kart.

**Beklenen durum:** **kısmen geçmesi beklenir.** Yan yana kartları model elle yerleştirmek zorunda; otomatik yatay akış henüz yok. Bu test Aşama 4'ün (yatay yerleşim) başarı ölçüsü olacak. Bugünkü sonucu referans olarak kaydet.

```
Build a factory monitoring dashboard.

LAYOUT:
A thin top bar across the full width with the title "LINE 3 - OVERVIEW" on
the left.
Below it, a row of THREE status cards side by side, equal width, spanning the
full width.
Below that row, one wide card spanning the full width.

STATUS CARDS (left to right):
Card "OUTPUT": a large value "1 284" (40 point) and under it a caption
"units today".
Card "EFFICIENCY": a large value "92 %" (40 point) and under it a caption
"target 90 %".
Card "DOWNTIME": a large value "14 min" (40 point) and under it a caption
"since 06:00".

WIDE CARD titled "STATION LOAD". Rows inside it:
a row labelled "Press" with a read-only fill bar at 81 percent,
a row labelled "Welder" with a read-only fill bar at 64 percent,
a row labelled "Paint" with a read-only fill bar at 37 percent.

STYLING:
Dark theme. Blue accent. Every card gets a thin border.
Card titles 16 point. Captions 13 point. Row labels 15 point.
```

**Ölçütler:**
- [ ] Üç durum kartı **yan yana**, üst üste değil — *Aşama 4 ölçütü*
- [ ] Üç kart eşit genişlikte ve birbirine binmiyor — *Aşama 4 ölçütü*
- [ ] Büyük değerler 40 punto ve kesilmeden okunuyor
- [ ] Geniş kart üç kartın **altında**, 3 dolum çubuğu dolu
- [ ] Accent **blue**

---

## R5 — Uzun liste

**Sınadığı:** tek kartta 8 satır (konteyner sınırı tam 8), otomatik akışın uzun bir listede davranışı, sıkışma ve kart sıkıştırma.

**Beklenen durum:** geçmeli. 8 satır sınırın tam üstünde. Dokuzuncu satır olsaydı engellenirdi, bu bilinçli.

```
Build a notification settings screen.

LAYOUT:
A thin top bar across the full width with the title "NOTIFICATIONS" on the
left.
Below it, one tall card filling most of the screen, titled "CHANNELS".

ROWS INSIDE THE CARD, top to bottom, each with an on/off switch:
"Email alerts" - on.
"SMS alerts" - off.
"Push notifications" - on.
"Weekly digest" - on.
"Security warnings" - on.
"Product updates" - off.
"Partner offers" - off.
"Quiet hours" - on.

Every row also has a short description line under its label, for example
"Receive a message when a job fails" for Email alerts. Write a fitting
description for each row.

STYLING:
Dark theme. Purple accent. The card gets a thin border.
Card title 18 point. Row labels 15 point. Descriptions 12 point.
```

**Ölçütler:**
- [ ] 8 satırın 8'i var, sırası doğru
- [ ] Her satırın açıklama satırı var ve kesilmiyor (açıklama iki satıra sarabilir, bu normal)
- [ ] Satırlar eşit aralıklı, üst üste binmiyor
- [ ] Hiçbir satır `Auto-flow shrank` ile ezilmemiş (Console'u kontrol et)
- [ ] Açık/kapalı durumları istenen gibi
- [ ] Accent **purple**
- [ ] Kart son satırın altında bitiyor

---

## Sonuç tablosu

Her çalıştırmada bir satır ekle. Hücreye: ✓ geçti · ✗ kaldı · ~ kısmen.

| Tarih | Kod değişikliği | R1 | R1 bağlama | R2 | R3 | R4 | R5 | Not |
|---|---|---|---|---|---|---|---|---|
| | *(referans)* | | | | | | | |
| | | | | | | | | |
| | | | | | | | | |

**Kural:** Bir önceki satırda ✓ olan bir test yeni satırda ✗ olursa, o değişiklik geri alınır ya da düzeltilir. Yeni bir özellik, var olanı bozma pahasına eklenmez.
