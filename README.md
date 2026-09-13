Kurulum, LM Studio bağlantısı ve Visual Studio üzerinde manuel kabul adımları için [manuel doğrulama senaryolarına](docs/MANUAL-VALIDATION.md) bakın.

Kurulacak paket: `src/CompanyCodeAgent.VisualStudio/bin/Release/CompanyCodeAgent.VisualStudio.vsix`.

## Çalışan özellikler

- OpenAI-uyumlu model listeleme ve SSE streaming chat
- Tek sağlayıcı altında Plan ve Act/Autopilot için ayrı model seçimi
- Panelden ayarlanabilir görev süresi (1–60 dk) ve agent adım sınırı (1–20)
- Şifreli yerel API anahtarı saklama (Windows kullanıcı hesabına bağlı DPAPI)
- Aktif solution, dosya, seçili kod ve istek sırasında yenilenebilen Visual Studio Error List bağlamı
- Plan ve Act modları
- Yerel agent host + kullanıcıya özel Named Pipe iletişimi
- Workspace sınırı, secret maskeleme ve tehlikeli komut politikası
- Dosya listeleme/arama/okuma/çoklu okuma, dosya yazma, exact patch ve silme
- Build, test, terminal, Git status/diff, checkpoint ve geri alma
- Visual Studio iki-panelli diff önizlemesiyle Accept/Reject onayı, SQLite audit/checkpoint/kalıcı oturum geçmişi
- `AGENTS.md` ve `.company-agent/rules.md` proje kuralları
- Kaynak denetiminde paylaşılabilen özel ajan profilleri (`.company-agent/agents` ve `.github/agents`)

## Geliştirme doğrulaması

Visual Studio 2022 Developer PowerShell veya normal PowerShell'de:

```powershell
dotnet test CompanyCodeAgent.sln
```

VSIX paketi:

```text
src\CompanyCodeAgent.VisualStudio\bin\Debug\CompanyCodeAgent.VisualStudio.vsix
```

VSIX projesini Visual Studio 2022'de başlangıç projesi yapıp `F5` ile çalıştırın. Experimental Instance açılır; **Tools → Company Code Agent** ile paneli açın.

## Sağlayıcı ayarı

Panelde API taban adresini (ör. `https://llm.company.local/`), token’ı ve modeli girin. Sağlayıcının aşağıdaki OpenAI-uyumlu uçları sunması gerekir:

- `GET /v1/models`
- `POST /v1/chat/completions` (`stream: true`, SSE)

Anahtar proje dosyalarına yazılmaz; yalnızca kullanıcının yerel Windows profilinde şifreli tutulur.

## Güvenlik sınırları

Agent yalnızca açık solution klasöründe çalışır. Yazma, patch, silme, komut, build, test ve checkpoint geri alma işlemleri kullanıcı onayı ister. Komutlar iki dakika ve 64 KB çıktı sınırıyla çalışır; yıkıcı komut desenleri engellenir. `.env`, sertifika, private-key ve bilinen secret dosyaları agent araçlarına kapalıdır.

Proje politikası için isteğe bağlı `.company-agent/policy.json` oluşturabilirsiniz:

```json
{ "blockedTools": ["RunCommand", "WebFetch", "McpCallTool"] }
```

Engellenen araçlar kullanıcı onayı veya Autopilot seçimiyle de çalıştırılamaz.

Terminal komutlarını izinli öneklerle sınırlamak için aynı dosyada `allowedCommandPrefixes` kullanın:

```json
{
  "allowedCommandPrefixes": ["dotnet", "git status", "git diff"]
}
```

Bu liste etkinse yalnızca listedeki önekle başlayan komutlar çalışır; `&&`, `|`, `;`, `&` ve çok satırlı shell zincirleri reddedilir. `build_solution` ve `run_tests` sırasıyla `dotnet build` ve `dotnet test` kullandığından, allowlist kullanırken `dotnet` öneğini ekleyin.

## Audit dışa aktarma

Agent, `export_audit({"path":"artifacts/audit.json"})` ile mevcut workspace içindeki `.json` hedefine oturum audit kaydını dışa aktarabilir. Bu işlem açık kullanıcı onayı ister; kayıtlar zaten secret-redacted biçimde saklanır ve araç herhangi bir uzak hedefe veri göndermez.

## Token ve maliyet görünürlüğü

Paneldeki **Kullanım** düğmesi aktif sohbet dalındaki token kullanımını ve istek sayısını model bazında gösterir. Her model çağrısı ayrı kaydedilir; kullanım bilgisi vermeyen yerel sağlayıcı çağrıları da istek sayısına dahildir. Ücretli bir sağlayıcı kullanılıyorsa üstteki **USD / 1M** alanına toplam bir milyon token için fiyat girilebilir; gösterilen maliyet yaklaşık değerdir ve model sağlayıcısından fiyat veya fatura verisi çekmez. Yerel modeller için alanı `0` bırakın. **Token** alanı görev başına bütçeyi belirler: değer OpenAI-uyumlu istekte `max_tokens` olarak gönderilir ve kullanım bilgisi gelmese dahi çıktı boyutuna göre muhafazakâr tahmini sayaç yeni araç turunu durdurur.

## Kullanım

Paneldeki **Kullanım** düğmesi, mevcut solution oturumunda model bazında kaydedilen token tüketimini gösterir. Token kaydı yerel agent veritabanında tutulur. Sağlayıcıların fiyatlandırması farklı olduğundan bu görünüm maliyet tahmini değil, ölçülen token kullanımını verir.

## Sohbet dalları

Paneldeki **Yeni sohbet** düğmesi aktif solution için yeni, ayrı bir konuşma oturumu başlatır. Önceki sohbet, görevler, checkpoint’ler, audit ve kullanım kayıtları silinmez. **Sohbetler** düğmesiyle önceki bir dal seçilip tekrar açılabilir; **Geçmiş** düğmesi yalnızca aktif sohbet dalını gösterir. **Dışa aktar** düğmesi, kullanıcının seçtiği yerel dosyaya aktif dalı Markdown veya ham JSON olarak kaydeder; bu işlem hiçbir veriyi ağa göndermez.

## Plan → Act

**Plan** modunda istek gönderin; agent yalnızca keşif araçlarını kullanarak yaklaşımı üretir. Sonuç uygun olduğunda **Planı Act'e aktar** düğmesi planı düzenlenebilir giriş alanına taşır ve modu **Interactive** yapar. Kullanıcı metni gözden geçirip **Gönder** demeden hiçbir dosya değişikliği veya komut çalışmaz.

## Onay profili

**Onay** alanındaki varsayılan **Her işlemi sor** seçeneği tüm etkili araçlar için onay ister. **Doğrulama otomatik** seçeneği yalnızca `build_solution` ve `run_tests` çağrılarını otomatik onaylar. Dosya yazma/silme, genel terminal komutu, Git commit, checkpoint geri alma, MCP, web erişimi ve audit dışa aktarımı bu profilde de kullanıcı onayı ister.

## Dosya yolu kuralları

Aktif dosyaya göre ek proje talimatı yüklemek için `.company-agent/rules.paths` dosyasını kullanın. Her satır `glob=rule-dosyası` biçimindedir:

```text
src/**/*.cs=.company-agent/rules/csharp.md
tests/**=.company-agent/rules/tests.md
```

Yorumlar `#` ile başlar. Eşleşen kural dosyaları yalnızca solution kökü içinden, 64 KB sınırıyla okunur.

## Çoklu dosya değişiklikleri

Agent, `apply_multi_patch` ile en fazla 20 mevcut dosyada bir transaction olarak exact patch uygulayabilir. Çağrıdaki her öğe `path`, `expected` ve `replacement` içerir. Tüm `expected` metinleri önce tekil olarak doğrulanır; biri uyuşmazsa hiçbir dosya değiştirilmez. İşlem tek kullanıcı onayı ve tüm etkilenen dosyaları kapsayan tek checkpoint ile yürür.

## MCP sunucuları

İsteğe bağlı stdio veya HTTPS MCP sunucularını proje kökündeki `.company-agent/mcp.json` dosyasında açıkça tanımlayın. Her çağrı araç bazlı onay ister ve `allowedTools` dışında çağrı yapılamaz:

```json
{
  "servers": [
    {
      "name": "docs",
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-example"],
      "allowedTools": ["search_docs"]
    }
  ]
}
```

## LM Studio ile yerel model

LM Studio’da bir modeli yükleyin ve **Developer** ekranından local server’ı başlatın. Paneldeki **LM Studio** düğmesi endpoint’i otomatik olarak `http://127.0.0.1:1234/` yapar, API anahtarını temizler ve OpenAI-uyumlu `/v1/models` listesini çağırır. Model listeden seçildikten sonra sohbet başlatılabilir. **Bağlantıyı sınama** düğmesi endpoint’in erişilebilir olduğunu ve kaç model bildirdiğini gösterir; geçici ağ/5xx hatalarında model listesi isteği bir kez yeniden denenir. `api/v1` adresini panelde yazmayın; uzantı OpenAI-uyumlu `v1` yolunu kendisi ekler.

Uzak MCP için `command` yerine HTTPS `url` kullanın:

```json
{
  "servers": [
    {
      "name": "team-docs",
      "url": "https://mcp.example.com/mcp",
      "allowedTools": ["search_docs", "get_page"]
    }
  ]
}
```

Agent önce `mcp_list_tools`, ardından yalnızca izinli araçlar için `mcp_call_tool` isteyebilir. Boş `allowedTools` listesi hiçbir aracın çağrılmasına izin vermez. HTTPS hedefleri genel HTTPS adresi olmalıdır; OAuth veya kalıcı kimlik bilgisi akışları henüz yapılandırma yüzeyine eklenmemiştir.

## Prompt ve skill dosyaları

Sohbete `/deep-planning <istek>` yazarak yerleşik derin planlamayı çağırın. Proje tanımlı çağrılar için şu dosya yollarından birini kullanın; örneğin `/security-review ödeme modülünü incele`:

- `.company-agent/prompts/security-review.md`
- `.github/prompts/security-review.prompt.md`
- `.company-agent/skills/security-review/SKILL.md`

Dosya adı yalnızca harf, sayı, `_` ve `-` içerebilir; 64 KB üzerindeki dosyalar bağlama alınmaz.

## Kod inceleme

**Tools → Company Code Agent → Review Changes** komutu branch ile staged/unstaged Git diff'ini Plan modunda inceler. Bulgular önem seviyesi ve `dosya:satır` konumuyla döner; sonuç altındaki **Bulgulara git** bağlantısına tıklamak ilgili dosyayı Visual Studio'da güvenle o satırda açar. Değişiklikten kaynaklanan doğrulanabilir hata, güvenlik açığı veya test eksikliği yoksa açıkça `BULGU YOK` sonucu verir.

Kod editöründe sağ tıklayınca **Company Code Agent: Seçime Sor**, **Seçili Kodu Açıkla** ve **Seçimi İncele ve Düzelt** komutları görünür. Komutlar Tool Window’u açar ve seçili kod/aktif dosya bağlamını otomatik ekler; düzeltme talebi de kullanıcı **Gönder** demeden çalıştırılmaz.

Solution Explorer’da bir proje, klasör veya dosyaya sağ tıklayınca **Company Code Agent: Bu Öğeyi İncele** komutu görünür. Seçilen öğe agent bağlamına eklenir ve inceleme Plan modunda açılır.

## Özel ajan profilleri

Proje kökünde `.company-agent/agents` veya `.github/agents` altında Markdown profil dosyaları oluşturun. Paneldeki **Ajan** listesinden seçilen dosya, her isteğe uzmanlık talimatı olarak eklenir. Örnek: `.company-agent/agents/security-reviewer.md`.

Bu profiller yalnızca proje içinde okunur, alt klasör taraması yapılmaz ve 64 KB sınırı uygulanır. Profil, workspace, secret ve kullanıcı onayı güvenlik sınırlarını değiştiremez.

Aktif dosyaya göre ek kurallar için `.company-agent/rules` içine `all.md`, uzantı için `cs.md` veya tek dosya için `Program.cs.md` koyabilirsiniz. Yalnızca aktif dosyayla eşleşen bu küçük kural dosyaları sistem bağlamına eklenir.

## Bağlam ifadeleri

Sohbet içinde aşağıdaki ifadeleri kullanabilirsiniz:

- `@file:src/Program.cs` — dosya içeriğini bağlama ekler.
- `@folder:src` — klasörün güvenli dosya listesini ekler.
- `@problems` — Visual Studio Error List tanılarını ekler.
- `@image:docs/screen.png` — PNG, JPEG, GIF veya WebP görselini çok-modlu modele ekler.

Workspace dışı, hassas veya büyük dosyalar bu bağlama alınmaz.

Görsel ekleri 5 MB ile sınırlıdır ve seçtiğiniz sağlayıcının OpenAI-uyumlu görsel mesajlarını desteklemesi gerekir.

## Güvenli web bağlamı

Agent, ihtiyaç duyduğunda `web_fetch({"url":"https://docs.example.com"})` isteyebilir. Çağrı onay bekler; yalnızca HTTPS metin içeriği okunur. `localhost`, özel IP blokları, `.local` alan adları ve bunlara yönlendirmeler engellenir.

## İzole Git worktree

Agent `list_git_worktrees` ile mevcut alanları inceleyebilir. `create_git_worktree({"branch":"feature/auth"})` onay sonrası yeni branch ve izole worktree oluşturur. Worktree ana solution altında değil, kullanıcının yerel `CompanyCodeAgent/worktrees` alanında tutulur.

`create_git_commit({"message":"feat: add auth"})` ayrıca kullanıcı onayıyla yalnızca Git index'te zaten stage edilmiş değişiklikleri commit eder; agent hiçbir zaman otomatik `git add` çalıştırmaz.
