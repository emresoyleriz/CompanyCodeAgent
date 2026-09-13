using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Shell;

namespace CompanyCodeAgent.VisualStudio;

public sealed class AgentToolWindowControl : UserControl
{
    private static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly Regex ReviewLocationPattern = new Regex(@"(?<![\w.])(?<path>[\w][\w ._\\/-]*\.[A-Za-z0-9]{1,12}):(?<line>[1-9]\d*)", RegexOptions.Compiled);
    private readonly TextBox _endpoint = new() { MinWidth = 180 };
    private readonly PasswordBox _apiKey = new() { MinWidth = 130 };
    private readonly ComboBox _models = new() { MinWidth = 140, IsEditable = true };
    private readonly CheckBox _separateModels = new() { Content = "Plan/Act ayrı model", Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ComboBox _planModel = new() { MinWidth = 120, IsEditable = true };
    private readonly ComboBox _actModel = new() { MinWidth = 120, IsEditable = true };
    private readonly TextBox _maxSteps = new() { MinWidth = 42, Text = "5" };
    private readonly TextBox _timeoutMinutes = new() { MinWidth = 42, Text = "10" };
    private readonly TextBox _maxTokens = new() { MinWidth = 58, Text = "50000" };
    private readonly TextBox _costPerMillion = new() { MinWidth = 58, Text = "0" };
    private readonly ComboBox _mode = new() { MinWidth = 90, IsEditable = true, IsReadOnly = true, ItemsSource = new[] { "Plan", "Interactive", "Autopilot" }, SelectedIndex = 0 };
    private readonly ComboBox _agentProfile = new() { MinWidth = 115, IsEditable = true, IsReadOnly = true };
    private readonly ComboBox _approvalProfile = new() { MinWidth = 122, IsEditable = true, IsReadOnly = true };
    private readonly ComboBox _composerMode = new() { MinWidth = 92, IsEditable = true, IsReadOnly = true, ItemsSource = new[] { "Plan", "Interactive", "Autopilot" } };
    private readonly ComboBox _composerModel = new() { MinWidth = 155, IsEditable = true };
    private readonly RichTextBox _conversation = new() { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBox _input = new() { MinHeight = 92, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button _send = new() { Content = "Gönder", MinWidth = 95 };
    private readonly Button _retry = new() { Content = "Tekrarla", MinWidth = 75 };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Text = "Hazır" };
    private readonly Border _settingsDrawer = new() { Visibility = Visibility.Collapsed };
    private Button _settingsButton;
    private AgentSettings _settings;
    private CancellationTokenSource _cancellation;
    private string _lastPrompt = string.Empty;
    private string _latestPlan = string.Empty;

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
        _maxTokens.Text = Clamp(_settings.MaxTokens, 1000, 500000).ToString(CultureInfo.InvariantCulture);
        _costPerMillion.Text = _settings.CostPerMillionTokensUsd.ToString("0.####", CultureInfo.InvariantCulture);
        _agentProfile.Items.Add("Genel");
        _agentProfile.SelectedIndex = 0;
        RefreshAgentProfiles(VisualStudioContextProvider.TryGetWorkspacePath(out var initialWorkspace) ? initialWorkspace : string.Empty);
        if (_agentProfile.Items.Cast<object>().OfType<string>().Any(profile => string.Equals(profile, _settings.AgentProfile, StringComparison.OrdinalIgnoreCase)))
            _agentProfile.SelectedItem = _settings.AgentProfile;
        _approvalProfile.Items.Add("Her işlemi sor");
        _approvalProfile.Items.Add("Doğrulama otomatik");
        _approvalProfile.SelectedItem = _approvalProfile.Items.Cast<object>().OfType<string>().FirstOrDefault(profile => string.Equals(profile, _settings.ApprovalProfile, StringComparison.OrdinalIgnoreCase)) ?? "Her işlemi sor";
        _composerMode.SelectedItem = _mode.SelectedItem;
        _composerModel.Text = _models.Text;
        _composerMode.SelectionChanged += (_, _) => { if (_composerMode.SelectedItem != null) _mode.SelectedItem = _composerMode.SelectedItem; };
        _composerModel.SelectionChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(_composerModel.Text)) _models.Text = _composerModel.Text; };
        _composerModel.LostFocus += (_, _) => _models.Text = _composerModel.Text;
        ApplyTheme();
        var root = new Grid { Background = AgentUiTheme.Background };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = CreateHeader();
        Grid.SetRow(header, 0); root.Children.Add(header);
        Grid.SetRow(_settingsDrawer, 1); root.Children.Add(_settingsDrawer);
        _conversation.Margin = new Thickness(14, 12, 14, 0);
        _conversation.Padding = new Thickness(6);
        Grid.SetRow(_conversation, 2); root.Children.Add(_conversation);
        var composer = CreateComposer();
        Grid.SetRow(composer, 3); root.Children.Add(composer); Content = root;
        Write("Hazır. Dosya ve seçili kod bağlamı otomatik eklenir. Ctrl+Enter ile gönderin.\n\n", AgentUiTheme.SecondaryText);
    }

    private FrameworkElement CreateComposer()
    {
        var composer = new Border { Margin = new Thickness(14, 10, 14, 12), Padding = new Thickness(10, 7, 10, 7), Background = AgentUiTheme.Elevated, BorderBrush = AgentUiTheme.Border, BorderThickness = new Thickness(1), CornerRadius = AgentUiTheme.RadiusMedium };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var contextBar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var review = CompactButton("İncele", 54); review.Click += (_, _) => { _input.Text = "Kod incelemesi yap"; _input.Focus(); }; DockPanel.SetDock(review, Dock.Right); contextBar.Children.Add(review);
        var context = CompactButton("Bağlam: aktif dosya", 126); context.Click += AddContextClicked; contextBar.Children.Add(context);
        Grid.SetRow(contextBar, 0); layout.Children.Add(contextBar);
        Grid.SetRow(_input, 1); layout.Children.Add(_input);
        var footer = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var cancel = StyledButton("Durdur", 70); cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => _cancellation?.Cancel();
        _send.Click += SendClicked;
        _retry.Click += RetryClicked; _retry.Margin = new Thickness(0, 0, 8, 0);
        _input.PreviewKeyDown += InputKeyDown;
        DockPanel.SetDock(_send, Dock.Right); DockPanel.SetDock(_retry, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
        footer.Children.Add(_send); footer.Children.Add(_retry); footer.Children.Add(cancel);
        var attach = CompactButton("Dosya ekle", 74); attach.Margin = new Thickness(0, 0, 8, 0); attach.Click += AttachFileClicked; DockPanel.SetDock(attach, Dock.Right); footer.Children.Add(attach);
        var selectors = new StackPanel { Orientation = Orientation.Horizontal }; selectors.Children.Add(_composerMode); _composerModel.Margin = new Thickness(8, 0, 0, 0); selectors.Children.Add(_composerModel); footer.Children.Add(selectors);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        composer.Child = layout;
        return composer;
    }

    private FrameworkElement CreateHeader()
    {
        var header = new DockPanel { Height = 46, LastChildFill = true, Background = new SolidColorBrush(Color.FromRgb(29, 29, 29)) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        title.Children.Add(new TextBlock { Text = "Company Code Agent", Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 255)), FontWeight = FontWeights.SemiBold, FontSize = 14 });
        title.Children.Add(new TextBlock { Text = "  Yerel coding agent", Foreground = new SolidColorBrush(Color.FromRgb(145, 145, 145)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(title, Dock.Left); header.Children.Add(title);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        var history = StyledButton("Geçmiş", 64); history.Margin = new Thickness(4); history.Click += HistoryClicked; actions.Children.Add(history);
        var conversations = StyledButton("Sohbetler", 72); conversations.Margin = new Thickness(4); conversations.Click += ConversationsClicked; actions.Children.Add(conversations);
        var newChat = StyledButton("+ Yeni sohbet", 92); newChat.Margin = new Thickness(4); newChat.Click += NewChatClicked; actions.Children.Add(newChat);
        _settingsButton = StyledButton("Ayarlar", 72); _settingsButton.Margin = new Thickness(4); _settingsButton.Click += ToggleSettingsClicked; actions.Children.Add(_settingsButton);
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);

        var drawerContent = new StackPanel();
        drawerContent.Children.Add(new TextBlock { Text = "Bağlantı ve model ayarları", Foreground = Brushes.WhiteSmoke, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        var connection = new WrapPanel();
        connection.Children.Add(Labeled("API adresi", _endpoint)); connection.Children.Add(Labeled("API anahtarı", _apiKey)); connection.Children.Add(Labeled("Model", _models));
        var models = StyledButton("Modelleri yükle", 110); models.Margin = new Thickness(4);
        models.Click += LoadModelsClicked; connection.Children.Add(models);
        var lmStudio = StyledButton("LM Studio", 78); lmStudio.Margin = new Thickness(4);
        lmStudio.Click += LmStudioClicked; connection.Children.Add(lmStudio);
        var testConnection = StyledButton("Bağlantıyı sınama", 112); testConnection.Margin = new Thickness(4);
        testConnection.Click += TestConnectionClicked; connection.Children.Add(testConnection);
        drawerContent.Children.Add(connection);
        drawerContent.Children.Add(new TextBlock { Text = "Agent davranışı", Foreground = Brushes.WhiteSmoke, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5) });
        var agent = new WrapPanel();
        agent.Children.Add(_separateModels); agent.Children.Add(Labeled("Plan modeli", _planModel)); agent.Children.Add(Labeled("Act modeli", _actModel)); agent.Children.Add(Labeled("Mod", _mode)); agent.Children.Add(Labeled("Ajan", _agentProfile)); agent.Children.Add(Labeled("Onay", _approvalProfile)); agent.Children.Add(Labeled("Maks. adım", _maxSteps)); agent.Children.Add(Labeled("Süre (dk)", _timeoutMinutes)); agent.Children.Add(Labeled("Token sınırı", _maxTokens)); agent.Children.Add(Labeled("USD / 1M", _costPerMillion));
        drawerContent.Children.Add(agent);
        var utilities = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var applyPlan = StyledButton("Planı Act'e aktar", 112); applyPlan.Margin = new Thickness(4);
        applyPlan.Click += TransferPlanToActClicked; utilities.Children.Add(applyPlan);
        var exportChat = StyledButton("Dışa aktar", 82); exportChat.Margin = new Thickness(4);
        exportChat.Click += ExportChatClicked; utilities.Children.Add(exportChat);
        var tasks = StyledButton("Görevler", 70); tasks.Margin = new Thickness(4);
        tasks.Click += TasksClicked; utilities.Children.Add(tasks);
        var checkpoints = StyledButton("Checkpoint'ler", 95); checkpoints.Margin = new Thickness(4);
        checkpoints.Click += CheckpointsClicked; utilities.Children.Add(checkpoints);
        var restore = StyledButton("Geri al…", 72); restore.Margin = new Thickness(4);
        restore.Click += RestoreCheckpointClicked; utilities.Children.Add(restore);
        var audit = StyledButton("Audit", 60); audit.Margin = new Thickness(4);
        audit.Click += AuditClicked; utilities.Children.Add(audit);
        var usage = StyledButton("Kullanım", 72); usage.Margin = new Thickness(4);
        usage.Click += UsageClicked; utilities.Children.Add(usage);
        drawerContent.Children.Add(utilities);
        _settingsDrawer.Margin = new Thickness(14, 0, 14, 0);
        _settingsDrawer.Padding = new Thickness(12);
        _settingsDrawer.Background = new SolidColorBrush(Color.FromRgb(35, 35, 35));
        _settingsDrawer.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        _settingsDrawer.BorderThickness = new Thickness(1);
        _settingsDrawer.CornerRadius = new CornerRadius(7);
        _settingsDrawer.Child = drawerContent;
        return header;
    }

    private void ToggleSettingsClicked(object sender, RoutedEventArgs e)
    {
        var open = _settingsDrawer.Visibility != Visibility.Visible;
        _settingsDrawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _settingsButton.Content = open ? "Ayarları kapat" : "Ayarlar";
    }

    private void AddContextClicked(object sender, RoutedEventArgs e)
    {
        _input.Text = string.IsNullOrWhiteSpace(_input.Text) ? "@file:" : _input.Text + " @file:";
        _input.CaretIndex = _input.Text.Length;
        _input.Focus();
        SetStatus("Bir çalışma alanı dosya yolu yazın; aktif dosya ve seçim zaten otomatik bağlama eklenir.");
    }

    private void AttachFileClicked(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!VisualStudioContextProvider.TryGetWorkspacePath(out var workspace))
        {
            SetStatus("Dosya eklemek için önce bir solution veya çalışma alanı dosyası açın.", true);
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Çalışma alanından dosya ekle", InitialDirectory = workspace };
        if (dialog.ShowDialog() != true) return;
        var root = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var selected = Path.GetFullPath(dialog.FileName);
        if (!selected.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Yalnızca açık çalışma alanındaki dosyalar bağlama eklenebilir.", true);
            return;
        }
        var relative = selected.Substring(root.Length);
        _input.Text = (string.IsNullOrWhiteSpace(_input.Text) ? string.Empty : _input.Text + " ") + "@file:\"" + relative + "\"";
        _input.CaretIndex = _input.Text.Length;
        _input.Focus();
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
        var background = AgentUiTheme.Surface;
        var foreground = AgentUiTheme.Text;
        foreach (var control in new Control[] { _endpoint, _apiKey, _models, _planModel, _actModel, _mode, _agentProfile, _approvalProfile, _composerMode, _composerModel, _maxSteps, _timeoutMinutes, _maxTokens, _costPerMillion, _input })
        {
            control.Background = background;
            control.Foreground = foreground;
            control.BorderBrush = AgentUiTheme.Border;
            control.Margin = new Thickness(0, 2, 0, 0);
        }
        _input.Background = Brushes.Transparent;
        _input.BorderThickness = new Thickness(0);
        _input.Foreground = AgentUiTheme.Text;
        _input.MinHeight = 72;
        _input.ToolTip = "Mesajınızı yazın — Ctrl+Enter ile gönderin";
        foreach (var comboBox in new[] { _models, _planModel, _actModel, _mode, _agentProfile, _approvalProfile, _composerMode, _composerModel }) ApplyComboBoxTheme(comboBox);
        _conversation.Foreground = AgentUiTheme.Text;
        _conversation.FontSize = 13;
        _send.Background = new SolidColorBrush(Color.FromRgb(79, 70, 229));
        _send.Foreground = Brushes.White;
        _send.BorderBrush = Brushes.Transparent;
        _retry.Background = new SolidColorBrush(Color.FromRgb(58, 62, 75));
        _retry.Foreground = Brushes.WhiteSmoke;
        _retry.BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 110));
    }

    private static void ApplyComboBoxTheme(ComboBox comboBox)
    {
        var dark = new SolidColorBrush(Color.FromRgb(38, 38, 38));
        var border = new SolidColorBrush(Color.FromRgb(80, 85, 100));
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, dark));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.WhiteSmoke));
        itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, border));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 3, 7, 3)));
        var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(79, 70, 229))));
        highlighted.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        itemStyle.Triggers.Add(highlighted);
        comboBox.ItemContainerStyle = itemStyle;

        var editorStyle = new Style(typeof(TextBox));
        editorStyle.Setters.Add(new Setter(Control.BackgroundProperty, dark));
        editorStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.WhiteSmoke));
        editorStyle.Setters.Add(new Setter(Control.BorderBrushProperty, border));
        editorStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 1, 4, 1)));
        comboBox.Resources[typeof(TextBox)] = editorStyle;
    }

    private static Button StyledButton(string content, double minWidth)
    {
        return new Button { Content = content, MinWidth = minWidth, Background = AgentUiTheme.Surface, Foreground = AgentUiTheme.Text, BorderBrush = AgentUiTheme.Border, Padding = new Thickness(8, 3, 8, 3) };
    }

    private static Button CompactButton(string content, double minWidth)
    {
        return new Button { Content = content, MinWidth = minWidth, Background = Brushes.Transparent, Foreground = AgentUiTheme.SecondaryText, BorderBrush = Brushes.Transparent, Padding = new Thickness(5, 2, 5, 2), FontSize = 11 };
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            SaveSettings(); SetStatus("Model listesi yükleniyor…");
            using var response = await GetModelsWithRetryAsync(); response.EnsureSuccessStatusCode();
            var root = Json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("data", out var data) || data is not object[] items) throw new InvalidOperationException("API /v1/models yanıtı beklenen biçimde değil.");
            _models.Items.Clear(); _planModel.Items.Clear(); _actModel.Items.Clear(); _composerModel.Items.Clear();
            foreach (var item in items) if (item is Dictionary<string, object> model && model.TryGetValue("id", out var id)) { _models.Items.Add(id.ToString()); _planModel.Items.Add(id.ToString()); _actModel.Items.Add(id.ToString()); _composerModel.Items.Add(id.ToString()); }
            if (_models.Items.Count > 0 && string.IsNullOrWhiteSpace(_models.Text)) _models.SelectedIndex = 0;
            _composerModel.Text = _models.Text;
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
        var isReviewRequest = prompt.IndexOf("Kod incelemesi yap", StringComparison.OrdinalIgnoreCase) >= 0;
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
            var tokenBudget = GetMaxTokens();
            var body = new { model = selectedModel, stream = true, max_tokens = tokenBudget, messages };
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
            var usageEvents = new List<int> { totalTokens };
            var budgetTokens = Math.Max(totalTokens, EstimateOutputTokens(assistantResponse));
            messages.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "assistant", ["content"] = assistantResponse });
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var tokenLimitReached = budgetTokens >= tokenBudget;
            var toolResult = _cancellation.IsCancellationRequested || tokenLimitReached ? string.Empty : await HandleToolCallAsync(assistantResponse, planMode, autopilot);
            for (var step = 1; step < GetMaxAgentSteps() && !string.IsNullOrWhiteSpace(toolResult) && !_cancellation.IsCancellationRequested && !tokenLimitReached; step++)
            {
                Write("\nAgent\n", Brushes.LightGreen);
                messages.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "user", ["content"] = "Araç sonucu:\n" + Limit(toolResult) + "\nGerekirse bir sonraki tek JSON tool_call döndür. İş bittiyse kullanıcıya kısa, doğrulanabilir sonucu bildir." });
                var remainingTokens = Math.Max(1, tokenBudget - budgetTokens);
                var followUp = new { model = selectedModel, stream = true, max_tokens = remainingTokens, messages };
                var streamed = await StreamResponseAsync(followUp);
                assistantResponse = streamed.Content;
                totalTokens += streamed.TotalTokens;
                usageEvents.Add(streamed.TotalTokens);
                budgetTokens += Math.Max(streamed.TotalTokens, EstimateOutputTokens(assistantResponse));
                tokenLimitReached = budgetTokens >= tokenBudget;
                messages.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "assistant", ["content"] = assistantResponse });
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                toolResult = tokenLimitReached ? string.Empty : await HandleToolCallAsync(assistantResponse, planMode, autopilot);
            }
            var hitStepLimit = !_cancellation.IsCancellationRequested && !string.IsNullOrWhiteSpace(toolResult);
            if (tokenLimitReached)
                Write("\n[Token bütçesine ulaşıldı. Yeni araç turu başlatılmadı.]\n", Brushes.OrangeRed);
            if (hitStepLimit)
                Write("\n[Agent adım sınırına ulaştı. Görev tamamlanmış sayılmadı; devam etmek için Tekrarla'yı kullanın.]\n", Brushes.OrangeRed);
            if (isReviewRequest && !hitStepLimit && !_cancellation.IsCancellationRequested)
                AppendReviewNavigation(assistantResponse);
            if (planMode && !hitStepLimit && !_cancellation.IsCancellationRequested && !string.IsNullOrWhiteSpace(assistantResponse))
                _latestPlan = assistantResponse;
            Write("\n", Brushes.White);
            var costSuffix = totalTokens > 0 && GetCostPerMillion() > 0 ? " · ~$" + (totalTokens * GetCostPerMillion() / 1_000_000m).ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty;
            var displayTokens = totalTokens > 0 ? totalTokens.ToString(CultureInfo.InvariantCulture) : "~" + budgetTokens.ToString(CultureInfo.InvariantCulture);
            SetStatus(_cancellation.IsCancellationRequested ? "Durduruldu." : tokenLimitReached ? "Token bütçesi nedeniyle durdu." : hitStepLimit ? "Adım sınırı nedeniyle durdu." : budgetTokens > 0 ? "Tamamlandı · " + displayTokens + " token" + costSuffix : "Tamamlandı.", hitStepLimit || tokenLimitReached);
            if (!_cancellation.IsCancellationRequested) await TrySaveMessageAsync(workspacePath, "assistant", assistantResponse);
            if (!_cancellation.IsCancellationRequested)
                foreach (var usageTokens in usageEvents) await TrySaveUsageAsync(workspacePath, selectedModel, usageTokens);
        }
        catch (OperationCanceledException) { SetStatus("Durduruldu."); }
        catch (Exception ex) { Write("\n[Hata] " + ex.Message + "\n\n", Brushes.OrangeRed); SetStatus("İstek başarısız.", true); }
        finally { _send.IsEnabled = true; }
    }

    private void SendClicked(object sender, RoutedEventArgs e) => StartSafely(SendAsync, "İstek başarısız.");

    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            StartSafely(SendAsync, "İstek başarısız.");
        }
        else if (e.Key == Key.Escape && _cancellation != null)
        {
            e.Handled = true;
            _cancellation.Cancel();
        }
    }

    private void RetryClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastPrompt)) { SetStatus("Tekrarlanacak önceki istek yok.", true); return; }
        _input.Text = _lastPrompt;
        StartSafely(SendAsync, "İstek başarısız.");
    }

    private void LoadModelsClicked(object sender, RoutedEventArgs e) => StartSafely(LoadModelsAsync, "Model listesi alınamadı.");

    private void LmStudioClicked(object sender, RoutedEventArgs e)
    {
        _endpoint.Text = "http://127.0.0.1:1234/";
        _apiKey.Clear();
        StartSafely(LoadModelsAsync, "LM Studio'ya bağlanılamadı. Developer ekranında server'ın Running olduğundan emin olun.");
    }

    private void TestConnectionClicked(object sender, RoutedEventArgs e) => StartSafely(TestConnectionAsync, "Bağlantı sınanamadı.");

    private async Task TestConnectionAsync()
    {
        try
        {
            SaveSettings();
            SetStatus("Bağlantı sınanıyor…");
            using var response = await GetModelsWithRetryAsync();
            response.EnsureSuccessStatusCode();
            var root = Json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
            var count = root != null && root.TryGetValue("data", out var data) && data is object[] models ? models.Length : 0;
            Write("\nBağlantı testi\nBaşarılı: " + _endpoint.Text.Trim() + " · " + count + " model bildirildi.\n\n", Brushes.LightGreen);
            SetStatus("Bağlantı başarılı · " + count + " model bulundu.");
        }
        catch (Exception ex)
        {
            Write("\nBağlantı testi\nBaşarısız: " + ex.Message + "\nLM Studio kullanıyorsanız Developer server'ın Running olduğundan ve adresin http://127.0.0.1:1234/ olduğundan emin olun.\n\n", Brushes.OrangeRed);
            SetStatus("Bağlantı başarısız.", true);
        }
    }

    private void HistoryClicked(object sender, RoutedEventArgs e) => StartSafely(ShowHistoryAsync, "Geçmiş alınamadı.");

    private void NewChatClicked(object sender, RoutedEventArgs e) => StartSafely(StartNewChatAsync, "Yeni sohbet başlatılamadı.");

    private void ConversationsClicked(object sender, RoutedEventArgs e) => StartSafely(SwitchConversationAsync, "Sohbet değiştirilemedi.");

    private void TransferPlanToActClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_latestPlan))
        {
            SetStatus("Aktarılacak tamamlanmış bir Plan sonucu yok.", true);
            return;
        }
        _mode.SelectedItem = "Interactive";
        _input.Text = "Aşağıdaki planı uygula. Gerekli dosya değişiklikleri ve komutlarda bana önizleme/onay sun.\n\nPlan:\n" + _latestPlan;
        _input.Focus();
        SetStatus("Plan Act isteğine aktarıldı; gözden geçirip Gönder'e basın.");
    }

    private void ExportChatClicked(object sender, RoutedEventArgs e) => StartSafely(ExportChatAsync, "Sohbet dışa aktarılamadı.");

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
        var grouped = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var raw in items)
        {
            if (raw is not Dictionary<string, object> item) continue;
            var model = item.TryGetValue("model", out var rawModel) ? rawModel?.ToString() ?? "bilinmiyor" : "bilinmiyor";
            var tokens = item.TryGetValue("tokens", out var rawTokens) ? Convert.ToInt32(rawTokens) : 0;
            total += tokens;
            if (!grouped.TryGetValue(model, out var summary)) { summary = new[] { 0, 0 }; grouped[model] = summary; }
            summary[0] += tokens;
            summary[1]++;
        }
        var lines = grouped.OrderBy(item => item.Key).Select(item => item.Key + " · " + item.Value[0] + " token · " + item.Value[1] + " istek").ToList();
        var cost = GetCostPerMillion() > 0 ? "\nYaklaşık maliyet (geçerli USD/1M fiyatıyla): $" + (total * GetCostPerMillion() / 1_000_000m).ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty;
        Write("\nKullanım\n" + string.Join("\n", lines) + "\nToplam: " + total + " token" + cost + "\n\n", Brushes.LightSteelBlue);
    }

    private async Task<string> TryReadHistoryAsync(string workspacePath)
    {
        try { return Limit(await ReadRawHistoryAsync(workspacePath)); }
        catch { return string.Empty; }
    }

    private async Task<string> ReadRawHistoryAsync(string workspacePath) => await GetHostClient(workspacePath).ReadMessagesAsync(workspacePath);

    private async Task ExportChatAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspace = VisualStudioContextProvider.GetWorkspacePath();
        var rawHistory = await ReadRawHistoryAsync(workspace);
        var messages = Json.DeserializeObject(rawHistory) as object[];
        if (messages == null || messages.Length == 0)
        {
            SetStatus("Dışa aktarılacak sohbet mesajı yok.", true);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Sohbeti dışa aktar",
            Filter = "Markdown dosyası (*.md)|*.md|JSON dosyası (*.json)|*.json",
            FileName = "company-code-agent-chat-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".md",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        var jsonOutput = string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase);
        var content = jsonOutput ? rawHistory : FormatChatExport(messages, workspace);
        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Write("\nSohbet dışa aktarıldı: " + dialog.FileName + "\n\n", Brushes.LightSteelBlue);
        SetStatus("Sohbet dışa aktarıldı.");
    }

    private static string FormatChatExport(object[] messages, string workspacePath)
    {
        var result = new StringBuilder();
        result.AppendLine("# Company Code Agent sohbet dışa aktarımı");
        result.AppendLine();
        result.AppendLine("- Çalışma alanı: `" + workspacePath.Replace("`", "'") + "`");
        result.AppendLine("- Aktarım: " + DateTime.Now.ToString("O"));
        result.AppendLine();
        foreach (var raw in messages)
        {
            if (raw is not Dictionary<string, object> message) continue;
            var role = message.TryGetValue("role", out var rawRole) ? rawRole?.ToString() ?? "bilinmiyor" : "bilinmiyor";
            var content = message.TryGetValue("content", out var rawContent) ? rawContent?.ToString() ?? string.Empty : string.Empty;
            result.AppendLine("## " + (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Agent"));
            result.AppendLine();
            result.AppendLine(content);
            result.AppendLine();
        }
        return result.ToString();
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
    private int GetMaxTokens() => int.TryParse(_maxTokens.Text, out var tokens) ? Clamp(tokens, 1000, 500000) : 50000;
    private static int EstimateOutputTokens(string value) => string.IsNullOrEmpty(value) ? 0 : Math.Max(1, (value.Length + 3) / 4);
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
            var approved = autopilot || !requiresApproval || IsApprovalProfileAllowed(toolKind) || MessageBox.Show(BuildApprovalPrompt(toolName?.ToString() ?? "Araç", arguments), "Company Code Agent Önizleme ve Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
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

    private bool IsApprovalProfileAllowed(int toolKind) => string.Equals(_approvalProfile.SelectedItem as string, "Doğrulama otomatik", StringComparison.OrdinalIgnoreCase) && (toolKind == 9 || toolKind == 10);

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

    private async Task<HttpResponseMessage> GetModelsWithRetryAsync()
    {
        Exception lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = CreateRequest(HttpMethod.Get, "v1/models");
                var response = await Http.SendAsync(request);
                if (response.IsSuccessStatusCode || attempt == 1 || ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)) return response;
                response.Dispose();
                lastError = new HttpRequestException("Model listesi geçici HTTP hatası döndürdü: " + (int)response.StatusCode);
            }
            catch (HttpRequestException ex) when (attempt == 0) { lastError = ex; }
            catch (TaskCanceledException ex) when (attempt == 0) { lastError = ex; }
            if (attempt == 0) await Task.Delay(500);
        }
        throw new HttpRequestException("Model listesi iki denemede alınamadı.", lastError);
    }

    private decimal GetCostPerMillion()
    {
        var value = _costPerMillion.Text.Trim().Replace(',', '.');
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost) && cost >= 0 ? cost : 0;
    }

    private void SaveSettings() { _settings.Endpoint = _endpoint.Text.Trim(); _settings.Model = _models.Text.Trim(); _settings.UseSeparateModeModels = _separateModels.IsChecked == true; _settings.PlanModel = _planModel.Text.Trim(); _settings.ActModel = _actModel.Text.Trim(); _settings.AgentProfile = _agentProfile.SelectedItem as string ?? "Genel"; _settings.ApprovalProfile = _approvalProfile.SelectedItem as string ?? "Her işlemi sor"; _settings.MaxAgentSteps = GetMaxAgentSteps(); _settings.TimeoutMinutes = GetTimeoutMinutes(); _settings.MaxTokens = GetMaxTokens(); _settings.CostPerMillionTokensUsd = GetCostPerMillion(); _settings.SetApiKey(_apiKey.Password); AgentSettingsStore.Save(_settings); }

    private void AppendReviewNavigation(string response)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var workspacePath = VisualStudioContextProvider.GetWorkspacePath();
        if (string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(response)) return;
        var locations = ReviewLocationPattern.Matches(response)
            .Cast<Match>()
            .Select(match => new { Path = match.Groups["path"].Value.Trim(), Line = int.Parse(match.Groups["line"].Value) })
            .Where(location => !Path.IsPathRooted(location.Path))
            .GroupBy(location => location.Path + ":" + location.Line, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .ToList();
        if (locations.Count == 0) return;

        var paragraph = new Paragraph { Margin = new Thickness(0, 4, 0, 2), Foreground = Brushes.LightSteelBlue };
        paragraph.Inlines.Add("Bulgulara git: ");
        foreach (var location in locations)
        {
            var targetPath = location.Path;
            var targetLine = location.Line;
            var link = new Hyperlink(new Run(targetPath + ":" + targetLine)) { Foreground = Brushes.LightSkyBlue, ToolTip = "Dosyayı bu satırda aç" };
            link.Click += (_, _) => OpenReviewFinding(targetPath, targetLine);
            paragraph.Inlines.Add(link);
            paragraph.Inlines.Add("  ");
        }
        _conversation.Document.Blocks.Add(paragraph);
        _conversation.ScrollToEnd();
    }

    private void OpenReviewFinding(string relativePath, int line)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var workspacePath = VisualStudioContextProvider.GetWorkspacePath();
        if (string.IsNullOrWhiteSpace(workspacePath) || Path.IsPathRooted(relativePath)) return;
        var root = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            SetStatus("İnceleme konumu çözüm klasöründe bulunamadı: " + relativePath, true);
            return;
        }
        var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        dte?.ItemOperations.OpenFile(fullPath);
        var selection = dte?.ActiveDocument?.Selection as EnvDTE.TextSelection;
        selection?.GotoLine(line, true);
    }

    private void Write(string value, Brush color)
    {
        var range = new TextRange(_conversation.Document.ContentEnd, _conversation.Document.ContentEnd) { Text = value };
        range.ApplyPropertyValue(TextElement.ForegroundProperty, color);
        _conversation.ScrollToEnd();
    }
    private void SetStatus(string value, bool error = false) { _status.Text = value; _status.Foreground = error ? Brushes.OrangeRed : Brushes.Gray; }

    private sealed class StreamedResponse(string content, int totalTokens) { public string Content { get; } = content; public int TotalTokens { get; } = totalTokens; }
}
