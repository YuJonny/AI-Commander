using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Commander;

public partial class FunctionPage : UserControl
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly MediaKeyInterceptor _interceptor;
    private readonly VoiceController _voiceController;
    private readonly VoiceHud _voiceHud;
    private readonly ModelDownloadService _downloader = ModelDownloadService.Shared;
    private readonly Dictionary<ModelDownloadTarget, ModelDownloadProgress> _downloadProgress = [];
    private readonly HashSet<int> _heldModifiers = [];
    private bool _suppressEvents;
    private bool _isRecordingShortcut;
    private int? _firstModifier;
    private nint _recordHookHandle;
    private LowLevelKeyboardProc? _recordHookCallback;
    private long _statusGeneration;

    public event EventHandler<bool>? IsEnabledChanged2;

    public FunctionPage(
        MediaKeyInterceptor interceptor,
        VoiceController voiceController,
        VoiceHud voiceHud)
    {
        InitializeComponent();
        _interceptor = interceptor;
        _voiceController = voiceController;
        _voiceHud = voiceHud;
        _interceptor.MediaKeyPressed += _voiceController.Toggle;
        _voiceController.StateChanged += VoiceController_StateChanged;
        _downloader.ProgressChanged += Downloader_ProgressChanged;
        _downloader.ModelsChanged += Downloader_ModelsChanged;
        Unloaded += (_, _) => StopShortcutRecording(cancelled: true);

        RefreshLocalization();
        LoadSettings();
        RefreshModels();
    }

    public void RefreshLocalization()
    {
        _suppressEvents = true;
        var selected = Settings.Instance.SelectedInputMethod;
        CmbInputMethod.ItemsSource = InputOptions();
        CmbInputMethod.SelectedItem = CmbInputMethod.Items.Cast<InputOption>()
            .FirstOrDefault(item => item.Kind == selected);
        CmbTriggerMode.ItemsSource = new[] { T("按住触发", "Hold to talk"), T("单击切换", "Press to toggle") };
        CmbTriggerMode.SelectedIndex = Settings.Instance.TargetTriggerMode == ShortcutTriggerMode.Hold ? 0 : 1;
        _suppressEvents = false;

        LblTitle.Text = I18n.Get("title.app");
        LblSubtitle.Text = I18n.Get("title.subtitle");
        LblInputMethodTitle.Text = T("输入方式", "Input method");
        LblAsrTitle.Text = T("语音识别模型", "Speech recognition model");
        LblQwenDescription.Text = T("约 840MB · 适合 16GB 以上机型", "About 840MB · Best with 16GB+ RAM");
        LblSenseDescription.Text = T("约 220MB · 适合 8GB 机型", "About 220MB · Designed for 8GB RAM");
        LblPolishTitle.Text = T("口述整理", "Dictation cleanup");
        LblCleanFillersTitle.Text = T("口语词优化", "Filler cleanup");
        LblCleanFillersDescription.Text = T("自动过滤嗯、呃、啊等停顿词", "Remove clear pause fillers automatically");
        LblLlmTitle.Text = T("本地大模型润色", "Local LLM polish");
        LblLlmDescription.Text = T("随口说，出成稿", "Speak naturally, get a clean draft");
        LblLlmModelTitle.Text = T("语义增强大模型 · 约 4.7GB", "Semantic enhancement model · about 4.7GB");
        LblCustomTitle.Text = T("自定义快捷键", "Custom shortcut");
        LblSource.Text = T("播放 / 暂停", "Play / Pause");
        LblTriggerMode.Text = T("触发方式", "Trigger mode");
        LblPresetHint.Text = T("使用该工具的官方 Windows 默认快捷键", "Uses the official Windows default shortcut");
        LblLaunchTitle.Text = T("开机自启动", "Launch at login");
        LblLaunchDescription.Text = T("登录 Windows 后自动运行", "Start automatically after signing in");
        LblReset.Text = T("恢复默认 (Right Alt)", "Reset default (Right Alt)");
        LblChangeKey.Text = _isRecordingShortcut ? T("取消", "Cancel") : T("更改", "Change");
        BtnQwenDelete.ToolTip = BtnSenseDelete.ToolTip = BtnLlmDelete.ToolTip = T("删除模型", "Delete model");
        BtnQwenCancel.ToolTip = BtnSenseCancel.ToolTip = BtnLlmCancel.ToolTip = T("取消下载", "Cancel download");
        ApplyInputMethod();
        RefreshModels();
    }

    private void LoadSettings()
    {
        _suppressEvents = true;
        var settings = Settings.Instance;
        SwEnabled.IsChecked = settings.IsEnabled;
        SwLaunch.IsChecked = settings.LaunchAtLogin;
        SwCleanFillers.IsChecked = settings.CleanFillers;
        SwLlm.IsChecked = settings.UseLlm;
        RbQwen.IsChecked = settings.SelectedAsrModel == AsrModelKind.Qwen;
        RbSense.IsChecked = settings.SelectedAsrModel == AsrModelKind.SenseVoice;
        LblTarget.Text = settings.CustomShortcut.DisplayName;
        _suppressEvents = false;

        ApplyInputMethod();
        if (settings.IsEnabled)
        {
            try
            {
                _interceptor.Start();
                IsEnabledChanged2?.Invoke(this, true);
            }
            catch (Exception exception)
            {
                AppLog.Error(nameof(FunctionPage), exception, "Cannot start media-key listener");
                _suppressEvents = true;
                SwEnabled.IsChecked = false;
                _suppressEvents = false;
                settings.IsEnabled = false;
                settings.Save();
            }
        }
        UpdateHeaderStatus();
    }

    private IReadOnlyList<InputOption> InputOptions() =>
    [
        new(InputMethodKind.LocalVoice, T("本地大模型", "Local models"),
            T("全程本地识别并直接输入", "On-device recognition and direct text input")),
        new(InputMethodKind.Custom, T("自定义", "Custom"),
            T("映射到任意 Windows 快捷键", "Map to any Windows shortcut")),
        new(InputMethodKind.WindowsVoiceTyping, T("Windows 语音输入", "Windows Voice Typing"), "Windows + H"),
        new(InputMethodKind.Typeless, "Typeless", T("官方 Windows 默认快捷键", "Official Windows default")),
        new(InputMethodKind.ClaudeCode, "Claude Code", T("语音输入快捷键", "Voice input shortcut")),
        new(InputMethodKind.Codex, "Codex", T("直接启动听写", "Start dictation directly")),
        new(InputMethodKind.WisprFlow, "Wispr Flow", T("Windows 免提听写", "Windows hands-free dictation")),
        new(InputMethodKind.WeChatVoice, T("微信语音输入", "WeChat Voice Input"),
            T("按住 Ctrl + Win 说话", "Hold Ctrl + Win to speak")),
    ];

    private void CmbInputMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || CmbInputMethod.SelectedItem is not InputOption option) return;
        Settings.Instance.SelectedInputMethod = option.Kind;
        Settings.Instance.Save();
        ApplyInputMethod();
        UpdateHeaderStatus();
    }

    private void ApplyInputMethod()
    {
        if (CmbInputMethod.SelectedItem is InputOption option)
            LblInputMethodDescription.Text = option.Description;

        var kind = Settings.Instance.SelectedInputMethod;
        LocalVoicePanel.Visibility = kind == InputMethodKind.LocalVoice ? Visibility.Visible : Visibility.Collapsed;
        CustomPanel.Visibility = kind == InputMethodKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        PresetPanel.Visibility = kind is not (InputMethodKind.LocalVoice or InputMethodKind.Custom)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _voiceController.SelectedModel = Settings.Instance.SelectedAsrModel;
        _voiceController.CleanFillers = Settings.Instance.CleanFillers;
        _voiceController.UseLlm = Settings.Instance.UseLlm;
        if (kind == InputMethodKind.LocalVoice)
        {
            _interceptor.Mode = MediaKeyActionMode.LocalVoice;
        }
        else
        {
            _interceptor.Mode = MediaKeyActionMode.Shortcut;
            _interceptor.Shortcut = kind == InputMethodKind.Custom
                ? Settings.Instance.CustomShortcut
                : InputMethodCatalog.Get(kind).Shortcut!;
            LblPresetMapping.Text = $"{T("播放 / 暂停", "Play / Pause")}  →  {_interceptor.Shortcut.DisplayName}";
        }
    }

    public void ToggleEnabled() => SwEnabled.IsChecked = !(SwEnabled.IsChecked ?? false);

    private void SwEnabled_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        try
        {
            ApplyInputMethod();
            _interceptor.Start();
            Settings.Instance.IsEnabled = true;
            Settings.Instance.Save();
            IsEnabledChanged2?.Invoke(this, true);
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(FunctionPage), exception, "Cannot enable media-key listener");
            _suppressEvents = true;
            SwEnabled.IsChecked = false;
            _suppressEvents = false;
            System.Windows.MessageBox.Show(
                T("无法监听耳机播放键，请重启应用后重试。", "Unable to listen for the headset play key. Restart the app and try again."),
                I18n.Get("title.app"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        UpdateHeaderStatus();
    }

    private void SwEnabled_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _interceptor.Stop();
        Settings.Instance.IsEnabled = false;
        Settings.Instance.Save();
        IsEnabledChanged2?.Invoke(this, false);
        UpdateHeaderStatus();
    }

    private void RbQwen_Checked(object sender, RoutedEventArgs e) => SelectAsrModel(AsrModelKind.Qwen);
    private void RbSense_Checked(object sender, RoutedEventArgs e) => SelectAsrModel(AsrModelKind.SenseVoice);

    private void SelectAsrModel(AsrModelKind model)
    {
        if (_suppressEvents) return;
        Settings.Instance.SelectedAsrModel = model;
        Settings.Instance.Save();
        _voiceController.SelectedModel = model;
        RefreshModels();
        UpdateHeaderStatus();
    }

    private void QwenCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RbQwen.IsChecked = true;
    }

    private void SenseCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RbSense.IsChecked = true;
    }

    private async void BtnQwenDownload_Click(object sender, RoutedEventArgs e)
    {
        RbQwen.IsChecked = true;
        await _downloader.DownloadAsrAsync(AsrModelKind.Qwen);
    }

    private async void BtnSenseDownload_Click(object sender, RoutedEventArgs e)
    {
        RbSense.IsChecked = true;
        await _downloader.DownloadAsrAsync(AsrModelKind.SenseVoice);
    }

    private async void BtnLlmDownload_Click(object sender, RoutedEventArgs e) => await _downloader.DownloadLlmAsync();

    private void BtnQwenDelete_Click(object sender, RoutedEventArgs e) => DeleteAsr(AsrModelKind.Qwen);
    private void BtnSenseDelete_Click(object sender, RoutedEventArgs e) => DeleteAsr(AsrModelKind.SenseVoice);

    private void DeleteAsr(AsrModelKind model)
    {
        var name = model == AsrModelKind.Qwen ? "Qwen3 · 高精度" : "SenseVoice · 轻量";
        if (!ConfirmDelete(name, T("删除后该档语音识别将无法使用。", "This recognition model will no longer be available."))) return;
        _downloader.DeleteAsr(model);
        if (Settings.Instance.SelectedAsrModel == model)
        {
            var other = model == AsrModelKind.Qwen ? AsrModelKind.SenseVoice : AsrModelKind.Qwen;
            if (ModelCatalog.IsAsrReady(other))
            {
                _suppressEvents = true;
                Settings.Instance.SelectedAsrModel = other;
                RbQwen.IsChecked = other == AsrModelKind.Qwen;
                RbSense.IsChecked = other == AsrModelKind.SenseVoice;
                _suppressEvents = false;
                Settings.Instance.Save();
                _voiceController.SelectedModel = other;
            }
        }
        RefreshModels();
    }

    private void BtnLlmDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDelete(
                T("语义增强大模型", "Semantic enhancement model"),
                T("删除后本地大模型润色将无法使用。", "Local LLM polishing will no longer be available."))) return;
        Settings.Instance.UseLlm = false;
        Settings.Instance.Save();
        _voiceController.UseLlm = false;
        LlamaRunner.Shared.Stop();
        _downloader.DeleteLlm();
        RefreshModels();
    }

    private bool ConfirmDelete(string name, string description) => System.Windows.MessageBox.Show(
        $"{T("删除", "Delete")} {name}?\n\n{description}",
        I18n.Get("title.app"),
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void RefreshModels()
    {
        var qwenReady = ModelCatalog.IsAsrReady(AsrModelKind.Qwen);
        var senseReady = ModelCatalog.IsAsrReady(AsrModelKind.SenseVoice);
        var llmReady = ModelCatalog.IsLlmReady;
        SetModelRow(
            ModelDownloadTarget.QwenAsr,
            qwenReady,
            LblQwenState,
            BtnQwenDownload,
            BtnQwenDelete,
            BtnQwenCancel,
            QwenProgressPanel,
            QwenProgress,
            LblQwenProgress,
            LblQwenDownload);
        SetModelRow(
            ModelDownloadTarget.SenseVoiceAsr,
            senseReady,
            LblSenseState,
            BtnSenseDownload,
            BtnSenseDelete,
            BtnSenseCancel,
            SenseProgressPanel,
            SenseProgress,
            LblSenseProgress,
            LblSenseDownload);
        SetModelRow(
            ModelDownloadTarget.Llm,
            llmReady,
            LblLlmModelState,
            BtnLlmDownload,
            BtnLlmDelete,
            BtnLlmCancel,
            LlmProgressPanel,
            LlmProgress,
            LblLlmProgress,
            LblLlmDownload);

        if (!llmReady && Settings.Instance.UseLlm)
        {
            _suppressEvents = true;
            Settings.Instance.UseLlm = false;
            Settings.Instance.Save();
            SwLlm.IsChecked = false;
            _suppressEvents = false;
            _voiceController.UseLlm = false;
        }
        SwLlm.IsEnabled = llmReady && !_downloader.IsDownloading(ModelDownloadTarget.Llm);
        UpdateModelCardVisuals(qwenReady, senseReady);
        UpdateHeaderStatus();
    }

    private void SetModelRow(
        ModelDownloadTarget target,
        bool ready,
        TextBlock state,
        Button download,
        Button delete,
        Button cancel,
        FrameworkElement progressPanel,
        ProgressBar progressBar,
        TextBlock progressLabel,
        TextBlock downloadLabel)
    {
        if (_downloader.IsDownloading(target))
        {
            var progress = _downloadProgress.GetValueOrDefault(target) ?? new ModelDownloadProgress(
                target,
                null,
                T("正在准备下载", "Preparing download"),
                0,
                IsIndeterminate: true);
            SetModelProgress(
                progress,
                state,
                download,
                delete,
                cancel,
                progressPanel,
                progressBar,
                progressLabel);
            return;
        }

        state.Text = ready ? T("已下载", "Downloaded") : T("未下载", "Not downloaded");
        state.Foreground = new SolidColorBrush(ready ? Color.FromRgb(0x16, 0xA2, 0x48) : Color.FromRgb(0x7A, 0x7A, 0x7A));
        download.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        delete.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = Visibility.Collapsed;
        progressPanel.Visibility = Visibility.Collapsed;
        progressBar.IsIndeterminate = false;
        progressBar.Value = 0;
        progressLabel.Text = "0%";
        downloadLabel.Text = T("下载", "Download");
    }

    private void UpdateModelCardVisuals(bool qwenReady, bool senseReady)
    {
        StyleModelCard(QwenCard, Settings.Instance.SelectedAsrModel == AsrModelKind.Qwen, qwenReady);
        StyleModelCard(SenseCard, Settings.Instance.SelectedAsrModel == AsrModelKind.SenseVoice, senseReady);
    }

    private static void StyleModelCard(Border card, bool selected, bool ready)
    {
        card.SetResourceReference(
            Border.BackgroundProperty,
            selected ? "SubtleFillColorSecondaryBrush" : "CardBackgroundFillColorDefaultBrush");
        if (selected && !ready)
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0x6E, 0x00));
        else
            card.SetResourceReference(
                Border.BorderBrushProperty,
                selected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
        card.BorderThickness = new Thickness(selected ? 1.5 : 1);
    }

    private void Downloader_ProgressChanged(object? sender, ModelDownloadProgress progress)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _downloadProgress[progress.Target] = progress;
            ApplyDownloadProgress(progress);
        });
    }

    private void Downloader_ModelsChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshModels();
            foreach (var target in Enum.GetValues<ModelDownloadTarget>())
            {
                if (_downloader.IsDownloading(target) ||
                    !_downloadProgress.TryGetValue(target, out var progress)) continue;
                ApplyDownloadProgress(progress);
                _ = ClearDownloadProgressLater(target, progress);
            }
        });
    }

    private void ApplyDownloadProgress(ModelDownloadProgress progress)
    {
        switch (progress.Target)
        {
            case ModelDownloadTarget.QwenAsr:
                SetModelProgress(
                    progress,
                    LblQwenState,
                    BtnQwenDownload,
                    BtnQwenDelete,
                    BtnQwenCancel,
                    QwenProgressPanel,
                    QwenProgress,
                    LblQwenProgress);
                break;
            case ModelDownloadTarget.SenseVoiceAsr:
                SetModelProgress(
                    progress,
                    LblSenseState,
                    BtnSenseDownload,
                    BtnSenseDelete,
                    BtnSenseCancel,
                    SenseProgressPanel,
                    SenseProgress,
                    LblSenseProgress);
                break;
            case ModelDownloadTarget.Llm:
                SetModelProgress(
                    progress,
                    LblLlmModelState,
                    BtnLlmDownload,
                    BtnLlmDelete,
                    BtnLlmCancel,
                    LlmProgressPanel,
                    LlmProgress,
                    LblLlmProgress);
                break;
        }
    }

    private void SetModelProgress(
        ModelDownloadProgress progress,
        TextBlock state,
        Button download,
        Button delete,
        Button cancel,
        FrameworkElement progressPanel,
        ProgressBar progressBar,
        TextBlock progressLabel)
    {
        var active = _downloader.IsDownloading(progress.Target);
        var color = active
            ? Color.FromRgb(0x00, 0x78, 0xD4)
            : progress.Fraction >= 1
                ? Color.FromRgb(0x16, 0xA2, 0x48)
                : Color.FromRgb(0xC7, 0x6E, 0x00);
        state.Text = progress.Status;
        state.Foreground = new SolidColorBrush(color);
        download.Visibility = Visibility.Collapsed;
        delete.Visibility = Visibility.Collapsed;
        cancel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        progressPanel.Visibility = Visibility.Visible;
        progressBar.IsIndeterminate = active && progress.IsIndeterminate;
        progressBar.Value = progress.Fraction * 100;
        progressLabel.Text = progressBar.IsIndeterminate
            ? "..."
            : $"{Math.Round(progress.Fraction * 100):0}%";
    }

    private async Task ClearDownloadProgressLater(
        ModelDownloadTarget target,
        ModelDownloadProgress expected)
    {
        await Task.Delay(1800);
        if (_downloader.IsDownloading(target) ||
            !_downloadProgress.TryGetValue(target, out var current) ||
            current != expected) return;
        _downloadProgress.Remove(target);
        RefreshModels();
    }

    private void BtnQwenCancel_Click(object sender, RoutedEventArgs e) =>
        _downloader.Cancel(ModelDownloadTarget.QwenAsr);

    private void BtnSenseCancel_Click(object sender, RoutedEventArgs e) =>
        _downloader.Cancel(ModelDownloadTarget.SenseVoiceAsr);

    private void BtnLlmCancel_Click(object sender, RoutedEventArgs e) =>
        _downloader.Cancel(ModelDownloadTarget.Llm);

    private void SwCleanFillers_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        Settings.Instance.CleanFillers = SwCleanFillers.IsChecked == true;
        Settings.Instance.Save();
        _voiceController.CleanFillers = Settings.Instance.CleanFillers;
    }

    private async void SwLlm_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SwLlm.IsChecked == true && !ModelCatalog.IsLlmReady)
        {
            _suppressEvents = true;
            SwLlm.IsChecked = false;
            _suppressEvents = false;
            return;
        }
        Settings.Instance.UseLlm = SwLlm.IsChecked == true;
        Settings.Instance.Save();
        _voiceController.UseLlm = Settings.Instance.UseLlm;
        if (!Settings.Instance.UseLlm)
        {
            LlamaRunner.Shared.Stop();
            return;
        }
        // 首次开启：实测本机能不能跑得动（CPU 推理各机器差一个数量级）
        if (LlmPerformanceProbe.CurrentTier == LlmPerformanceProbe.Tier.Unknown)
            await RunLlmPerformanceProbeAsync();
    }

    /// 一次性性能探测：慢机器上直接告知实情，不让用户下载了 4.7GB 却每次都等到超时
    private async Task RunLlmPerformanceProbeAsync()
    {
        SwLlm.IsEnabled = false;
        var previousState = LblLlmModelState.Text;
        LblLlmModelState.Text = T("正在评估本机运行速度…", "Measuring this PC's speed…");
        try
        {
            var result = await LlmPerformanceProbe.RunAsync();
            switch (result.Tier)
            {
                case LlmPerformanceProbe.Tier.TooSlow:
                    _suppressEvents = true;
                    SwLlm.IsChecked = false;
                    _suppressEvents = false;
                    Settings.Instance.UseLlm = false;
                    Settings.Instance.Save();
                    _voiceController.UseLlm = false;
                    LlamaRunner.Shared.Stop();
                    ShowLlmProbeMessage(
                        T($"本机运行润色大模型较慢，预计每句约需 {result.EstimatedSeconds:F0} 秒，已暂不开启。\n\n" +
                          "语音识别和口语词清理不受影响，可正常使用。\n" +
                          "（Windows 版目前使用 CPU 推理，速度取决于处理器性能。）",
                          $"Polishing on this PC is slow — about {result.EstimatedSeconds:F0}s per sentence, so it stays off.\n\n" +
                          "Speech recognition and filler cleanup are unaffected.\n" +
                          "(The Windows build runs the model on CPU; speed depends on your processor.)"),
                        T("暂不建议开启润色", "Polishing not recommended"));
                    break;

                case LlmPerformanceProbe.Tier.Slow:
                    ShowLlmProbeMessage(
                        T($"已开启。本机预计每句润色约需 {result.EstimatedSeconds:F0} 秒，出字会慢一些。\n" +
                          "若觉得太慢，随时关掉此开关即可。",
                          $"Enabled. Polishing takes about {result.EstimatedSeconds:F0}s per sentence on this PC.\n" +
                          "Turn the switch off anytime if that feels too slow."),
                        T("已开启（本机较慢）", "Enabled (slower on this PC)"));
                    break;

                case LlmPerformanceProbe.Tier.Failed:
                    // 探测没成功不拦人：可能只是这次服务启动慢，留给真实使用时的超时保护兜底
                    AppLog.Info(nameof(FunctionPage), "LLM performance probe failed; leaving polish enabled.");
                    break;
            }
        }
        finally
        {
            LblLlmModelState.Text = previousState;
            SwLlm.IsEnabled = ModelCatalog.IsLlmReady && !_downloader.IsDownloading(ModelDownloadTarget.Llm);
            RefreshModels();
        }
    }

    private static void ShowLlmProbeMessage(string message, string title) =>
        System.Windows.MessageBox.Show(
            message,
            $"{I18n.Get("title.app")} - {title}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void SwLaunch_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        StartupHelper.Enable();
        Settings.Instance.LaunchAtLogin = true;
        Settings.Instance.Save();
    }

    private void SwLaunch_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        StartupHelper.Disable();
        Settings.Instance.LaunchAtLogin = false;
        Settings.Instance.Save();
    }

    private void VoiceController_StateChanged(object? sender, VoiceState state)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _voiceHud.ShowState(state);
            switch (state.Kind)
            {
                case VoiceStateKind.Listening:
                case VoiceStateKind.Transcribing:
                case VoiceStateKind.Enhancing:
                    SetStatus(state.Text, Color.FromRgb(0x00, 0x78, 0xD4));
                    break;
                case VoiceStateKind.Inserted:
                    SetStatus(T("已完成", "Done"), Color.FromRgb(0x16, 0xA2, 0x48));
                    RestoreHeaderStatusLater();
                    break;
                case VoiceStateKind.NoAudio:
                case VoiceStateKind.NoTranscript:
                case VoiceStateKind.ModelMissing:
                case VoiceStateKind.Error:
                    SetStatus(state.Text, Color.FromRgb(0xC7, 0x6E, 0x00));
                    RestoreHeaderStatusLater();
                    break;
            }
        });
    }

    private async void RestoreHeaderStatusLater()
    {
        var generation = ++_statusGeneration;
        await Task.Delay(2500);
        if (generation == _statusGeneration) UpdateHeaderStatus();
    }

    private void UpdateHeaderStatus()
    {
        _statusGeneration++;
        if (SwEnabled.IsChecked != true)
        {
            SetStatus(T("已停止", "Stopped"), Color.FromRgb(0x7A, 0x7A, 0x7A));
            return;
        }
        if (Settings.Instance.SelectedInputMethod == InputMethodKind.LocalVoice &&
            !ModelCatalog.IsAsrReady(Settings.Instance.SelectedAsrModel))
        {
            var name = Settings.Instance.SelectedAsrModel == AsrModelKind.Qwen ? "Qwen3" : "SenseVoice";
            SetStatus(T($"需要下载 {name}", $"Download {name}"), Color.FromRgb(0xC7, 0x6E, 0x00));
            return;
        }
        SetStatus(T("已就绪", "Ready"), Color.FromRgb(0x16, 0xA2, 0x48));
    }

    private void SetStatus(string text, Color color)
    {
        LblAppStatus.Text = text;
        StatusDot.Fill = new SolidColorBrush(color);
    }

    private void BtnChangeKey_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecordingShortcut) StopShortcutRecording(cancelled: true);
        else StartShortcutRecording();
    }

    private void StartShortcutRecording()
    {
        _isRecordingShortcut = true;
        _heldModifiers.Clear();
        _firstModifier = null;
        LblTarget.Text = T("请按快捷键…", "Press a shortcut…");
        LblTarget.Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0x6E, 0x00));
        LblChangeKey.Text = T("取消", "Cancel");
        _recordHookCallback = RecordHookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _recordHookHandle = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _recordHookCallback,
            GetModuleHandle(module?.ModuleName),
            0);
    }

    private void StopShortcutRecording(bool cancelled)
    {
        if (_recordHookHandle != 0)
        {
            UnhookWindowsHookEx(_recordHookHandle);
            _recordHookHandle = 0;
        }
        _recordHookCallback = null;
        _heldModifiers.Clear();
        _firstModifier = null;
        _isRecordingShortcut = false;
        if (cancelled) LblTarget.Text = Settings.Instance.CustomShortcut.DisplayName;
        LblTarget.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA2, 0x48));
        LblChangeKey.Text = T("更改", "Change");
    }

    private nint RecordHookCallback(int code, nint message, nint data)
    {
        if (code < 0) return CallNextHookEx(_recordHookHandle, code, message, data);
        var hook = Marshal.PtrToStructure<KeyboardHookData>(data);
        var isDown = message == WmKeyDown || message == WmSysKeyDown;
        var isUp = message == WmKeyUp || message == WmSysKeyUp;
        if (hook.VirtualKey == VirtualKeys.MediaPlayPause) return 1;

        if (isDown && hook.VirtualKey == VirtualKeys.Escape)
        {
            Dispatcher.InvokeAsync(() => StopShortcutRecording(cancelled: true));
            return 1;
        }

        if (IsModifier(hook.VirtualKey))
        {
            if (isDown)
            {
                _heldModifiers.Add(hook.VirtualKey);
                _firstModifier ??= hook.VirtualKey;
            }
            else if (isUp)
            {
                _heldModifiers.Remove(hook.VirtualKey);
                if (_heldModifiers.Count == 0 && _firstModifier is int modifier)
                    CaptureShortcut(new KeyStroke(
                        modifier,
                        KeyModifiers.None,
                        CurrentTriggerMode(),
                        MediaKeyInterceptor.GetKeyName(modifier)));
            }
            return 1;
        }

        if (isDown)
        {
            var modifiers = CurrentModifiers();
            var display = FormatShortcut(hook.VirtualKey, modifiers);
            CaptureShortcut(new KeyStroke(hook.VirtualKey, modifiers, CurrentTriggerMode(), display));
            return 1;
        }
        return CallNextHookEx(_recordHookHandle, code, message, data);
    }

    private void CaptureShortcut(KeyStroke shortcut)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Settings.Instance.CustomShortcut = shortcut;
            Settings.Instance.Save();
            _interceptor.Shortcut = shortcut;
            LblTarget.Text = shortcut.DisplayName;
            StopShortcutRecording(cancelled: false);
        });
    }

    private KeyModifiers CurrentModifiers()
    {
        var value = KeyModifiers.None;
        foreach (var key in _heldModifiers)
        {
            if (key is VirtualKeys.LeftControl or VirtualKeys.RightControl) value |= KeyModifiers.Control;
            if (key is VirtualKeys.LeftShift or VirtualKeys.RightShift) value |= KeyModifiers.Shift;
            if (key is VirtualKeys.LeftAlt or VirtualKeys.RightAlt) value |= KeyModifiers.Alt;
            if (key is VirtualKeys.LeftWindows or VirtualKeys.RightWindows) value |= KeyModifiers.Windows;
        }
        return value;
    }

    private ShortcutTriggerMode CurrentTriggerMode() =>
        CmbTriggerMode.SelectedIndex == 1 ? ShortcutTriggerMode.Tap : ShortcutTriggerMode.Hold;

    private static bool IsModifier(int key) => key is
        VirtualKeys.LeftControl or VirtualKeys.RightControl or
        VirtualKeys.LeftShift or VirtualKeys.RightShift or
        VirtualKeys.LeftAlt or VirtualKeys.RightAlt or
        VirtualKeys.LeftWindows or VirtualKeys.RightWindows;

    private static string FormatShortcut(int key, KeyModifiers modifiers)
    {
        var names = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) names.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Shift)) names.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt)) names.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Windows)) names.Add("Win");
        names.Add(MediaKeyInterceptor.GetKeyName(key));
        return string.Join(" + ", names);
    }

    private void CmbTriggerMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || CmbTriggerMode.SelectedIndex < 0) return;
        Settings.Instance.TargetTriggerMode = CurrentTriggerMode();
        Settings.Instance.Save();
        ApplyInputMethod();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        Settings.Instance.CustomShortcut = new KeyStroke(
            VirtualKeys.RightAlt,
            KeyModifiers.None,
            ShortcutTriggerMode.Hold,
            "Right Alt");
        Settings.Instance.Save();
        _suppressEvents = true;
        CmbTriggerMode.SelectedIndex = 0;
        _suppressEvents = false;
        LblTarget.Text = "Right Alt";
        ApplyInputMethod();
    }

    private static string T(string chinese, string english) => I18n.IsChinese ? chinese : english;

    private sealed record InputOption(InputMethodKind Kind, string DisplayName, string Description);

    private delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public nint ExtraInfo;
    }
}
