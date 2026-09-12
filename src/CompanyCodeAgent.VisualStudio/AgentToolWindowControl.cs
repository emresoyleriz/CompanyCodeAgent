using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace CompanyCodeAgent.VisualStudio;

public sealed class AgentToolWindowControl : UserControl
{
    private static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private readonly TextBox _endpoint = new() { MinWidth = 180 };
    private readonly PasswordBox _apiKey = new() { MinWidth = 130 };
    private readonly ComboBox _models = new() { MinWidth = 140, IsEditable = true };
    private readonly CheckBox _separateModels = new() { Content = "Plan/Act ayrı model", Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ComboBox _planModel = new() { MinWidth = 120, IsEditable = true };
    private readonly ComboBox _actModel = new() { MinWidth = 120, IsEditable = true };
    private readonly TextBox _maxSteps = new() { MinWidth = 42, Text = "5" };
    private readonly TextBox _timeoutMinutes = new() { MinWidth = 42, Text = "10" };
    private readonly ComboBox _mode = new() { MinWidth = 90, ItemsSource = new[] { "Plan", "Interactive", "Autopilot" }, SelectedIndex = 0 };
    private readonly ComboBox _agentProfile = new() { MinWidth = 115, IsEditable = false };
    private readonly RichTextBox _conversation = new() { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBox _input = new() { MinHeight = 92, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button _send = new() { Content = "Gönder", MinWidth = 95 };
    private readonly Button _retry = new() { Content = "Tekrarla", MinWidth = 75 };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Text = "Hazır" };
    private AgentSettings _settings;
    private CancellationTokenSource _cancellation;
    private string _lastPrompt = string.Empty;

    public AgentToolWindowControl()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings = AgentSettingsStore.Load();
        _endpoint.Text = _settings.Endpoint;
        _apiKey.Password = _settings.GetApiKey();
        _models.Text = _settings.Model;
        _separateModels.IsChecked = _settings.UseSeparateModeModels;
        _planModel.Text = string.IsNullOrWhiteSpace(_settings.PlanModel) ? _settings.Model : _settings.PlanModel;
        _actModel.Text = string.IsNullOrWhiteSpace(_settings.ActModel) ? _settings.Model : _settings.ActModel;
        _maxSteps.Text = Clamp(_settings.MaxAgentSteps, 1, 20).ToString();
        _timeoutMinutes.Text = Clamp(_settings.TimeoutMinutes, 1, 60).ToString();
        _agentProfile.Items.Add("Genel");
        _agentProfile.SelectedIndex = 0;
        RefreshAgentProfiles(VisualStudioContextProvider.GetWorkspacePath());
        if (_agentProfile.Items.Cast<object>().OfType<string>().Any(profile => string.Equals(profile, _settings.AgentProfile, StringComparison.OrdinalIgnoreCase)))
            _agentProfile.SelectedItem = _settings.AgentProfile;
        ApplyTheme();
        var root = new Grid { Margin = new Thickness(10), Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "COMPANY CODE AGENT", Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250)), FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 6) });
        top.Children.Add(CreateHeader()); Grid.SetRow(top, 0); root.Children.Add(top);
        Grid.SetRow(_conversation, 1); root.Children.Add(_conversation);
        Grid.SetRow(_input, 2); root.Children.Add(_input);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var cancel = StyledButton("Durdur", 70); cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => _cancellation?.Cancel();
        _send.Click += SendClicked;
        _retry.Click += RetryClicked; _retry.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(_send, Dock.Right); DockPanel.SetDock(_retry, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
        footer.Children.Add(_send); footer.Children.Add(_retry); footer.Children.Add(cancel); footer.Children.Add(_status);
        Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        Write("Hazır. Aktif dosya ve seçili kod bağlama otomatik eklenir. Plan modunda önce yaklaşımı üretin; Act modunda onaylı araçlarla uygulayın.\n\n", Brushes.LightSteelBlue);
    }

    private FrameworkElement CreateHeader()
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(Labeled("API", _endpoint)); panel.Children.Add(Labeled("Anahtar", _apiKey)); panel.Children.Add(Labeled("Model", _models)); panel.Children.Add(_separateModels); panel.Children.Add(Labeled("Plan modeli", _planModel)); panel.Children.Add(Labeled("Act modeli", _actModel)); panel.Children.Add(Labeled("Mod", _mode)); panel.Children.Add(Labeled("Ajan", _agentProfile)); panel.Children.Add(Labeled("Adım", _maxSteps)); panel.Children.Add(Labeled("Dakika", _timeoutMinutes));
        var models = StyledButton("Modelleri yükle", 110); models.Margin = new Thickness(4);
        models.Click += LoadModelsClicked; panel.Children.Add(models);
        var history = StyledButton("Geçmiş", 70); history.Margin = new Thickness(4);
        history.Click += HistoryClicked; panel.Children.Add(history);
        var newChat = StyledButton("Yeni sohbet", 85); newChat.Margin = new Thickness(4);
        newChat.Click += NewChatClicked; panel.Children.Add(newChat);
        var conversations = StyledButton("Sohbetler", 75); conversations.Margin = new Thickness(4);
        conversations.Click += ConversationsClicked; panel.Children.Add(conversations);
        var tasks = StyledButton("Görevler", 70); tasks.Margin = new Thickness(4);
        tasks.Click += TasksClicked; panel.Children.Add(tasks);
        var checkpoints = StyledButton("Checkpoint'ler", 95); checkpoints.Margin = new Thickness(4);
        checkpoints.Click += CheckpointsClicked; panel.Children.Add(checkpoints);
        var restore = StyledButton("Geri al…", 72); restore.Margin = new Thickness(4);
        restore.Click += RestoreCheckpointClicked; panel.Children.Add(restore);
        var audit = StyledButton("Audit", 60); audit.Margin = new Thickness(4);
        audit.Click += AuditClicked; panel.Children.Add(audit);
        var usage = StyledButton("Kullanım", 72); usage.Margin = new Thickness(4);
        usage.Click += UsageClicked; panel.Children.Add(usage); return panel;
    }

    private void RefreshAgentProfiles(string workspacePath)
    {
        var selected = _agentProfile.SelectedItem as string ?? "Genel";
        var profiles = FindAgentProfiles(workspacePath).ToArray();
        if (profiles.All(profile => !string.Equals(profile, selected, StringComparison.OrdinalIgnoreCase))) selected = "Genel";
        _agentProfile.Items.Clear();
        _agentProfile.Items.Add("Genel");
        foreach (var profile in profiles) _agentProfile.Items.Add(profile);
        _agentProfile.SelectedItem = selected;
    }

    private static IEnumerable<string> FindAgentProfiles(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) yield break;
        foreach (var folder in new[] { ".company-agent\\agents", ".github\\agents" })
        {
            var directory = Path.Combine(workspacePath, folder);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Take(30))
                yield return folder.Replace('\\', '/') + "/" + Path.GetFileName(file);
        }
    }

    private string LoadSelectedAgentProfile(string workspacePath)
    {
        var selected = _agentProfile.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "Genel", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        var known = FindAgentProfiles(workspacePath).FirstOrDefault(profile => string.Equals(profile, selected, StringComparison.OrdinalIgnoreCase));
        if (known == null) return string.Empty;
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(workspacePath, known.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath) || new FileInfo(fullPath).Length > 64 * 1024) return string.Empty;
            return "--- " + known + " ---\n" + File.ReadAllText(fullPath);
        }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    private static FrameworkElement Labeled(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 11 }); panel.Children.Add(control); return panel;
    }

    private void ApplyTheme()
    {
        var background = new SolidColorBrush(Color.FromRgb(38, 38, 45));
        var foreground = Brushes.WhiteSmoke;
        foreach (var control in new Control[] { _endpoint, _apiKey, _models, _planModel, _actModel, _mode, _maxSteps, _timeoutMinutes, _input })
        {
            control.Background = background;
            control.Foreground = foreground;
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 85, 100));
            control.Margin = new Thickness(0, 2, 0, 0);
        }
        _conversation.Foreground = Brushes.WhiteSmoke;
        _send.Background = new SolidColorBrush(Color.FromRgb(79, 70, 229));
        _send.Foreground = Brushes.White;
        _send.BorderBrush = Brushes.Transparent;
        _retry.Background = new SolidColorBrush(Color.FromRgb(58, 62, 75));
        _retry.Foreground = Brushes.WhiteSmoke;
        _retry.BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 110));
    }

    private static Button StyledButton(string content, double minWidth)
    {
        return new Button { Content = content, MinWidth = minWidth, Background = new SolidColorBrush(Color.FromRgb(58, 62, 75)), Foreground = Brushes.WhiteSmoke, BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 110)), Padding = new Thickness(8, 3, 8, 3) };
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            SaveSettings(); SetStatus("Model listesi yükleniyor…");
            using var request = CreateRequest(HttpMethod.Get, "v1/models"); using var response = await Http.SendAsync(request); response.EnsureSuccessStatusCode();
            var root = Json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("data", out var data) || data is not object[] items) throw new InvalidOperationException("API /v1/models yanıtı beklenen biçimde değil.");
            _models.Items.Clear(); _planModel.Items.Clear(); _actModel.Items.Clear();
            foreach (var item in items) if (item is Dictionary<string, object> model && model.TryGetValue("id", out var id)) { _models.Items.Add(id.ToString()); _planModel.Items.Add(id.ToString()); _actModel.Items.Add(id.ToString()); }
            if (_models.Items.Count > 0 && string.IsNullOrWhiteSpace(_models.Text)) _models.SelectedIndex = 0;
            SetStatus($"{_models.Items.Count} model bulundu.");
        }
        catch (Exception ex) { SetStatus("Model listesi alınamadı: " + ex.Message, true); }
    }

    private async Task SendAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var prompt = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) return;
        _lastPrompt = prompt;
        try
        {
            SaveSettings(); _cancellation?.Cancel(); _cancellation = new CancellationTokenSource(); _cancellation.CancelAfter(TimeSpan.FromMinutes(GetTimeoutMinutes())); _send.IsEnabled = false;
            Write("Siz\n" + prompt + "\n\n", Brushes.White); _input.Clear(); Write("Agent\n", Brushes.LightGreen); SetStatus("Yanıt akışı alınıyor…");
            var workspacePath = VisualStudioContextProvider.GetWorkspacePath();
            RefreshAgentProfiles(workspacePath);
            var promptWithoutImage = VisualStudioContextProvider.ExtractImageMention(prompt, out var imageDataUri);
            var expandedPrompt = VisualStudioContextProvider.ExpandMentions(VisualStudioContextProvider.ExpandPromptOrSkill(promptWithoutImage));
            if (!string.Equals(expandedPrompt, prompt, StringComparison.Ordinal)) SetStatus("Prompt/skill bağlamı yüklendi.");
            var savedHistory = await TryReadHistoryAsync(workspacePath);
            await TrySaveMessageAsync(workspacePath, "user", prompt);
            var planMode = string.Equals(_mode.Text, "Plan", StringComparison.OrdinalIgnoreCase);
            var autopilot = string.Equals(_mode.Text, "Autopilot", StringComparison.OrdinalIgnoreCase);
            var selectedModel = GetSelectedModel(planMode);
            if (string.IsNullOrWhiteSpace(selectedModel)) { SetStatus("Önce model seçin veya model adını girin.", true); return; }
            if (autopilot && MessageBox.Show("Autopilot bu görev boyunca dosya değişiklikleri ve izinli komutlar için tek tek onay sormaz. Workspace sınırı, secret maskeleme ve tehlikeli komut engelleri yürürlükte kalır. Devam edilsin mi?", "Company Code Agent Autopilot", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                SetStatus("Autopilot kullanıcı tarafından iptal edildi.");
                return;
            }
            var modeInstruction = planMode
                ? "PLAN modundasın. Kod tabanını list_files, search_files, search_text, read_file, read_multiple_files ve get_git_diff/status ile keşfedebilirsin. Dosya değiştirme, silme, terminal, build veya test çalıştırma yasaktır. Önce bulguları, sonra numaralı planı ve riskleri açıkla."
                : autopilot
                    ? "AUTOPILOT modundasın. Hedef tamamlanana kadar küçük, doğrulanabilir adımlarla ilerle. Araç çağrılarında JSON tool_call döndür; güvenlik politikası tehlikeli komutları ve workspace dışını yine engeller. Değişiklik sonrası build/test çalıştır ve sonucu bildir."
                    : "INTERACTIVE modundasın. Dosya değişikliği veya komut gerektiğinde yalnızca JSON tool_call döndür; her etkili işlem kullanıcı onayı bekler.";
            var context = VisualStudioContextProvider.Capture();
            var projectRules = VisualStudioContextProvider.LoadProjectRules();
            var agentProfile = LoadSelectedAgentProfile(workspacePath);
            var systemInstruction = "Sen güvenli bir Visual Studio coding agent'sın. " + modeInstruction + " Gizli bilgileri yazma. " + ToolContract + (string.IsNullOrWhiteSpace(projectRules) ? string.Empty : "\nProje kuralları:\n" + projectRules) + (string.IsNullOrWhiteSpace(agentProfile) ? string.Empty : "\nSeçili özel ajan profili:\n" + agentProfile) + (string.IsNullOrWhiteSpace(savedHistory) ? string.Empty : "\nÖnceki oturum mesajları:\n" + savedHistory);
            var messages = new List<Dictionary<string, object>>
            {
                new(StringComparer.Ordinal) { ["role"] = "system", ["content"] = systemInstruction },
                new(StringComparer.Ordinal) { ["role"] = "system", ["content"] = context },
                new(StringComparer.Ordinal) { ["role"] = "user", ["content"] = string.IsNullOrWhiteSpace(imageDataUri) ? (object)expandedPrompt : new object[] { new { type = "text", text = expandedPrompt }, new { type = "image_url", image_url = new { url = imageDataUri } } } }
            };
            var body = new { model = selectedModel, stream = true, messages };
            using var request = CreateRequest(HttpMethod.Post, "v1/chat/completions"); request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token); response.EnsureSuccessStatusCode();
            var fullResponse = new StringBuilder();
            var totalTokens = 0;
            using var stream = await response.Content.ReadAsStreamAsync(); using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !_cancellation.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(); if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                var data = line.Substring(6); if (data == "[DONE]") break; totalTokens = Math.Max(totalTokens, ReadTotalTokens(data)); var token = ReadDelta(data); if (!string.IsNullOrEmpty(token)) { fullResponse.Append(token); Write(token, Brushes.White); }
            }
            var assistantResponse = fullResponse.ToString();
            messages.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "assistant", ["content"] = assistantResponse });
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var toolResult = _cancellation.IsCancellationRequested ? string.Empty : await HandleToolCallAsync(assistantResponse, planMode, autopilot);
            for (var step = 1; step < GetMaxAgentSteps() && !string.IsNullOrWhiteSpace(toolResult) && !_cancellation.IsCancellationRequested; step++)
            {
                Write("\nAgent\n", Brushes.LightGreen);
                messages.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "user", ["content"] = "Araç sonucu:\n" + Limit(toolResult) + "\nGerekirse bir sonraki tek JSON tool_call döndür. İş bittiyse kullanıcıya kısa, doğrulanabilir sonucu bildir." });
                var followUp = new { model = selectedModel, stream = true, messages };
                var streamed = await StreamResponseAsync(followUp);
                assistantResponse = streamed.Content;
                totalTokens += streamed.TotalTokens;
                messages.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "assistant", ["content"] = assistantResponse });
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                toolResult = await HandleToolCallAsync(assistantResponse, planMode, autopilot);
            }
            var hitStepLimit = !_cancellation.IsCancellationRequested && !string.IsNullOrWhiteSpace(toolResult);
            if (hitStepLimit)
                Write("\n[Agent adım sınırına ulaştı. Görev tamamlanmış sayılmadı; devam etmek için Tekrarla'yı kullanın.]\n", Brushes.OrangeRed);
            Write("\n", Brushes.White);
            SetStatus(_cancellation.IsCancellationRequested ? "Durduruldu." : hitStepLimit ? "Adım sınırı nedeniyle durdu." : totalTokens > 0 ? "Tamamlandı · " + totalTokens + " token" : "Tamamlandı.", hitStepLimit);
            if (!_cancellation.IsCancellationRequested) await TrySaveMessageAsync(workspacePath, "assistant", assistantResponse);
            if (!_cancellation.IsCancellationRequested && totalTokens > 0) await TrySaveUsageAsync(workspacePath, selectedModel, totalTokens);
        }
        catch (OperationCanceledException) { SetStatus("Durduruldu."); }
        catch (Exception ex) { Write("\n[Hata] " + ex.Message + "\n\n", Brushes.OrangeRed); SetStatus("İstek başarısız.", true); }
        finally { _send.IsEnabled = true; }
    }

    private void SendClicked(object sender, RoutedEventArgs e) => StartSafely(SendAsync, "İstek başarısız.");

    private void RetryClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastPrompt)) { SetStatus("Tekrarlanacak önceki istek yok.", true); return; }
        _input.Text = _lastPrompt;
        StartSafely(SendAsync, "İstek başarısız.");
    }

    private void LoadModelsClicked(object sender, RoutedEventArgs e) => StartSafely(LoadModelsAsync, "Model listesi alınamadı.");

    private void HistoryClicked(object sender, RoutedEventArgs e) => StartSafely(ShowHistoryAsync, "Geçmiş alınamadı.");

    private void NewChatClicked(object sender, RoutedEventArgs e) => StartSafely(StartNewChatAsync, "Yeni sohbet başlatılamadı.");

    private void ConversationsClicked(object sender, RoutedEventArgs e) => StartSafely(SwitchConversationAsync, "Sohbet değiştirilemedi.");

    private void TasksClicked(object sender, RoutedEventArgs e) => StartSafely(ShowTasksAsync, "Görevler alınamadı.");

    private void CheckpointsClicked(object sender, RoutedEventArgs e) => StartSafely(ShowCheckpointsAsync, "Checkpoint'ler alınamadı.");

    private void RestoreCheckpointClicked(object sender, RoutedEventArgs e) => StartSafely(RestoreCheckpointAsync, "Checkpoint geri yüklenemedi.");

    private void AuditClicked(object sender, RoutedEventArgs e) => StartSafely(ShowAuditAsync, "Audit alınamadı.");

    private void UsageClicked(object sender, RoutedEventArgs e) => StartSafely(ShowUsageAsync, "Kullanım alınamadı.");

    private async Task ShowHistoryAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var history = await TryReadHistoryAsync(VisualStudioContextProvider.GetWorkspacePath());
        Write("\nGeçmiş\n" + (string.IsNullOrWhiteSpace(history) ? "Bu proje için kaydedilmiş mesaj yok." : Limit(history)) + "\n\n", Brushes.LightSteelBlue);
    }

    private async Task ShowTasksAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var result = await GetHostClient(workspace).ExecuteAsync(workspace, 20, new Dictionary<string, string>(), false, true);
        Write("\nGörevler\n" + result.Output + "\n\n", result.Success ? Brushes.LightSteelBlue : Brushes.OrangeRed);
    }

    private async Task ShowCheckpointsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var result = await GetHostClient(workspace).ExecuteAsync(workspace, 14, new Dictionary<string, string>(), false, true);
        Write("\nCheckpoint'ler\n" + result.Output + "\n\n", result.Success ? Brushes.LightSteelBlue : Brushes.OrangeRed);
        if (result.Success) SetStatus("Geri almak için Geri al… düğmesini kullanın.");
    }

    private async Task RestoreCheckpointAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var checkpointId = PromptForCheckpointId();
        if (string.IsNullOrWhiteSpace(checkpointId)) return;
        if (MessageBox.Show("Bu checkpoint'ten sonraki dosya değişiklikleri geri alınacak. Devam edilsin mi?", "Company Code Agent — Checkpoint geri al", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            SetStatus("Checkpoint geri alma iptal edildi.");
            return;
        }
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var result = await GetHostClient(workspace).ExecuteAsync(workspace, 13, new Dictionary<string, string> { ["checkpointId"] = checkpointId }, true, true);
        Write("\nCheckpoint geri al\n" + result.Output + "\n\n", result.Success ? Brushes.LightGreen : Brushes.OrangeRed);
        SetStatus(result.Success ? "Checkpoint geri yüklendi." : "Checkpoint geri yüklenemedi.", !result.Success);
    }

    private static string PromptForCheckpointId()
    {
        var value = new TextBox { MinWidth = 360, Margin = new Thickness(10, 4, 10, 10) };
        var dialog = new Window
        {
            Title = "Checkpoint geri al",
            Width = 430,
            Height = 155,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Checkpoint kimliğini girin:", Margin = new Thickness(10, 10, 10, 0) },
                    value,
                    new Button { Content = "Devam", IsDefault = true, Width = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) }
                }
            }
        };
        var submit = ((StackPanel)dialog.Content).Children[2] as Button;
        submit.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? value.Text.Trim() : string.Empty;
    }

    private async Task ShowAuditAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var result = await GetHostClient(workspace).ExecuteAsync(workspace, 23, new Dictionary<string, string>(), false, true);
        Write("\nAudit\n" + result.Output + "\n\n", result.Success ? Brushes.LightSteelBlue : Brushes.OrangeRed);
    }

    private async Task ShowUsageAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var usage = await GetHostClient(workspace).ReadUsageAsync(workspace);
        var items = Json.DeserializeObject(usage) as object[];
        if (items == null || items.Length == 0)
        {
            Write("\nKullanım\nBu oturum için kaydedilmiş token kullanımı yok.\n\n", Brushes.LightSteelBlue);
            return;
        }
        var lines = new List<string>();
        var total = 0;
        foreach (var raw in items)
        {
            if (raw is not Dictionary<string, object> item) continue;
            var model = item.TryGetValue("model", out var rawModel) ? rawModel?.ToString() ?? "bilinmiyor" : "bilinmiyor";
            var tokens = item.TryGetValue("tokens", out var rawTokens) ? Convert.ToInt32(rawTokens) : 0;
            total += tokens;
            lines.Add(model + " · " + tokens + " token");
        }
        Write("\nKullanım\n" + string.Join("\n", lines) + "\nToplam: " + total + " token\n\n", Brushes.LightSteelBlue);
    }

    private async Task<string> TryReadHistoryAsync(string workspacePath)
    {
        try { return Limit(await GetHostClient(workspacePath).ReadMessagesAsync(workspacePath)); }
        catch { return string.Empty; }
    }

    private async Task StartNewChatAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var key = NormalizeWorkspaceKey(workspace);
        var current = GetConversationId(workspace);
        var history = GetConversationHistory(key);
        if (!history.Contains(current, StringComparer.OrdinalIgnoreCase)) history.Add(current);
        var next = Guid.NewGuid().ToString("N");
        history.Add(next);
        _settings.ConversationIds[key] = next;
        SaveSettings();
        _conversation.Document.Blocks.Clear();
        _lastPrompt = string.Empty;
        Write("Yeni sohbet başlatıldı. Bu konuşmanın geçmişi önceki sohbetten ayrıdır.\n\n", Brushes.LightSteelBlue);
        SetStatus("Yeni sohbet hazır.");
    }

    private async Task SwitchConversationAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var key = NormalizeWorkspaceKey(workspace);
        var current = GetConversationId(workspace);
        var history = GetConversationHistory(key);
        if (!history.Contains(current, StringComparer.OrdinalIgnoreCase)) history.Add(current);
        var selected = PromptForConversationId(history, current);
        if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, current, StringComparison.OrdinalIgnoreCase)) return;
        _settings.ConversationIds[key] = selected;
        SaveSettings();
        _conversation.Document.Blocks.Clear();
        Write("Sohbet dalı değiştirildi. Geçmişi görmek için Geçmiş düğmesini kullanın.\n\n", Brushes.LightSteelBlue);
        SetStatus("Sohbet dalı değiştirildi.");
    }

    private static string PromptForConversationId(IEnumerable<string> conversationIds, string current)
    {
        var list = new ListBox { MinWidth = 430, Height = 160, Margin = new Thickness(10, 4, 10, 8) };
        foreach (var id in conversationIds.Distinct(StringComparer.OrdinalIgnoreCase))
            list.Items.Add((string.Equals(id, current, StringComparison.OrdinalIgnoreCase) ? "Aktif · " : "      ") + id);
        if (list.Items.Count == 0) return string.Empty;
        list.SelectedIndex = 0;
        var dialog = new Window
        {
            Title = "Sohbet dalı seç",
            Width = 500,
            Height = 280,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Açmak istediğiniz sohbet dalını seçin:", Margin = new Thickness(10, 10, 10, 0) },
                    list,
                    new Button { Content = "Aç", IsDefault = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 10, 10) }
                }
            }
        };
        var submit = ((StackPanel)dialog.Content).Children[2] as Button;
        submit.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true || list.SelectedItem == null) return string.Empty;
        var item = list.SelectedItem.ToString();
        return item.Substring(item.IndexOf('·') >= 0 ? item.IndexOf('·') + 2 : 6).Trim();
    }

    private AgentHostClient GetHostClient(string workspacePath) => new AgentHostClient(GetConversationId(workspacePath));

    private string GetConversationId(string workspacePath)
    {
        _settings.ConversationIds ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var key = NormalizeWorkspaceKey(workspacePath);
        if (!_settings.ConversationIds.TryGetValue(key, out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = AgentHostClient.CreateSessionId(workspacePath);
            _settings.ConversationIds[key] = sessionId;
            AgentSettingsStore.Save(_settings);
        }
        var history = GetConversationHistory(key);
        if (!history.Contains(sessionId, StringComparer.OrdinalIgnoreCase))
        {
            history.Add(sessionId);
            AgentSettingsStore.Save(_settings);
        }
        return sessionId;
    }

    private List<string> GetConversationHistory(string workspaceKey)
    {
        _settings.ConversationHistory ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!_settings.ConversationHistory.TryGetValue(workspaceKey, out var history) || history == null)
        {
            history = new List<string>();
            _settings.ConversationHistory[workspaceKey] = history;
        }
        return history;
    }

    private static string NormalizeWorkspaceKey(string workspacePath) => Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();

    private async Task TrySaveMessageAsync(string workspacePath, string role, string content)
    {
        try { await GetHostClient(workspacePath).SaveMessageAsync(workspacePath, role, content); }
        catch { /* Chat remains available if local history storage is temporarily unavailable. */ }
    }

    private async Task TrySaveUsageAsync(string workspacePath, string model, int tokens)
    {
        try { await GetHostClient(workspacePath).SaveUsageAsync(workspacePath, model, tokens); }
        catch { /* Usage history must not interrupt an otherwise successful agent response. */ }
    }

    private string GetSelectedModel(bool planMode)
    {
        if (_separateModels.IsChecked == true) return (planMode ? _planModel.Text : _actModel.Text).Trim();
        return _models.Text.Trim();
    }

    private int GetMaxAgentSteps() => int.TryParse(_maxSteps.Text, out var steps) ? Clamp(steps, 1, 20) : 5;
    private int GetTimeoutMinutes() => int.TryParse(_timeoutMinutes.Text, out var minutes) ? Clamp(minutes, 1, 60) : 10;
    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private void StartSafely(Func<Task> action, string errorStatus)
    {
#pragma warning disable VSSDK007 // WPF event handlers cannot await; FileAndForget routes faults to the VS activity log.
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try { await action(); }
            catch (Exception ex) { Write("\n[Hata] " + ex.Message + "\n", Brushes.OrangeRed); SetStatus(errorStatus, true); }
        }).FileAndForget("CompanyCodeAgent/UiOperation");
#pragma warning restore VSSDK007
    }

    public void SetPrompt(string prompt, string mode = null)
    {
        if (!string.IsNullOrWhiteSpace(mode)) _mode.Text = mode;
        _input.Text = prompt;
        _input.Focus();
        _input.CaretIndex = _input.Text.Length;
    }

    private string ReadDelta(string data)
    {
        try
        {
            var root = Json.DeserializeObject(data) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("choices", out var choices) || choices is not object[] list || list.Length == 0) return string.Empty;
            if (list[0] is not Dictionary<string, object> choice || !choice.TryGetValue("delta", out var delta) || delta is not Dictionary<string, object> content) return string.Empty;
            return content.TryGetValue("content", out var text) ? text?.ToString() ?? string.Empty : string.Empty;
        }
        catch { return string.Empty; }
    }

    private int ReadTotalTokens(string data)
    {
        try
        {
            var root = Json.DeserializeObject(data) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("usage", out var raw) || raw is not Dictionary<string, object> usage) return 0;
            return usage.TryGetValue("total_tokens", out var total) ? Convert.ToInt32(total) : 0;
        }
        catch { return 0; }
    }

    private async Task<string> HandleToolCallAsync(string response, bool planMode, bool autopilot)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var json = ExtractJson(response);
        if (json == null) return string.Empty;
        try
        {
            var root = Json.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("type", out var type) || !string.Equals(type?.ToString(), "tool_call", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (!root.TryGetValue("tool", out var toolName) || !TryGetTool(toolName?.ToString(), out var toolKind, out var requiresApproval)) { Write("\n[Agent geçersiz bir araç istedi.]", Brushes.OrangeRed); return string.Empty; }
            if (planMode && !IsPlanSafeTool(toolKind))
            {
                const string planOnly = "Plan modu yalnızca dosya keşfi ve Git incelemesine izin verir; yazma, silme, komut, build/test ve checkpoint geri alma engellendi.";
                Write("\n[Plan koruması] " + planOnly + "\n", Brushes.OrangeRed);
                return planOnly;
            }
            if (toolKind == 28)
            {
                var diagnostics = VisualStudioContextProvider.GetDiagnostics();
                Write("\n[Tool " + toolName + "] " + diagnostics + "\n", Brushes.LightGreen);
                return diagnostics;
            }
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetValue("arguments", out var raw) && raw is Dictionary<string, object> values)
                foreach (var value in values) arguments[value.Key] = value.Value is Dictionary<string, object> or object[] ? Json.Serialize(value.Value) : value.Value?.ToString() ?? string.Empty;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (toolKind == 5 || toolKind == 6 || toolKind == 7)
            {
                try { VisualStudioDiffPreview.TryOpen(VisualStudioContextProvider.GetWorkspacePath(), toolName?.ToString() ?? string.Empty, arguments); }
                catch (Exception ex) { Write("\n[Diff önizlemesi açılamadı] " + ex.Message + "\n", Brushes.OrangeRed); }
            }
            var approved = autopilot || !requiresApproval || MessageBox.Show(BuildApprovalPrompt(toolName?.ToString() ?? "Araç", arguments), "Company Code Agent Önizleme ve Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var workspacePath = VisualStudioContextProvider.GetWorkspacePath();
            var result = await GetHostClient(workspacePath).ExecuteAsync(workspacePath, toolKind, arguments, requiresApproval, approved);
            Write("\n[Tool " + toolName + "] " + result.Output + "\n", result.Success ? Brushes.LightGreen : Brushes.OrangeRed);
            return result.Output;
        }
        catch (Exception ex) { Write("\n[Tool hatası] " + ex.Message + "\n", Brushes.OrangeRed); return "Araç hatası: " + ex.Message; }
    }

    private async Task<StreamedResponse> StreamResponseAsync(object body)
    {
        using var request = CreateRequest(HttpMethod.Post, "v1/chat/completions");
        request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(); using var reader = new StreamReader(stream);
        var completeResponse = new StringBuilder(); var totalTokens = 0;
        while (!reader.EndOfStream && !_cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = line.Substring(6); if (data == "[DONE]") break; totalTokens = Math.Max(totalTokens, ReadTotalTokens(data));
            var token = ReadDelta(data); if (!string.IsNullOrEmpty(token)) { completeResponse.Append(token); Write(token, Brushes.White); }
        }
        return new StreamedResponse(completeResponse.ToString(), totalTokens);
    }

    private static bool TryGetTool(string name, out int kind, out bool requiresApproval)
    {
        requiresApproval = false;
        switch ((name ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant())
        {
            case "listfiles": kind = 0; return true;
            case "searchfiles": kind = 1; return true;
            case "readfile": kind = 2; return true;
            case "readmultiplefiles": kind = 3; return true;
            case "searchtext": kind = 4; return true;
            case "writefile": kind = 5; requiresApproval = true; return true;
            case "applypatch": kind = 6; requiresApproval = true; return true;
            case "deletefile": kind = 7; requiresApproval = true; return true;
            case "runcommand": kind = 8; requiresApproval = true; return true;
            case "buildsolution": kind = 9; requiresApproval = true; return true;
            case "runtests": kind = 10; requiresApproval = true; return true;
            case "getgitdiff": kind = 11; return true;
            case "getgitstatus": kind = 12; return true;
            case "restorecheckpoint": kind = 13; requiresApproval = true; return true;
            case "listcheckpoints": kind = 14; return true;
            case "comparecheckpoint": kind = 15; return true;
            case "mcplisttools": kind = 16; requiresApproval = true; return true;
            case "mcpcalltool": kind = 17; requiresApproval = true; return true;
            case "createtask": kind = 18; return true;
            case "updatetask": kind = 19; return true;
            case "listtasks": kind = 20; return true;
            case "listgitworktrees": kind = 21; return true;
            case "creategitworktree": kind = 22; requiresApproval = true; return true;
            case "listauditevents": kind = 23; return true;
            case "webfetch": kind = 24; requiresApproval = true; return true;
            case "getgitbranch": kind = 25; return true;
            case "creategitcommit": kind = 26; requiresApproval = true; return true;
            case "getgitstageddiff": kind = 27; return true;
            case "getdiagnostics": kind = 28; return true;
            case "exportaudit": kind = 29; requiresApproval = true; return true;
            case "applymultipatch": kind = 30; requiresApproval = true; return true;
            default: kind = -1; return false;
        }
    }

    private static bool IsPlanSafeTool(int toolKind) => toolKind == 0 || toolKind == 1 || toolKind == 2 || toolKind == 3 || toolKind == 4 || toolKind == 11 || toolKind == 12 || toolKind == 14 || toolKind == 15 || toolKind == 18 || toolKind == 19 || toolKind == 20 || toolKind == 21 || toolKind == 23 || toolKind == 25 || toolKind == 27 || toolKind == 28;

    private const string ToolContract = "Araç gerektiğinde yalnızca şu JSON'u döndür: {\"type\":\"tool_call\",\"id\":\"benzersiz\",\"tool\":\"AraçAdı\",\"arguments\":{...}}. Araçlar: list_files({path?}), search_files({pattern,path?}), read_file({path}), read_multiple_files({paths}), search_text({query,path?}), get_diagnostics({}), write_file({path,content}), apply_patch({path,expected,replacement}), apply_multi_patch({patchesJson}), delete_file({path}), run_command({command}), build_solution({}), run_tests({}), get_git_diff({}), get_git_staged_diff({}), get_git_status({}), get_git_branch({}), create_git_commit({message}), list_checkpoints({}), compare_checkpoint({checkpointId}), restore_checkpoint({checkpointId}), mcp_list_tools({server}), mcp_call_tool({server,toolName,argumentsJson}), create_task({title,status?}), update_task({id,status}), list_tasks({}), list_git_worktrees({}), create_git_worktree({branch}), list_audit_events({}), export_audit({path}), web_fetch({url}). apply_multi_patch için patchesJson, path/expected/replacement alanlı en fazla 20 farklı dosyadan oluşan JSON dizisidir; tüm patch'ler doğrulanır, biri başarısızsa değişiklikler geri alınır. MCP çağrıları yapılandırılmış ve izinli araçlarla sınırlıdır. web_fetch yalnızca kullanıcı onayıyla HTTPS metin içeriği alır; create_git_commit yalnızca zaten stage edilmiş dosyaları commit eder. export_audit yalnızca workspace içindeki .json dosyasına kullanıcı onayıyla yazar. Kod incelemesinde hem get_git_diff hem get_git_staged_diff ve get_diagnostics kullan. Plan oluştururken create_task kullan; uygulamaya başlarken in_progress, bittiğinde completed durumuna geçir. Bir yanıt için yalnızca tek araç çağrısı döndür; araç gerekmiyorsa normal Türkçe yanıt ver.";

    private static string BuildApprovalPrompt(string toolName, IReadOnlyDictionary<string, string> arguments)
    {
        arguments.TryGetValue("path", out var path);
        var preview = toolName.ToLowerInvariant() switch
        {
            "writefile" => "Yeni içerik:\n" + Limit(arguments.TryGetValue("content", out var content) ? content : string.Empty),
            "applypatch" => "Kaldırılacak metin:\n" + Limit(arguments.TryGetValue("expected", out var expected) ? expected : string.Empty) + "\n\nEklenecek metin:\n" + Limit(arguments.TryGetValue("replacement", out var replacement) ? replacement : string.Empty),
            "deletefile" => "Dosya silinecek.",
            "restorecheckpoint" => "Checkpoint geri yüklenecek: " + (arguments.TryGetValue("checkpointId", out var checkpoint) ? checkpoint : "bilinmiyor"),
            _ => "Bu işlem workspace üzerinde değişiklik veya komut yürütme etkisi yaratabilir."
        };
        return $"Agent işlemi: {toolName}\nHedef: {path ?? "workspace"}\n\n{preview}\n\nUygulamak istiyor musunuz?";
    }

    private static string Limit(string value) => value.Length <= 4000 ? value : value.Substring(0, 4000) + "\n[Önizleme kısaltıldı]";

    private static string ExtractJson(string response)
    {
        var text = response.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var start = text.IndexOf('\n'); var end = text.LastIndexOf("```", StringComparison.Ordinal);
            return start >= 0 && end > start ? text.Substring(start + 1, end - start - 1).Trim() : null;
        }
        return text.StartsWith("{") && text.EndsWith("}") ? text : null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var baseUri = new Uri(_endpoint.Text.Trim().TrimEnd('/') + "/", UriKind.Absolute);
        var localEndpoint = baseUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || baseUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || baseUri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        if (!baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !localEndpoint)
            throw new InvalidOperationException("Uzak model endpoint'i HTTPS olmalıdır. HTTP yalnızca localhost için kabul edilir.");
        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        var key = _apiKey.Password.Trim(); if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key); return request;
    }

    private void SaveSettings() { _settings.Endpoint = _endpoint.Text.Trim(); _settings.Model = _models.Text.Trim(); _settings.UseSeparateModeModels = _separateModels.IsChecked == true; _settings.PlanModel = _planModel.Text.Trim(); _settings.ActModel = _actModel.Text.Trim(); _settings.AgentProfile = _agentProfile.SelectedItem as string ?? "Genel"; _settings.MaxAgentSteps = GetMaxAgentSteps(); _settings.TimeoutMinutes = GetTimeoutMinutes(); _settings.SetApiKey(_apiKey.Password); AgentSettingsStore.Save(_settings); }
    private void Write(string value, Brush color) { _conversation.Foreground = color; _conversation.AppendText(value); _conversation.ScrollToEnd(); }
    private void SetStatus(string value, bool error = false) { _status.Text = value; _status.Foreground = error ? Brushes.OrangeRed : Brushes.Gray; }

    private sealed class StreamedResponse(string content, int totalTokens) { public string Content { get; } = content; public int TotalTokens { get; } = totalTokens; }
}
