# Company Code Agent yol haritası

Bu ürün Cline'dan bağımsız, C#/.NET ile yazılmış Visual Studio coding agent'ıdır. Cline kaynak kodu, adı, tasarımı veya prompt'ları kullanılmaz.

## Durum

| Paket | Durum | Kabul kriteri |
|---|---|---|
| P0 — Solution ve VSIX kabuğu | Tamam | Derlenen solution, yüklenebilir VSIX ve Tool Window |
| P1 — LLM temel akışı | Tamam | Model listeleme ve OpenAI-uyumlu SSE istemcisi |
| P2 — IDE ↔ Host iletişimi | Tamam | Kullanıcıya özel Named Pipe ile onaylı tool istekleri ve paketlenmiş host |
| P3 — Context ve araçlar | Devam ediyor | Aktif dosya, seçim, dosya okuma/arama, Git, build/test |
| P4 — Güvenli dosya değişikliği | Devam ediyor | Patch preview, açık Accept/Reject, checkpoint |
| P5 — Agent modu | Devam ediyor | Plan/Act/Autopilot, JSON tool calling, özel ajan profilleri, iptal ve token takibi |
| P6 — Checkpoint ve geçmiş | Devam ediyor | SQLite oturumları, görev geri alma, audit kaydı |
| P7 — Genişletilebilirlik | Planlandı | MCP, web araçları, kurallar, görsel girdi, export |
| P8 — Kurumsal dağıtım | Planlandı | Merkezi politika, rol, imzalama, proxy/sertifika |

## P2: IDE ↔ Host iletişimi

1. Kullanıcı hesabına kısıtlı yerel Named Pipe üzerinden tek istek/tek yanıt sözleşmesi kur.
2. `Protocol` projesindeki versioned mesaj sözleşmeleri üzerinden JSON-RPC kur.
3. VSIX, host yoksa paketlenmiş yerel host'u başlatır; bozuk/boş istemci bağlantısı host'u sonlandırmaz.
4. Tool Window aktif solution, dosya ve seçimi `ChatRequest` olarak gönderir.
5. Host streaming `AgentEvent` mesajlarını VSIX'e iletir; pencere anlık güncellenir.
6. API anahtarı, Windows kullanıcı hesabına bağlı DPAPI ile yerel ayar deposunda şifrelenir; loglarda maskelenir.

## P3: araç sözleşmesi ve güvenlik

Araçlar yalnızca açık solution kökünde çalışır. İlk araç seti:

- `list_files`, `search_files`, `read_file`, `read_multiple_files`
- `get_diagnostics`, `build_solution`, `run_tests`, `get_git_diff`
- `apply_patch`, `write_file`, `run_command`

Yazma ve komut araçları varsayılan olarak onay bekler. Workspace dışına çıkma, gizli dosya sızıntısı ve tehlikeli komutlar policy katmanında engellenir.

## P4–P6: kullanıcı deneyimi

- Plan ve Act modları ayrı görünür.
- Her değişiklik önce geçici dosyada hazırlanır ve Visual Studio diff penceresinde açılır.
- Kullanıcı dosya veya işlem bazında Accept/Reject seçer.
- Checkpoint görev başlangıcındaki Git durumunu ve agent değişikliklerini ayrı saklar.
- İptal, maksimum adım, süre ve token sınırı her görevde zorunludur.

## Ürün paritesi kontrol listesi

Bu liste Cline ve GitHub Copilot'un herkese açık IDE iş akışlarının clean-room karşılığıdır. Kapalı kaynak modeller, GitHub bulut ajanı ya da marka/arayüz kopyalanmaz; aynı kullanıcı sonucunu sağlayan özgün C# bileşenleri üretilir.

| Akış | Mevcut durum | Teslim kabul kriteri |
|---|---|---|
| Çok turlu chat ve streaming | Kısmi | Kalıcı oturum geçmişi, seçilebilir sohbet dalları, Markdown/JSON konuşma exportu, durdur, yeniden dene, token görünürlüğü, maksimum token bütçesi, kullanıcı tanımlı yaklaşık maliyet ve süre/adım sınırı var; sağlayıcı-fiyatı tabanlı ayrıntılı faturalama eksik |
| Plan → Act | Kısmi | Plan'da salt-okunur keşif, ayrı Plan/Act model seçimi ve kullanıcı gönderiminden önce düzenlenebilir Plan → Interactive aktarımı var; görev kalemi düzeyinde takip eksik |
| Interactive / Autopilot | Kısmi | Araç bazlı onay, güvenlik politikasıyla sınırlı Autopilot, kalıcı yalnızca build/test otomatik-onay profili ve kesin adım/süre sınırı var; proje kuralı tabanlı daha ayrıntılı izin profilleri eksik |
| Dosya değişiklikleri | Kısmi | Tek dosya için VS diff görünümü ve Accept/Reject, en fazla 20 dosyada atomik exact-patch transaction ve ortak checkpoint var; bölüm bazlı seçim eksik |
| Checkpoint | Kısmi | Her değişiklik öncesi snapshot, zaman çizelgesi, karşılaştır/geri al, görev bağlamı geri alma |
| IDE bağlamı | Kısmi | Aktif dosya/seçim, çözüm içindeki açık editör envanteri, `get_diagnostics` ile güncel Error List ve Output panelinden sınırlı/redacted son build-test çıktısı var; canlı debug/watch bağlamı eksik |
| Kod inceleme | Kısmi | Branch/staged/unstaged diff üzerinde önem ve dosya:satır biçimli bulgular var; bulgulara tıklayıp editörde ilgili satıra gitme var, öneriyi tek tık uygulama eksik |
| Kurallar / prompt / custom agents | Kısmi | Kural dosyaları, aktif dosya için glob tabanlı yol-deseni kuralları, `/deep-planning`, proje prompt/skill dosyaları ve kaynak denetimli özel ajan profilleri var; kuruluş çapında merkezi kural dağıtımı eksik |
| Model sağlayıcıları | Kısmi | OpenAI-uyumlu akış, LM Studio yerel profil düğmesi, Plan/Act model ayrımı ve token kullanım kaydı var; sağlayıcı-fiyatı tabanlı maliyet hesabı eksik |
| MCP / haricî araç | Kısmi | İzinli stdio ve HTTPS MCP, güvenli HTTPS web fetch, görsel ek ve allowlist/onay/audit var; OAuth ve ayar arayüzü eksik |
| Git ve görev yönetimi | Kısmi | Diff/status/staged diff, görev listesi, izole worktree ve yalnızca kullanıcıca stage edilmiş dosyalarla commit var; PR hazırlığı ve uzak sağlayıcı entegrasyonu eksik |
| Güvenlik ve kurum | Kısmi | Araç bloklama politikası, terminal command-prefix allowlist'i, secret redaction, audit görünümü ve kullanıcı onaylı yerel JSON export var; merkezi dağıtım, proxy/sertifika ve imzalama eksik |

## Doğrulama standardı

Her satır; birim test, host/VSIX entegrasyon testi, iptal ve hatalı bağlantı testi, güvenlik policy testi ve Visual Studio Experimental Instance manuel senaryosu geçmeden `Tamam` durumuna alınmaz.

## P7–P8: özellik ve kurum paritesi

- MCP istemcisi ve kurumun izin verdiği sunucular
- Web/browser aracı, görsel ek ve proje kuralları
- Konuşma geçmişi, arama, export ve kullanım istatistikleri
- Merkezi model/komut izinleri, audit export, TLS proxy, sertifika ve imzalı VSIX

## Definition of Done

Bir paketin tamamlanması için unit ve integration testleri, başarısız bağlantı/iptal senaryoları, güvenlik policy testleri ve Visual Studio Experimental Instance üzerinde manuel doğrulama gereklidir.
