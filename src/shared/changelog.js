// What every version brought, newest first. The "Yenilikler" window shows
// it, and `npm run release` writes the newest entry into the GitHub release
// and the in-app update notes. A new version starts with a new entry here.
//
// kind: 'new' (a feature), 'better' (a change to one), 'fix' (a repair).

export const KINDS = { new: 'Yeni', better: 'İyileştirme', fix: 'Düzeltme' };

export const CHANGELOG = [
  {
    version: '1.16.1',
    date: '2026-10-26',
    title: 'Portable artık her açılışta hızlı',
    items: [
      ['fix', 'Portable sürüm açıldıktan sonraki her başlatmada da kurulum sürümü kadar hızlı açılır; 1.16.0’da her açılışta hâlâ tüm uygulamayı açıp bekletiyordu'],
      ['better', 'Portable sürümün her yeni sürümdeki ilk açılışı da birkaç kat kısaldı (dosya 40 MB kadar büyüdü)'],
      ['better', 'Yayından önceki Windows denetimi portable sürümün açılış süresini de ölçer; kurulum sürümünden belirgin yavaşsa sürüm yayınlanmaz'],
    ],
  },
  {
    version: '1.16.0',
    date: '2026-10-26',
    title: 'Hızlı portable, dışa açılan panel, etiket renkleri',
    items: [
      ['fix', 'Portable sürüm çok daha hızlı açılır: her açılışta tüm uygulamayı geçici klasöre yeniden çıkarmak yerine her sürümü bir kez açar (%LOCALAPPDATA%\\Xloer\\portable), sonraki açılışlar doğrudan oradan başlar; eski sürümlerin klasörü kendiliğinden silinir'],
      ['better', 'Her sürüm yayınlanmadan önce Windows’ta kurulum ve portable sürüm gerçekten açılıp denetlenir: pencere, sayfa, kayıt, widget, dosya yolları, güncelleme denetimi'],
      ['better', 'Ayrıntı paneli dışa doğru açılır: pencere sağa genişler, liste yerinde ve aynı genişlikte kalır, kapanınca pencere eski boyutuna döner. Ekranın sağında yer yoksa pencere sola kayar; ekranı kaplayan pencerede panel içeride açılır. Panel açıkken pencerenin sağ kenarını çekmek paneli büyütür'],
      ['new', 'Kenar çubuğu genişletilip daraltılabilir: liste ile arasındaki çizgiyi sürükle (çift tık: varsayılan); genişlik hatırlanır'],
      ['better', 'Etiket rengi etiketin kutusunun tamamını doldurur (kenar çubuğu, satırlar, ayrıntı paneli, widget); renk menüsünde seçenekler renkli kutular, “Renksiz” de eklendi'],
      ['better', 'Liste başlığına çift tıklamak artık düzenleme penceresini açmaz; ad ve simge kenar çubuğundaki sağ tık menüsünden değişir'],
      ['better', 'Sürümlerde .blockmap dosyası yok; güncelleme kurulum dosyasını bütün olarak indirir'],
    ],
  },
  {
    version: '1.15.0',
    date: '2026-10-26',
    title: 'Başlık çubuğu, aşamalar, ayrıntı paneli',
    items: [
      ['fix', 'Pencere yalnız en üstteki başlık çubuğundan taşınır; ikonlar, düğmeler ve başlıklar artık pencereyi sürüklemez'],
      ['fix', 'Pencere küçükken iletişim kutularının X düğmesi (ör. Yenilikler) tam üzerinden tıklanır; pencere düğmeleri başlık çubuğunda, her ölçeklendirmede yerinde'],
      ['better', 'Arama: yazarken dönen gösterge, ardından “N sonuç” ya da “Sonuç yok”; arama düğmesi kutuyu vurgular (widget’ta da)'],
      ['better', 'Daha hızlı açılış: ana pencere hemen görünür, widget, tepsi ve güncelleyici arkadan yüklenir; kurulum yalnız Türkçe ve İngilizce dil dosyalarıyla daha küçük'],
      ['new', 'Aşamalar düzenlenebilir: kenar çubuğunda “Aşama”nın yanındaki kalemle yeni aşama ekle, adını değiştir, sırala, sil (“Geri al” ile); raporlar aşamalarında kalır'],
      ['new', 'Ayrıntı paneli bir göreve tıklayınca kayarak açılır, kapatınca kapanır; liste ile panel arasındaki çizgiyle genişliği ayarlanır ve hatırlanır'],
    ],
  },
  {
    version: '1.14.0',
    date: '2026-10-26',
    title: 'Fatura yılı',
    items: [
      ['new', 'Her proje bir yıla atanabilir, test tarihinden bağımsız: kartta “Yıl” seçilir (Otomatik: son testin yılı). Tutarı seçilen yılın toplamına eklenir, Excel’deki yıllık tablolar gibi'],
      ['new', 'Faturalar’da “Yıl” seçimi: o yılın projeleri ve başlıkta o yılın toplamı. Seçili kartlar “Yıla taşı” ile tek seferde başka yıla alınır'],
      ['better', 'Pano yılları fatura yılına göre toplar; testi önceki yılda olup bu yıla yazılan projeler aylık grafikte “Devir” sütununda'],
      ['better', 'Excel’i içe aktarırken dosya adındaki yıl (ör. “fatura 2026.xlsx”) projelerin yılı olur'],
    ],
  },
  {
    version: '1.13.0',
    date: '2026-10-26',
    title: 'Tek başlık alanı',
    items: [
      ['better', 'Fatura kartında proje kodu, müşteri / test adı ve rapor no tek alanda, Excel’deki başlık gibi yazılır: “P26-0001-0001-MÜŞTERİ Test adı (TEST…)”; kaydederken parçalarına ayrılır'],
    ],
  },
  {
    version: '1.12.0',
    date: '2026-10-26',
    title: 'Aramada alt görevler, widget sabitleme',
    items: [
      ['better', 'Arama yapıldığında projelerin alt görevleri de gelir: eşleşen görevin altındaki her şey sonuçta görünür (widget’ta ve ana pencerede)'],
      ['better', 'Widget’taki iğne artık yalnız masaüstündeki yerini sabitler; widget diğer uygulamaların üstünde durmaz. İstenirse ⋯ → “Diğer pencerelerin üstünde dur”'],
      ['fix', 'Önceden iğneyle sabitlenmiş widget yerinde kalır ama artık her pencerenin üstüne çıkmaz'],
    ],
  },
  {
    version: '1.11.0',
    date: '2026-10-26',
    title: 'Yeni temalar',
    items: [
      ['new', 'Beş yeni renk teması: Gece (lacivert, camgöbeği), Orman (koyu yeşil, limon), Bakır (kahve, bakır), Mürdüm (mürdüm, gül pembesi) ve Kontrast (siyah-beyaz); Ayarlar → Görünüm'],
      ['new', 'Her tema koyu, açık ve sistem moduyla çalışır; ana pencere, widget ve pencere çerçevesi birlikte değişir'],
    ],
  },
  {
    version: '1.10.0',
    date: '2026-10-26',
    title: 'Tek depo',
    items: [
      ['better', 'Kaynak kod, kurulum dosyaları ve güncellemeler tek depoda: github.com/ati-space/xloer; güncellemeler artık oradan gelir'],
      ['better', 'Ayarlar’daki içe aktarma “CSV’den içe aktar” oldu: başka bir görev uygulamasından alınan .csv yedeği okunur'],
    ],
  },
  {
    version: '1.9.0',
    date: '2026-10-26',
    title: 'Yenilikler penceresi',
    items: [
      ['new', 'Yenilikler: soldaki ✦ düğmesi bu zamana kadar eklenen her şeyi sürüm sürüm gösterir; Ayarlar’ın altından da açılır'],
      ['new', 'Güncellemeden sonra düğmede bir nokta belirir, pencere açılınca kaybolur'],
    ],
  },
  {
    version: '1.8.0',
    date: '2026-10-25',
    title: 'Boşluksuz tarihler, veri kontrolü, müşteriler',
    items: [
      ['better', 'Excel’den gelen tek günlük testlerde iki tarih alanı da dolu; tek tarihli ama Gün / Vardiya’sı uzun testlerin bitişi hesaplanır'],
      ['new', 'Veri kontrolü: Gün / Vardiya uyuşmazlığı, düzeltilen yıl, başlıkla çelişen proje kodu, çok ileri tarih, tarihsiz satır ve eksik tutar kartta işaretlenir; tek tıkla düzeltme ve “Uyarılı” filtresi'],
      ['new', 'Cihaz adlarını birleştirme: aynı cihazın farklı yazılışları önerilir, birleştirilen ad sonraki Excel’lerde de uygulanır'],
      ['new', 'Müşteri alanı (başlıktan otomatik okunur) ve Pano’da “Müşteriler” kartı ile müşteri filtresi'],
    ],
  },
  {
    version: '1.7.0',
    date: '2026-10-25',
    title: 'Seçerek kaldırma, Türkçe takvim',
    note: 'Ayrı yayınlanmadı; 1.8.0 ile geldi.',
    items: [
      ['new', 'Faturalar’da seçerek kaldırma: kartları kutuyla seç (Shift ile aralık, üstteki kutuyla görünenlerin hepsi), “Seçilenleri kaldır (N)”'],
      ['new', 'Türkçe takvim: Pazartesi ile başlayan, Türkçe ay ve gün adlı; Windows İngilizce olsa da Türkçe açılır'],
      ['better', 'Tarihler gg.aa.yyyy yazılabilir (15.09.2026, 15092026, 15.9.26); hatalı tarih uyarıyla geri alınır'],
      ['better', 'Görevlerin bitiş tarihi de aynı Türkçe takvimi kullanır'],
    ],
  },
  {
    version: '1.6.0',
    date: '2026-10-25',
    title: 'Pano',
    items: [
      ['new', 'Faturalar’a “Pano”: aylık (yıl seçerek) ve yıllık kesilen tutar grafiği ve tablosu; proje, test günü, vardiya, tutar'],
      ['new', 'Cihaz kullanımı: her test sisteminin dönem içindeki süresi (gün ve vardiya), en çok kullanılan önce'],
      ['new', 'Bir aya veya yıla tıklayınca tüm sayılar o döneme göre süzülür'],
    ],
  },
  {
    version: '1.5.0',
    date: '2026-10-25',
    title: 'Excel düzeltmeleri, sıralama, hepsini kaldır',
    items: [
      ['fix', '“Toplam Kesilen Fatura (€)” satırındaki tutarlar okunuyor; toplam satırı test satırı sanılmıyor'],
      ['fix', 'İki parça kesilmiş faturalarda iki toplam toplanıyor; boş satırlardan sonraki toplam da bulunuyor'],
      ['fix', 'Proje kodu başlıktan alınıyor; “22.05.206” gibi yıl yazım hataları okunuyor'],
      ['fix', 'Ara başlık satırları atlanıyor; uzun cihaz adları kartı taşırmıyor'],
      ['new', 'Faturalar’da “Sırala”: No’ya veya son test tarihine göre'],
      ['new', '“Hepsini kaldır” (arama varken “Görünenleri kaldır”), “Geri al” ile'],
    ],
  },
  {
    version: '1.4.0',
    date: '2026-10-25',
    title: 'Excel’den içe aktarma',
    items: [
      ['new', 'Fatura Excel’ini (.xlsx) pencereye sürükle-bırak; projeler Faturalar’a kendiliğinden gelir'],
      ['new', 'Her blok bir kart olur: No, proje kodu, müşteri / test adı, rapor no, testler, tarihler ve tutar'],
      ['new', 'Aynı proje kodu varsa kart güncellenir; yeni cihaz ve test türleri listeye eklenir'],
      ['new', '“Excel’den al” düğmesi; bildirimdeki “Geri al” içe aktarmayı geri alır'],
    ],
  },
  {
    version: '1.3.0',
    date: '2026-10-25',
    title: 'Laboratuvar görünümü, rapor aşamaları',
    items: [
      ['new', 'Yeni görünüm “Laboratuvar”: grafit zemin, sarı vurgu, keskin köşeler, gömülü yazı tipleri'],
      ['new', 'Rapor aşamaları: Test → Veri → Rapor → Müşteri → Fatura; kenar çubuğundan aşamaya göre süzme'],
      ['new', 'Listenin üstünde sayaçlar: açık rapor, bekleyen adım, müşteride bekleyen, fatura toplamı'],
      ['new', 'Raporlara proje kodu; aynı kodlu faturanın testleri, tutarı ve “Tabloyu kopyala” rapor detayında'],
      ['better', 'Başlıkta her yerden arama; proje koduyla da bulur'],
    ],
  },
  {
    version: '1.2.0',
    date: '2026-10-25',
    title: 'Faturalar',
    items: [
      ['new', 'Faturalar: her proje için test satırları (cihaz + tarih), Gün / Vardiya kendiliğinden (gün × 3), tutar ve tüm projelerin toplamı'],
      ['new', 'Kopyalama simgesi: “Gerçekleştirilen testler ve tarihleri aşağıdaki gibidir;” tablosunu e-posta / Word için panoya koyar'],
      ['new', 'Cihaz ve test türü listesi düzenlenebilir'],
    ],
  },
  {
    version: '1.1.1',
    date: '2026-10-25',
    title: 'Otomatik yayın',
    items: [
      ['better', 'Sürümler GitHub Actions ile derlenip yayınlanıyor'],
      ['fix', 'Güncelleyicide kullanılmayan web kurulum desteği kapatıldı'],
    ],
  },
  {
    version: '1.1.0',
    date: '2026-10-25',
    title: 'Yeni logo, uygulama içi güncelleme',
    items: [
      ['new', 'Yeni Xloer logosu'],
      ['new', 'Güncelleme düğmesi: yeni sürüm çıkınca uygulama haber verir, tek tıkla indirip kurar'],
      ['new', 'Ayarlar → Güncellemeler: elle denetleme, otomatik denetimi açıp kapatma'],
      ['better', 'İlk açılıştaki örnek liste sadeleştirildi'],
    ],
  },
  {
    version: '1.0.0',
    date: '2026-10-25',
    title: 'İlk sürüm',
    items: [
      ['new', 'Listeler → bölümler → görevler → alt görevler (5 seviyeye kadar)'],
      ['new', 'Sürükle-bırak: rapor başlığını alt görevleriyle taşı, bölümleri taşı, başka listeye bırak; klavyeyle Alt+↑/↓, Tab, Shift+Tab'],
      ['new', 'Arama (Ctrl+F): Türkçe karakterlere duyarsız, etiket ve notlarda da arar'],
      ['new', 'Masaüstü widget’ı: aramalı, hızlı ekleme, her zaman üstte, kilit ve opaklık'],
      ['new', 'Bugün / Sonraki 7 Gün, etiketler, öncelik, bitiş tarihi, Tamamlanan ve Çöp Kutusu, “Geri al”'],
      ['new', 'CSV’den içe aktarma, JSON yedek ve günlük otomatik yedek'],
      ['new', 'Sistem tepsisinde çalışma, Windows açılışında başlatma'],
    ],
  },
];

// The GitHub release text for one version: a "- " line per item.
export function releaseNotes(version) {
  const entry = CHANGELOG.find((e) => e.version === version);
  return entry ? `${entry.items.map(([, text]) => `- ${text}`).join('\n')}\n` : null;
}
