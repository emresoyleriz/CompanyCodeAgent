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

## Audit dışa aktarma

Agent, `export_audit({"path":"artifacts/audit.json"})` ile mevcut workspace içindeki `.json` hedefine oturum audit kaydını dışa aktarabilir. Bu işlem açık kullanıcı onayı ister; kayıtlar zaten secret-redacted biçimde saklanır ve araç herhangi bir uzak hedefe veri göndermez.

## Kullanım

Paneldeki **Kullanım** düğmesi, mevcut solution oturumunda model bazında kaydedilen token tüketimini gösterir. Token kaydı yerel agent veritabanında tutulur. Sağlayıcıların fiyatlandırması farklı olduğundan bu görünüm maliyet tahmini değil, ölçülen token kullanımını verir.

## Sohbet dalları

Paneldeki **Yeni sohbet** düğmesi aktif solution için yeni, ayrı bir konuşma oturumu başlatır. Önceki sohbet, görevler, checkpoint’ler, audit ve kullanım kayıtları silinmez. **Sohbetler** düğmesiyle önceki bir dal seçilip tekrar açılabilir; **Geçmiş** düğmesi yalnızca aktif sohbet dalını gösterir.

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

**Tools → Company Code Agent → Review Changes** komutu branch ile staged/unstaged Git diff'ini Plan modunda inceler. Bulgular önem seviyesi ve `dosya:satır` konumuyla döner; değişiklikten kaynaklanan doğrulanabilir hata, güvenlik açığı veya test eksikliği yoksa açıkça `BULGU YOK` sonucu verir.

## Özel ajan profilleri

Proje kökünde `.company-agent/agents` veya `.github/agents` altında Markdown profil dosyaları oluşturun. Paneldeki **Ajan** listesinden seçilen dosya, her isteğe uzmanlık talimatı olarak eklenir. Örnek: `.company-agent/agents/security-reviewer.md`.

Bu profiller yalnızca proje içinde okunur, alt klasör taraması yapılmaz ve 64 KB sınırı uygulanır. Profil, workspace, secret ve kullanıcı onayı güvenlik sınırlarını değiştiremez.

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
