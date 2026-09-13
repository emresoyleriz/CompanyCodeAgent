using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace CompanyCodeAgent.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Company Code Agent", "Şirket içi coding-agent", "0.2.0")]
[ProvideToolWindow(typeof(AgentToolWindow))]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuid)]
public sealed class CompanyCodeAgentPackage : AsyncPackage
{
    public const string PackageGuid = "e50bb793-a2bd-470e-8a02-5f31829e63ba";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService == null) return;
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt(null), new CommandID(new Guid(CommandSetGuid), ShowToolWindowCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Seçili kodu açıkla; sorumluluklarını, akışını ve olası riskleri maddeler halinde anlat."), new CommandID(new Guid(CommandSetGuid), ExplainSelectionCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Aktif dosyayı ve ilgili hata bağlamını incele. Önce kısa planı çıkar, sonra Act modunda güvenli düzeltme öner."), new CommandID(new Guid(CommandSetGuid), FixActiveFileCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Kod incelemesi yap. Önce get_git_branch, get_git_diff ve get_git_staged_diff çağır. Yalnızca değişiklikten kaynaklanan gerçek hata, güvenlik açığı veya test eksikliği bildir; stil tercihi ve belirsiz spekülasyon yazma. Her bulguyu şu kesin biçimde ver: [ÖNEM: kritik|yüksek|orta|düşük] `dosya:SATIR` — kısa başlık; Neden: ...; Öneri: .... Bulgu yoksa yalnızca `BULGU YOK — incelenen diff'te doğrulanabilir sorun bulunmadı.` yaz.", "Plan"), new CommandID(new Guid(CommandSetGuid), ReviewChangesCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Seçili kod ve aktif dosya bağlamıyla sorumu yanıtla. Gerekirse önce ilgili kodu ve tanıları incele."), new CommandID(new Guid(CommandSetGuid), AskSelectionContextCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Seçili kodu açıkla; sorumluluklarını, akışını ve olası riskleri maddeler halinde anlat."), new CommandID(new Guid(CommandSetGuid), ExplainSelectionContextCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Seçili kodu ve ilgili hata bağlamını incele. Önce kısa planı çıkar, sonra Act modunda güvenli düzeltme öner."), new CommandID(new Guid(CommandSetGuid), FixSelectionContextCommandId)));
        commandService.AddCommand(new MenuCommand((_, _) => ShowWithPrompt("Solution Explorer'da seçili öğeyi ve ilgili dosyaları incele. Yapısını, bağımlılıklarını, risklerini ve gerekli sonraki adımları kısa bir planla açıkla.", "Plan"), new CommandID(new Guid(CommandSetGuid), InspectSolutionItemCommandId)));
    }

    private void ShowWithPrompt(string prompt, string mode = null)
    {
        JoinableTaskFactory.Run(async () =>
        {
            var window = await ShowToolWindowAsync(typeof(AgentToolWindow), 0, true, DisposalToken) as AgentToolWindow;
            if (!string.IsNullOrWhiteSpace(prompt)) window?.SetPrompt(prompt, mode);
        });
    }

    public const string CommandSetGuid = "b2c53c74-679f-43cd-b2ea-56e215ae2857";
    public const int ShowToolWindowCommandId = 0x0100;
    public const int ExplainSelectionCommandId = 0x0110;
    public const int FixActiveFileCommandId = 0x0120;
    public const int ReviewChangesCommandId = 0x0130;
    public const int AskSelectionContextCommandId = 0x0140;
    public const int ExplainSelectionContextCommandId = 0x0150;
    public const int FixSelectionContextCommandId = 0x0160;
    public const int InspectSolutionItemCommandId = 0x0170;
}
