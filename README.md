# Company Code Agent

Company Code Agent; C# ile yazılmış, Visual Studio 2022 için paketlenen bağımsız coding-agent uzantısıdır. Cline veya GitHub Copilot kaynak kodu, markası ya da özel bulut hizmetleri kullanılmaz.

## Çalışan özellikler

- OpenAI-uyumlu model listeleme ve SSE streaming chat
- Tek sağlayıcı altında Plan ve Act/Autopilot için ayrı model seçimi
- Panelden ayarlanabilir görev süresi (1–60 dk) ve agent adım sınırı (1–20)
- Şifreli yerel API anahtarı saklama (Windows kullanıcı hesabına bağlı DPAPI)
- Aktif solution, dosya ve seçili kod bağlamı
- Plan ve Act modları
- Yerel agent host + kullanıcıya özel Named Pipe iletişimi
- Workspace sınırı, secret maskeleme ve tehlikeli komut politikası
- Dosya listeleme/arama/okuma/çoklu okuma, dosya yazma, exact patch ve silme
- Build, test, terminal, Git status/diff, checkpoint ve geri alma
- Visual Studio iki-panelli diff önizlemesiyle Accept/Reject onayı, SQLite audit/checkpoint/kalıcı oturum geçmişi
- `AGENTS.md` ve `.company-agent/rules.md` proje kuralları

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

## MCP sunucuları

İsteğe bağlı stdio MCP sunucularını proje kökündeki `.company-agent/mcp.json` dosyasında açıkça tanımlayın. Her çağrı araç bazlı onay ister ve `allowedTools` dışında çağrı yapılamaz:

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

Agent önce `mcp_list_tools`, ardından yalnızca izinli araçlar için `mcp_call_tool` isteyebilir. Boş `allowedTools` listesi hiçbir aracın çağrılmasına izin vermez.

## Prompt ve skill dosyaları

Sohbete `/deep-planning <istek>` yazarak yerleşik derin planlamayı çağırın. Proje tanımlı çağrılar için şu dosya yollarından birini kullanın; örneğin `/security-review ödeme modülünü incele`:

- `.company-agent/prompts/security-review.md`
- `.github/prompts/security-review.prompt.md`
- `.company-agent/skills/security-review/SKILL.md`

Dosya adı yalnızca harf, sayı, `_` ve `-` içerebilir; 64 KB üzerindeki dosyalar bağlama alınmaz.

## Bağlam ifadeleri

Sohbet içinde aşağıdaki ifadeleri kullanabilirsiniz:

- `@file:src/Program.cs` — dosya içeriğini bağlama ekler.
- `@folder:src` — klasörün güvenli dosya listesini ekler.
- `@problems` — Visual Studio Error List tanılarını ekler.

Workspace dışı, hassas veya büyük dosyalar bu bağlama alınmaz.

## Güvenli web bağlamı

Agent, ihtiyaç duyduğunda `web_fetch({"url":"https://docs.example.com"})` isteyebilir. Çağrı onay bekler; yalnızca HTTPS metin içeriği okunur. `localhost`, özel IP blokları, `.local` alan adları ve bunlara yönlendirmeler engellenir.

## İzole Git worktree

Agent `list_git_worktrees` ile mevcut alanları inceleyebilir. `create_git_worktree({"branch":"feature/auth"})` onay sonrası yeni branch ve izole worktree oluşturur. Worktree ana solution altında değil, kullanıcının yerel `CompanyCodeAgent/worktrees` alanında tutulur.

`create_git_commit({"message":"feat: add auth"})` ayrıca kullanıcı onayıyla yalnızca Git index'te zaten stage edilmiş değişiklikleri commit eder; agent hiçbir zaman otomatik `git add` çalıştırmaz.
