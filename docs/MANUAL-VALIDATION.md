# Company Code Agent manuel kabul senaryoları

Bu senaryolar güncel VSIX paketi için kullanıcı arayüzü doğrulamasıdır. Birim testler bunların yerini tutmaz.

## Kurulum

1. Visual Studio'yu kapatın.
2. `src/CompanyCodeAgent.VisualStudio/bin/Release/CompanyCodeAgent.VisualStudio.vsix` dosyasını çift tıklayın ve kurulum sihirbazını tamamlayın.
3. Visual Studio 2022 veya daha yeni bir sürümü açın; **Extensions → Manage Extensions → Installed** altında `Company Code Agent` göründüğünü kontrol edin.
4. Bir `.sln` dosyası açın ve **Tools → Company Code Agent → Company Code Agent** ile pencereyi açın.

## LM Studio yerel bağlantısı

1. LM Studio'da modeli yükleyin.
2. **Developer** bölümünde local server'ın durumu `Running` olmalıdır.
3. Agent panelinde **LM Studio** düğmesine basın.
4. API alanının `http://127.0.0.1:1234/`, anahtar alanının boş olduğunu doğrulayın.
5. **Bağlantıyı sınama** düğmesine basın; başarı iletisi model sayısını göstermelidir.
6. **Modelleri yükle** düğmesine basıp modeli seçin.
7. `Seçili kodu açıkla` yazın veya editörde sağ tıklayıp **Company Code Agent: Seçili Kodu Açıkla** seçin. Yanıtın akarak geldiğini doğrulayın.

> API alanına `/api/v1` veya `/v1` eklemeyin. Uzantı endpoint'in sonuna OpenAI-uyumlu `v1/...` yolunu ekler.

## Güvenli kod değişikliği

1. Modu **Interactive** yapın.
2. Küçük bir değişiklik isteyin, örneğin `README.md içindeki başlığı güncelle`.
3. Agent dosya değişikliği önerdiğinde Visual Studio diff önizlemesinin açıldığını kontrol edin.
4. Onay penceresinde **Hayır** seçin; dosyanın değişmediğini doğrulayın.
5. Aynı isteği tekrarlayın ve **Evet** seçin; dosyanın değiştiğini ve checkpoint kaydı oluştuğunu doğrulayın.
6. **Checkpoint'ler** ardından **Geri al…** ile checkpoint'i geri yükleyin; dosyanın önceki haline döndüğünü kontrol edin.

## Plan → Act

1. Modu **Plan** yapın ve bir geliştirme isteği gönderin.
2. Planın yalnızca keşif araçlarını kullandığını, dosya/terminal değişikliğinin olmadığını doğrulayın.
3. **Planı Act'e aktar** düğmesine basın.
4. Panelin **Interactive** moda geçtiğini ve planın düzenlenebilir giriş alanına eklendiğini doğrulayın.
5. Kullanıcı **Gönder** düğmesine basmadan işlem yapılmadığını kontrol edin.

## Sınırlar ve denetim

- `Ctrl+Enter` isteği göndermeli; `Esc` çalışan isteği durdurmalıdır.
- Düşük bir **Token** değeriyle uzun görev çalıştırıldığında yeni araç turu token bütçesinde durmalıdır.
- `.company-agent/policy.json` içinde `blockedTools` ile engellenen araçların Autopilot'ta bile çalışmadığını doğrulayın.
- `allowedCommandPrefixes` tanımlıyken izin dışı veya `&&` içeren terminal komutunun reddedildiğini doğrulayın.
- **Audit**, **Kullanım**, **Geçmiş**, **Sohbetler** ve **Dışa aktar** düğmelerinin aktif sohbet dalı için çalıştığını doğrulayın.

## Kod inceleme

1. Git değişikliği olan bir solution açın.
2. **Tools → Company Code Agent → Agent: Değişiklikleri İncele** komutunu seçin.
3. Bulgularda görünüyorsa **Bulgulara git** bağlantısına tıklayın; ilgili çözüm içi dosyanın doğru satırda açıldığını doğrulayın.
