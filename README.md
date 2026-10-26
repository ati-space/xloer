# Xloer

Windows masaüstü için görev, rapor ve fatura takip uygulaması.
Listeler → bölümler → görevler → alt görevler (5 seviyeye kadar), sürükle-bırak ile
düzenleme ve aramalı bir masaüstü widget’ı.

## Kurulum (Windows)

Son sürüm: **https://github.com/ati-space/xloer/releases/latest**

| Dosya | Ne işe yarar |
| --- | --- |
| `Xloer-Setup-x.y.z.exe` | Kurulum sihirbazı. Başlat menüsü ve masaüstü kısayolu oluşturur, kendini günceller. |
| `Xloer-Portable-x.y.z.exe` | Kurulumsuz çalışan tek dosya. Yeni sürüm çıkınca haber verir, indirme sayfasını açar. |

Uygulama imzasız olduğu için Windows ilk açılışta **“Windows bilgisayarınızı korudu”**
uyarısı gösterebilir: **Ek bilgi → Yine de çalıştır**.

## Özellikler

**Taşıma (sürükle-bırak)**
- Bir rapor başlığını tutup sürükle: alt görevleriyle birlikte taşınır.
- İki satırın arasına bırak: oraya yerleşir. Çizginin girintisi fareyi sağa/sola
  çektikçe değişir: sağa = alt görev, sola = bir üst seviye.
- Bir satırın **ortasına** bırak (satır mavi çerçeveyle yanar): onun alt görevi olur.
- Bölüm başlığını (ör. RAPORLAR) sürükle: bölüm, içindeki her şeyle birlikte taşınır.
- Kenar çubuğundaki bir listenin üzerine bırak: göreve o listeye geçer.
- Sürüklerken `Esc` iptal eder. Klavyeyle: `Alt+↑/↓` sırala, `Tab` alt görev yap,
  `Shift+Tab` bir seviye çıkar.

**Arama**
- Ana pencerede `Ctrl+F` (veya soldaki büyüteç). Widget’ta en üstteki arama kutusu.
- Türkçe karakterlere duyarsızdır: `ing` yazınca “İNG”, “ING”, “İng” bulunur;
  `odeme` → “ödeme”. Birden çok kelime birlikte aranır (`acme rapor`).
- `#RAPOR` yalnızca etikette arar. Açıklamalarda (notlarda) da arar.
- Sonuçlar ağaç olarak gösterilir: bulunan görevin alt görevleri de gelir, üst
  başlığı soluk görünür.
- Widget’ta “Bu liste / Tüm listeler” düğmesi aramanın kapsamını değiştirir.
  `Enter` ilk sonucu ana pencerede açar.

**Widget**
- Masaüstünde ayrı, çerçevesiz bir pencere; görev çubuğunda yer kaplamaz.
- Liste seçimi, hızlı ekleme, tamamlama, sürükle-bırak, sağ tık menüsü.
- İğne simgesi: widget’ın masaüstündeki yerini sabitler (diğer pencerelerin üstüne
  çıkmaz). `⋯` menüsü: diğer pencerelerin üstünde dur, opaklık, gizle.
- Satırın sağındaki ↗ simgesi görevi ana pencerede açar.

**Rapor aşamaları ve proje kodu**
- Her raporun (listedeki üst düzey görev) bir aşaması olur: **Test → Veri → Rapor →
  Müşteri → Fatura**. Satırdaki çizgide bir parçaya tıkla; aynı parçaya tekrar
  tıklamak aşamayı kaldırır. Kenar çubuğundaki “Aşama” bölümü raporları aşamaya
  göre süzer; listenin üstündeki sayaçlar açık rapor, bekleyen adım, müşteride
  bekleyen rapor ve fatura toplamını gösterir.
- Detay panelinde rapora **proje kodu** yazılabilir (P26-…). Aynı kodlu bir fatura
  kartı varsa testleri, gün/vardiyaları, tutarı ve “Tabloyu kopyala” orada görünür;
  yoksa “Fatura kartı oluştur” ile açılır. Arama proje koduyla da bulur.

**Fatura takibi (Faturalar)**
- Soldaki fiş simgesi. Her proje bir kart: sıra no, tek satırlık başlık (Excel’deki
  gibi “P26-…-MÜŞTERİ Test adı (TEST…)”: proje kodu, müşteri/test adı, rapor no),
  test satırları ve tutar (€). Üstte tüm projelerin toplamı.
- Satırda yalnızca **cihazı** ve **tarihleri** seç; test türü cihazdan gelir
  (değiştirilebilir). Tek günlük testte iki tarih de aynı gündür.
- **Gün / Vardiya** kendiliğinden hesaplanır: gün sayısı (iki uç dahil) / gün × 3,
  çünkü günde 3 vardiya dönülüyor (ör. 15.09–28.09 → 14/42).
- Kopyalama simgesi, “Gerçekleştirilen testler ve tarihleri aşağıdaki gibidir;”
  cümlesini ve sarı başlıklı tabloyu panoya koyar; Outlook’a veya Word’e
  yapıştırınca tablo olarak gelir, Excel’e hücre hücre yapıştırılır.
- Cihazlar ve test türleri `⋯ → Cihazlar ve test türleri` ile eklenip çıkarılır.
- **Excel’den içe aktarma:** fatura Excel’ini (.xlsx) pencerenin herhangi bir yerine
  sürükle-bırak ya da `Excel’den al` / `⋯ → Excel’den içe aktar…` ile seç. Faturalar
  açılır ve her “No / Proje Kodu / Test · Test Sistemi · Test Tarihi” bloğu bir kart
  olur: sıra no, proje kodu, müşteri/test adı, rapor no, testler, tarihler ve tutar.
  - Tutar, bloğun “Toplam Kesilen Fatura (€)” satırından (ya da yalnız sayı olan
    satırdan) okunur; iki parça kesilmiş bir faturada iki toplam toplanır.
  - Proje kodu başlıktaki koddan alınır; “Proje Kodu” hücresi başlıkla çelişirse
    (üstteki bloktan kopyalanmışsa) başlık geçerlidir.
  - Virgülle ayrılmış tarihler (09.10.2026, 11.10.2026) ayrı satır olur; “22.05.206”
    gibi eksik yazılmış yıllar 2026 olarak okunur.
  - Tarihsiz not satırları (ör. Test Raporu) kartta tarihsiz satır olarak kalır;
    yalnız başlık olan ara satırlar atlanır.
  - Aynı proje kodu ve No zaten varsa kart güncellenir; tarihli satırlardaki yeni
    cihaz ve testler listeye eklenir. Bildirimdeki “Geri al” içe aktarmanın
    tamamını geri alır. Eski .xls okunmaz; Excel’de .xlsx olarak kaydet.
- **Sırala:** No’ya ya da son test tarihine göre, yeniden eskiye veya eskiden yeniye.
- **Kontrol edilecekler (Uyarılı):** Excel’den gelen kartlarda şunlar işaretlenir:
  Gün / Vardiya’nın tarihlerle uyuşmaması (satırın altında “Excel 1/3”), düzeltilen
  yıl yazımı (“22.05.206”), Gün / Vardiya’dan hesaplanan bitiş, başlıkla çelişen
  “Proje Kodu” hücresi, çok ileri bir tarih (29.09.2029), tarihsiz satır ve
  bulunamayan tutar. Kart başlığındaki uyarı simgesi listeyi açar; her maddenin
  tek tıkla düzeltmesi (ör. “Bitişi 07.10.2026 yap”, “Tek gün yap”, “… kullan”)
  ve “Yoksay” düğmesi vardır. Araç çubuğundaki “Uyarılı (N)” yalnız bunları gösterir.
- Tek günlük testte iki tarih alanı da o günü gösterir; Excel’de tek tarih olup
  Gün / Vardiya daha uzunsa bitiş oradan hesaplanır (ve işaretlenir).
- **Müşteri:** kartın altında; boşsa başlıktan okunur (“ACME CONVERTER …” →
  ACME, aynı ilk kelimeyle başlayan projeler aynı müşteri sayılır). Yazılan ad
  yeniden içe aktarmada korunur.
- **Cihaz adlarını birleştirme:** `⋯ → Cihazlar ve test türleri` aynı cihazın farklı
  yazılışlarını önerir (“70kN Elektrodinamik Sarsıcı” → “70kN Sarsıcı”; ⇄ ile kalacak
  ad değiştirilir) ve her cihazda “Birleştir…” vardır. Kartlardaki satırlar
  güncellenir, sonraki Excel içe aktarmalarında eski ad da yeni ada çevrilir.
- **Seçerek kaldır:** kartın solundaki kutuyla proje seç; Shift ile tıklamak
  aradaki kartları da seçer, araç çubuğundaki kutu görünenlerin hepsini seçer.
  Seçim arama değişince korunur (birkaç aramayla seçim biriktirilebilir); Esc
  temizler. Seçim varken düğme “Seçilenleri kaldır (N)” olur; yoksa “Hepsini
  kaldır” (arama varken “Görünenleri kaldır”). Onay ister, “Geri al” geri getirir.
- **Türkçe takvim:** tarih alanları gg.aa.yyyy yazılır (15.09.2026, 15092026,
  15.9.26 da olur) ya da Pazartesi ile başlayan Türkçe takvimden seçilir. Bitiş
  alanında başlangıçtan önceki günler kapalıdır. Görevlerin bitiş tarihi de aynı
  takvimi kullanır; Windows’un dili ne olursa olsun takvim Türkçedir.

**Pano (Faturalar → Pano)**
- **Aylık:** seçilen yılın 12 ayı için kesilen tutar sütun grafiği ve tablosu
  (proje, test günü, vardiya, tutar); `‹ 2026 ›` ile yıl değiştirilir.
- **Yıllık:** her yılın toplamı. Bir aya ya da yıla tıklayınca üstteki sayılar ve
  cihaz listesi o döneme göre süzülür; tekrar tıklamak seçimi kaldırır.
- Tutar, projenin **son test tarihinin** ayına/yılına yazılır (Excel’de fatura
  tarihi yok). Test tarihi olmayan projeler ayrıca belirtilir.
- **Müşteriler:** dönemde en çok fatura kesilen müşteriler (tutar, proje). Bir
  müşteriye tıklamak ya da üstteki “Müşteri” seçimi tüm panoyu o müşteriye süzer.
- **Cihaz kullanımı:** her test sisteminin dönem içindeki kullanım süresi —
  takvim günü (aynı gün iki test bir gün) ve vardiya (gün × 3). Aya taşan testler
  ay sınırında bölünür. İpucunda doluluk oranı, test ve proje sayısı görünür.

**Diğer**
- **Temalar** (Ayarlar → Görünüm): Laboratuvar, Gece, Orman, Bakır, Mürdüm ve Kontrast;
  her biri koyu, açık ya da sistemle birlikte çalışır.
- **Yenilikler** (soldaki ✦ düğmesi ya da Ayarlar’ın altı): her sürümde eklenenler,
  en yeniden eskiye. Güncellemeden sonra düğmede nokta belirir.
- Bugün / Sonraki 7 Gün, etiketler, öncelik (bayrak), bitiş tarihi, açıklama.
- Tamamlanan ve Çöp Kutusu (geri yükleme), silme/tamamlama için “Geri al”.
- Yeni görev yazarken `#etiket` kullanılabilir. Satıra çift tıkla: yeniden adlandır.
  Bölümde `+` ile ekleyip `Enter`’a basınca bir sonraki satır açılır.
- Pencereyi kapatınca sistem tepsisinde çalışmaya devam eder (Ayarlar’dan kapatılabilir).
- Ayarlar → “Windows açılışında başlat”: oturum açılınca yalnızca widget gelir.

## Güncellemeler

Kurulu Xloer, açılıştan kısa süre sonra ve 6 saatte bir bu deponun
(`ati-space/xloer`) son sürümüne bakar (Ayarlar’dan kapatılabilir). Yeni sürüm
varsa sol alttaki güncelleme butonunda bir nokta belirir, widget’ta “Güncelle”
düğmesi çıkar. **Güncelle** indirir, **Yeniden başlat ve kur** kurar; görevler ve
ayarlar korunur.

### Yeni sürüm yayınlamak

1. `package.json` içindeki `"version"` değerini artır (ör. `1.9.0` → `1.10.0`).
2. `src/shared/changelog.js` dosyasının en başına bu sürümün kaydını ekle (sürüm,
   tarih, başlık, maddeler). Uygulamadaki **Yenilikler** penceresi, güncelleme
   penceresi ve GitHub’daki sürüm notu bu kayıttan gelir; `npm test` kaydın
   unutulmadığını denetler.
3. Commit’leyip `main`’e gönder. `.github/workflows/release.yml` sürüm numarasının
   henüz yayınlanmadığını görünce Windows’ta testleri çalıştırır, kurulumları
   derler ve bu depoda sürüm olarak yayınlar (10 dakika kadar sürer, ilerlemesi
   **Actions** sekmesinde görünür). Ek bir token gerekmez.

Sürüm numarası değişmeyen gönderimler bir şey yayınlamaz; aynı sürüm iki kez
yayınlanamaz. Actions → Release → **Run workflow** ile elle de başlatılabilir;
“Sadece derle” seçilirse yayınlamadan deneme yapar. Yerelde `npm run release`
de aynısını yapar (GitHub CLI’de bu depoya yazma izniyle oturum açmış olmak
gerekir).

## Başka bir uygulamadan taşıma (CSV)

İlk açılışta örnek satırlar içeren boş bir **Work → RAPORLAR** listesi gelir.
Başka bir görev uygulamasındaki verileri getirmek için oradan .csv yedeği al ve
Xloer’de **Ayarlar → CSV’den içe aktar** ile o dosyayı seç. Dosyada en az
“List Name” ve “Title” sütunları olmalı; bölüm, alt görev, kontrol listesi,
tarih, öncelik ve etiket sütunları da varsa okunur.

Zaten olan görevler ikinci kez eklenmez; içe aktarmadan önce mevcut veri
yedeklenir.

## Veriler

`%APPDATA%\Xloer\xloer-data.json` (Ayarlar → Veri klasörünü aç). Her gün
`backups\` altına bir kopya alınır (son 14 gün). Ayarlar’dan JSON yedek alınıp
geri yüklenebilir.

## Geliştirme

Node.js 20+ gerekir.

```bash
npm install
npm start          # uygulamayı çalıştır (F12: geliştirici araçları)
npm test           # veri modeli, sürükle-bırak hesapları, arama, CSV testleri
npm run icon       # build/logo.png’den uygulama simgelerini yeniden üret
npm run dist:win   # dist/ altına Windows kurulum + taşınabilir exe (yayınlamadan)
npm run dist:zip   # dist/ altına Windows ZIP paketi
npm run release    # derle ve bu depoda yeni sürüm olarak yayınla
```

Apple Silicon bir Mac’te `dist:win` ve `release` için Rosetta gerekir (NSIS
derleyicisi Intel ikilisidir): `softwareupdate --install-rosetta --agree-to-license`.

Dosyalar:

- `src/shared/` — veri modeli ve işlemler (`store.js`), sürükle-bırak hedef hesabı ve
  satır listeleri (`tree-rows.js`), arama (`search.js`), CSV içe aktarma (`tasks-csv.js`).
  Electron’a bağımlı değildir; testler bunları doğrudan çalıştırır.
- `src/main/` — Electron ana süreci: pencereler, widget, tepsi, kayıt,
  güncellemeler (`updater.js`).
- `build/logo.png` — logonun kaynağı (beyaz, saydam zemin). `npm run icon` bundan
  `build/icon.png` ve `src/renderer/img/logo.png` üretir.
- `src/renderer/` — arayüz. `index.html` ana pencere, `widget.html` widget.
  Tarayıcıda açılırsa (ör. `python3 -m http.server -d src`) localStorage ile çalışır.
- `src/renderer/fonts/` — Space Grotesk ve JetBrains Mono (SIL Open Font License,
  lisans metinleri yanlarında); internetsiz çalışsın diye uygulamaya gömülü.
- `test/fixtures.js` — testlerin kullandığı gerçek rapor listesi. `src/` dışında
  durduğu için yayınlanan kurulum dosyalarına girmez.
