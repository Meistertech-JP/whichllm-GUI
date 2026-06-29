Option Strict On
Option Explicit On

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Reflection
Imports System.Windows
Imports System.Windows.Input
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Engine
Imports WhichLlm.Core.Export
Imports WhichLlm.Core.Services
Imports WhichLlm.Core.Utilities
Imports WhichLlm.Gui.Infrastructure
Imports WhichLlm.Gui.Models

Namespace ViewModels
    Public Class MainViewModel
        Inherits ObservableObject

        Private ReadOnly _service As WhichLlmApplicationService
        Private ReadOnly _export As New ExportService()
        Private _lastRanking As RankingResult
        Private _lastHardware As HardwareInfo
        Private _lastPlan As PlanResult
        Private _lastUpgrade As UpgradeResult
        Private _isBusy As Boolean
        Private _statusMessage As String = AppText.Text("準備完了", "Ready")
        Private _selectedResult As RankedModelRow
        Private _selectedLanguage As String = AppText.InitialLanguage()
        Private _selectedUseCase As String = "general"
        Private _selectedEvidence As String = "base"
        Private _selectedFit As String = "any"
        Private _selectedSpeed As String = "any"
        Private _selectedGpuGroup As String = "auto"
        Private _quantText As String = ""
        Private _simulatedGpuText As String = ""
        Private _planQuery As String = "llama 3 8b"
        Private _planQuant As String = ""
        Private _upgradeTargetsText As String = "RTX 4090, RX 7900 XTX"
        Private _snippetQuery As String = "qwen 7b"
        Private _snippetQuant As String = "Q4_K_M"
        Private ReadOnly _allQuantOptions As List(Of String) = DefaultQuantOptions()
        Private ReadOnly _knownModelOptions As List(Of String) = DefaultModelOptions()
        Private _allGpuOptions As New List(Of String)()
        Private _loadingModelSuggestions As Boolean
        Private _modelSuggestionsLoaded As Boolean
        Private Const DefaultSuggestionLimit As Integer = 28
        Private Const ModelSuggestionLimit As Integer = 140
        Private Const GpuSuggestionLimit As Integer = 120

        Public Event LanguageChanged As EventHandler

        Public Sub New()
            _service = WhichLlmApplicationService.CreateDefault()

            AppText.CurrentLanguage = _selectedLanguage
            LanguageOptions = New ObservableCollection(Of ComboOption)()
            UseCases = New ObservableCollection(Of ComboOption)()
            EvidenceModes = New ObservableCollection(Of ComboOption)()
            FitModes = New ObservableCollection(Of ComboOption)()
            SpeedModes = New ObservableCollection(Of ComboOption)()
            GpuGroupOptions = New ObservableCollection(Of ComboOption)()
            RefreshLocalizedChoiceLists()
            RefreshGpuGroupOptions(Nothing)

            _allGpuOptions = _service.AllGpuNames().ToList()
            QuantOptions = New ObservableCollection(Of String)()
            PlanQuantOptions = New ObservableCollection(Of String)()
            SnippetQuantOptions = New ObservableCollection(Of String)()
            GpuOptions = New ObservableCollection(Of String)()
            UpgradeGpuOptions = New ObservableCollection(Of String)()
            PlanModelOptions = New ObservableCollection(Of String)()
            SnippetModelOptions = New ObservableCollection(Of String)()
            RefreshSuggestionLists()

            RecommendationRows = New ObservableCollection(Of RankedModelRow)()
            HardwareRows = New ObservableCollection(Of HardwareGpuRow)()
            PlanRows = New ObservableCollection(Of PlanDisplayRow)()
            UpgradeRows = New ObservableCollection(Of UpgradeDisplayRow)()

            RunRecommendationCommand = New AsyncRelayCommand(AddressOf RunRecommendationAsync)
            DetectHardwareCommand = New AsyncRelayCommand(AddressOf DetectHardwareAsync)
            RunPlanCommand = New AsyncRelayCommand(AddressOf RunPlanAsync)
            RunUpgradeCommand = New AsyncRelayCommand(AddressOf RunUpgradeAsync)
            GenerateSnippetCommand = New AsyncRelayCommand(AddressOf GenerateSnippetAsync)
            CopyMarkdownCommand = New RelayCommand(AddressOf CopyMarkdown, Function() _lastRanking IsNot Nothing)
            CopyJsonCommand = New RelayCommand(AddressOf CopyJson, Function() _lastRanking IsNot Nothing)
            CopySnippetCommand = New RelayCommand(AddressOf CopySnippet, Function() Not String.IsNullOrWhiteSpace(SnippetCode))
        End Sub

        Public Property TopText As String = "10"
        Public Property ContextText As String = "4096"
        Public Property MinSpeedText As String = ""
        Public Property MinParamsText As String = ""
        Public Property CpuOnly As Boolean
        Public Property Refresh As Boolean
        Public Property VramOverrideText As String = ""
        Public Property BandwidthOverrideText As String = ""
        Public Property VramHeadroomText As String = "auto"
        Public Property RamBudgetText As String = "available"
        Public Property PlanContextText As String = "4096"

        Public Property SelectedLanguage As String
            Get
                Return _selectedLanguage
            End Get
            Set(value As String)
                Dim normalized = If(String.Equals(value, AppText.English, StringComparison.OrdinalIgnoreCase), AppText.English, AppText.Japanese)
                If SetProperty(_selectedLanguage, normalized) Then
                    AppText.CurrentLanguage = normalized
                    AppText.SaveLanguage(normalized)
                    RefreshLocalizedChoiceLists()
                    OnPropertyChanged(NameOf(SelectedLanguageOption))
                    RefreshRowsForLanguage()
                    OnPropertyChanged(NameOf(AppTitle))
                    OnPropertyChanged(NameOf(AppVersion))
                    OnPropertyChanged(NameOf(IsBusyLabel))
                    OnPropertyChanged(NameOf(SelectedDetails))
                    StatusMessage = L("表示言語を変更しました", "Display language changed")
                    RaiseEvent LanguageChanged(Me, EventArgs.Empty)
                End If
            End Set
        End Property

        Public Property SelectedLanguageOption As ComboOption
            Get
                If LanguageOptions Is Nothing Then Return Nothing
                Return LanguageOptions.FirstOrDefault(Function(optionItem) String.Equals(optionItem.Value, SelectedLanguage, StringComparison.OrdinalIgnoreCase))
            End Get
            Set(value As ComboOption)
                If value Is Nothing Then Return
                SelectedLanguage = value.Value
            End Set
        End Property

        Public Property QuantText As String
            Get
                Return _quantText
            End Get
            Set(value As String)
                If SetProperty(_quantText, If(value, "")) Then RefreshOptions(QuantOptions, _allQuantOptions, _quantText)
            End Set
        End Property

        Public Property SimulatedGpuText As String
            Get
                Return _simulatedGpuText
            End Get
            Set(value As String)
                If SetProperty(_simulatedGpuText, If(value, "")) Then RefreshOptions(GpuOptions, _allGpuOptions, _simulatedGpuText, GpuSuggestionLimit)
            End Set
        End Property

        Public Property PlanQuery As String
            Get
                Return _planQuery
            End Get
            Set(value As String)
                If SetProperty(_planQuery, If(value, "")) Then RefreshOptions(PlanModelOptions, _knownModelOptions, _planQuery, ModelSuggestionLimit)
            End Set
        End Property

        Public Property PlanQuant As String
            Get
                Return _planQuant
            End Get
            Set(value As String)
                If SetProperty(_planQuant, If(value, "")) Then RefreshOptions(PlanQuantOptions, _allQuantOptions, _planQuant)
            End Set
        End Property

        Public Property UpgradeTargetsText As String
            Get
                Return _upgradeTargetsText
            End Get
            Set(value As String)
                If SetProperty(_upgradeTargetsText, If(value, "")) Then RefreshOptions(UpgradeGpuOptions, _allGpuOptions, _upgradeTargetsText, GpuSuggestionLimit)
            End Set
        End Property

        Public Property SnippetQuery As String
            Get
                Return _snippetQuery
            End Get
            Set(value As String)
                If SetProperty(_snippetQuery, If(value, "")) Then RefreshOptions(SnippetModelOptions, _knownModelOptions, _snippetQuery, ModelSuggestionLimit)
            End Set
        End Property

        Public Property SnippetQuant As String
            Get
                Return _snippetQuant
            End Get
            Set(value As String)
                If SetProperty(_snippetQuant, If(value, "")) Then RefreshOptions(SnippetQuantOptions, _allQuantOptions, _snippetQuant)
            End Set
        End Property

        Public ReadOnly Property UseCases As ObservableCollection(Of ComboOption)
        Public ReadOnly Property EvidenceModes As ObservableCollection(Of ComboOption)
        Public ReadOnly Property FitModes As ObservableCollection(Of ComboOption)
        Public ReadOnly Property SpeedModes As ObservableCollection(Of ComboOption)
        Public ReadOnly Property GpuGroupOptions As ObservableCollection(Of ComboOption)
        Public ReadOnly Property LanguageOptions As ObservableCollection(Of ComboOption)
        Public ReadOnly Property QuantOptions As ObservableCollection(Of String)
        Public ReadOnly Property PlanQuantOptions As ObservableCollection(Of String)
        Public ReadOnly Property SnippetQuantOptions As ObservableCollection(Of String)
        Public ReadOnly Property GpuOptions As ObservableCollection(Of String)
        Public ReadOnly Property UpgradeGpuOptions As ObservableCollection(Of String)
        Public ReadOnly Property PlanModelOptions As ObservableCollection(Of String)
        Public ReadOnly Property SnippetModelOptions As ObservableCollection(Of String)
        Public ReadOnly Property RecommendationRows As ObservableCollection(Of RankedModelRow)
        Public ReadOnly Property HardwareRows As ObservableCollection(Of HardwareGpuRow)
        Public ReadOnly Property PlanRows As ObservableCollection(Of PlanDisplayRow)
        Public ReadOnly Property UpgradeRows As ObservableCollection(Of UpgradeDisplayRow)

        Public ReadOnly Property RunRecommendationCommand As ICommand
        Public ReadOnly Property DetectHardwareCommand As ICommand
        Public ReadOnly Property RunPlanCommand As ICommand
        Public ReadOnly Property RunUpgradeCommand As ICommand
        Public ReadOnly Property GenerateSnippetCommand As ICommand
        Public ReadOnly Property CopyMarkdownCommand As RelayCommand
        Public ReadOnly Property CopyJsonCommand As RelayCommand
        Public ReadOnly Property CopySnippetCommand As RelayCommand

        Public Property SelectedUseCase As String
            Get
                Return _selectedUseCase
            End Get
            Set(value As String)
                If SetProperty(_selectedUseCase, NormalizeChoice(value, "general")) Then
                    OnPropertyChanged(NameOf(SelectedUseCaseOption))
                End If
            End Set
        End Property

        Public Property SelectedUseCaseOption As ComboOption
            Get
                Return OptionFor(UseCases, SelectedUseCase)
            End Get
            Set(value As ComboOption)
                If value Is Nothing Then Return
                SelectedUseCase = value.Value
            End Set
        End Property

        Public Property SelectedEvidence As String
            Get
                Return _selectedEvidence
            End Get
            Set(value As String)
                If SetProperty(_selectedEvidence, NormalizeChoice(value, "base")) Then
                    OnPropertyChanged(NameOf(SelectedEvidenceOption))
                End If
            End Set
        End Property

        Public Property SelectedEvidenceOption As ComboOption
            Get
                Return OptionFor(EvidenceModes, SelectedEvidence)
            End Get
            Set(value As ComboOption)
                If value Is Nothing Then Return
                SelectedEvidence = value.Value
            End Set
        End Property

        Public Property SelectedFit As String
            Get
                Return _selectedFit
            End Get
            Set(value As String)
                If SetProperty(_selectedFit, NormalizeChoice(value, "any")) Then
                    OnPropertyChanged(NameOf(SelectedFitOption))
                End If
            End Set
        End Property

        Public Property SelectedFitOption As ComboOption
            Get
                Return OptionFor(FitModes, SelectedFit)
            End Get
            Set(value As ComboOption)
                If value Is Nothing Then Return
                SelectedFit = value.Value
            End Set
        End Property

        Public Property SelectedSpeed As String
            Get
                Return _selectedSpeed
            End Get
            Set(value As String)
                If SetProperty(_selectedSpeed, NormalizeChoice(value, "any")) Then
                    OnPropertyChanged(NameOf(SelectedSpeedOption))
                End If
            End Set
        End Property

        Public Property SelectedSpeedOption As ComboOption
            Get
                Return OptionFor(SpeedModes, SelectedSpeed)
            End Get
            Set(value As ComboOption)
                If value Is Nothing Then Return
                SelectedSpeed = value.Value
            End Set
        End Property

        Public Property SelectedGpuGroup As String
            Get
                Return _selectedGpuGroup
            End Get
            Set(value As String)
                If SetProperty(_selectedGpuGroup, NormalizeChoice(value, "auto")) Then
                    OnPropertyChanged(NameOf(SelectedGpuGroupOption))
                End If
            End Set
        End Property

        Public Property SelectedGpuGroupOption As ComboOption
            Get
                Return OptionFor(GpuGroupOptions, SelectedGpuGroup)
            End Get
            Set(value As ComboOption)
                If value Is Nothing Then Return
                SelectedGpuGroup = value.Value
            End Set
        End Property

        Public Property SelectedResult As RankedModelRow
            Get
                Return _selectedResult
            End Get
            Set(value As RankedModelRow)
                If SetProperty(_selectedResult, value) Then
                    OnPropertyChanged(NameOf(SelectedDetails))
                End If
            End Set
        End Property

        Public ReadOnly Property SelectedDetails As String
            Get
                If SelectedResult Is Nothing Then Return L("「おすすめを探す」を押すと、このPCで使えるモデルの詳細がここに出ます。", "Press Find Recommendations to show details for models that can run on this PC.")
                Return SelectedResult.Details
            End Get
        End Property

        Private _hasResults As Boolean
        Public Property HasResults As Boolean
            Get
                Return _hasResults
            End Get
            Set(value As Boolean)
                If SetProperty(_hasResults, value) Then
                    OnPropertyChanged(NameOf(ShowEmptyHint))
                End If
            End Set
        End Property

        Public ReadOnly Property ShowEmptyHint As Boolean
            Get
                Return Not _hasResults
            End Get
        End Property

        Public ReadOnly Property TopPick As RankedModelRow
            Get
                Return RecommendationRows.FirstOrDefault()
            End Get
        End Property

        Public Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Set(value As Boolean)
                If SetProperty(_isBusy, value) Then
                    OnPropertyChanged(NameOf(IsBusyLabel))
                End If
            End Set
        End Property

        Public ReadOnly Property IsBusyLabel As String
            Get
                Return If(IsBusy, L("処理中", "Working"), L("待機中", "Idle"))
            End Get
        End Property

        Public Property StatusMessage As String
            Get
                Return _statusMessage
            End Get
            Set(value As String)
                SetProperty(_statusMessage, value)
            End Set
        End Property

        Private _hardwareSummary As String = ""
        Public Property HardwareSummary As String
            Get
                Return _hardwareSummary
            End Get
            Set(value As String)
                SetProperty(_hardwareSummary, value)
            End Set
        End Property

        Private _recommendationHardwareSummary As String = AppText.Text("このPC: 検出中...", "This PC: detecting...")
        Public Property RecommendationHardwareSummary As String
            Get
                Return _recommendationHardwareSummary
            End Get
            Set(value As String)
                SetProperty(_recommendationHardwareSummary, value)
            End Set
        End Property

        Private _planSummary As String = ""
        Public Property PlanSummary As String
            Get
                Return _planSummary
            End Get
            Set(value As String)
                SetProperty(_planSummary, value)
            End Set
        End Property

        Private _upgradeSummary As String = ""
        Public Property UpgradeSummary As String
            Get
                Return _upgradeSummary
            End Get
            Set(value As String)
                SetProperty(_upgradeSummary, value)
            End Set
        End Property

        Private _snippetCommandLine As String = ""
        Public Property SnippetCommandLine As String
            Get
                Return _snippetCommandLine
            End Get
            Set(value As String)
                SetProperty(_snippetCommandLine, value)
                CopySnippetCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        Private _snippetCode As String = ""
        Public Property SnippetCode As String
            Get
                Return _snippetCode
            End Get
            Set(value As String)
                SetProperty(_snippetCode, value)
                CopySnippetCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        Public ReadOnly Property CachePath As String
            Get
                Return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whichllm-gui", "cache")
            End Get
        End Property

        Public ReadOnly Property HfEndpoint As String
            Get
                Dim value = Environment.GetEnvironmentVariable("HF_ENDPOINT")
                If String.IsNullOrWhiteSpace(value) Then Return "https://huggingface.co"
                Return value
            End Get
        End Property

        Public ReadOnly Property AppVersion As String
            Get
                Dim informational = GetType(MainViewModel).Assembly.GetCustomAttribute(Of AssemblyInformationalVersionAttribute)()?.InformationalVersion
                If Not String.IsNullOrWhiteSpace(informational) Then
                    Return "v" & informational.Split("+"c)(0)
                End If

                Dim assemblyVersion = GetType(MainViewModel).Assembly.GetName().Version
                If assemblyVersion Is Nothing Then Return "v0.1.0"
                Return "v" & assemblyVersion.ToString(3)
            End Get
        End Property

        Public ReadOnly Property AppTitle As String
            Get
                Return $"whichllm GUI {AppVersion}"
            End Get
        End Property

        Public Async Function InitializeAsync() As Task
            Await DetectHardwareAsync()
            Await RefreshModelSuggestionsAsync()
            ' 起動直後に一度おすすめを出して、空の画面ではなく「答え」を見せる。
            Await RunRecommendationAsync()
        End Function

        Private Async Function RunRecommendationAsync() As Task
            Await RunWithBusyAsync(L("推薦を計算しています...", "Calculating recommendations..."), Async Function()
                Dim options = BuildOptions()
                Dim result = Await _service.RankAsync(options)
                _lastRanking = result
                RecommendationHardwareSummary = BuildHardwareInline(result.Hardware)
                RememberModels(result.Models.Select(Function(row) row.Model))
                RecommendationRows.Clear()
                For Each row In result.Models
                    RecommendationRows.Add(New RankedModelRow(row))
                Next
                SelectedResult = RecommendationRows.FirstOrDefault()
                HasResults = RecommendationRows.Count > 0
                OnPropertyChanged(NameOf(TopPick))
                StatusMessage = If(HasResults,
                    L($"おすすめが出ました（{RecommendationRows.Count}件）", $"Found {RecommendationRows.Count} recommendations"),
                    L("条件に合うモデルが見つかりませんでした。詳細条件をゆるめてお試しください。", "No models matched the current filters. Try loosening the advanced filters."))
                CopyMarkdownCommand.RaiseCanExecuteChanged()
                CopyJsonCommand.RaiseCanExecuteChanged()
            End Function)
        End Function

        Private Async Function DetectHardwareAsync() As Task
            Await RunWithBusyAsync(L("ハードウェアを検出しています...", "Detecting hardware..."), Async Function()
                Dim hardware = Await _service.DetectHardwareAsync(BuildOptions())
                _lastHardware = hardware
                HardwareRows.Clear()
                For Each gpu In hardware.Gpus
                    HardwareRows.Add(New HardwareGpuRow(gpu))
                Next
                RefreshGpuGroupOptions(hardware)
                HardwareSummary = BuildHardwareSummary(hardware)
                RecommendationHardwareSummary = BuildHardwareInline(hardware)
                StatusMessage = L("ハードウェア検出完了", "Hardware detection complete")
            End Function)
        End Function

        Private Async Function RunPlanAsync() As Task
            Await RunWithBusyAsync(L("プランを作成しています...", "Creating plan..."), Async Function()
                Await RefreshModelSuggestionsAsync()
                Dim options = BuildOptions()
                Dim contextLength = InputParsers.ParseContextLength(PlanContextText, options.ContextLength)
                Dim result = Await _service.PlanAsync(PlanQuery, PlanQuant, contextLength, options)
                _lastPlan = result
                RememberModels(New List(Of ModelInfo) From {result.MatchedModel})
                PlanRows.Clear()
                For Each row In result.Rows
                    PlanRows.Add(New PlanDisplayRow(row))
                Next
                PlanSummary = L($"候補: {result.MatchedModel.RepoId} / 文脈長 {result.ContextLength:N0}", $"Matched: {result.MatchedModel.RepoId} / context {result.ContextLength:N0}")
                StatusMessage = L("プランを作成しました", "Plan created")
            End Function)
        End Function

        Private Async Function RunUpgradeAsync() As Task
            Await RunWithBusyAsync(L("GPU比較を計算しています...", "Calculating GPU comparison..."), Async Function()
                Dim targets = SplitInputs(UpgradeTargetsText)
                Dim result = Await _service.UpgradeAsync(targets, BuildOptions())
                _lastUpgrade = result
                UpgradeRows.Clear()
                For Each row In result.Rows
                    UpgradeRows.Add(New UpgradeDisplayRow(row))
                Next
                UpgradeSummary = L($"現在のPCの最有力: {result.CurrentTopModel} ({result.CurrentScore:0.0})", $"Current top model: {result.CurrentTopModel} ({result.CurrentScore:0.0})")
                StatusMessage = L("GPU比較を作成しました", "GPU comparison created")
            End Function)
        End Function

        Private Async Function GenerateSnippetAsync() As Task
            Await RunWithBusyAsync(L("スニペットを生成しています...", "Generating snippet..."), Async Function()
                Await RefreshModelSuggestionsAsync()
                Dim result = Await _service.SnippetAsync(SnippetQuery, SnippetQuant, BuildOptions())
                RememberModels(New List(Of ModelInfo) From {result.Model})
                SnippetCommandLine = result.CommandLine
                SnippetCode = result.Code
                StatusMessage = L($"スニペット生成完了: {result.Model.RepoId}", $"Snippet generated: {result.Model.RepoId}")
            End Function)
        End Function

        Private Sub CopyMarkdown()
            If _lastRanking Is Nothing Then Return
            CopyTextToClipboard(_export.RankingToMarkdown(_lastRanking), L("Markdown表をコピーしました", "Markdown table copied"))
        End Sub

        Private Sub CopyJson()
            If _lastRanking Is Nothing Then Return
            CopyTextToClipboard(_export.RankingToJson(_lastRanking), L("JSONをコピーしました", "JSON copied"))
        End Sub

        Private Sub CopySnippet()
            CopyTextToClipboard(SnippetCommandLine & Environment.NewLine & Environment.NewLine & SnippetCode, L("スニペットをコピーしました", "Snippet copied"))
        End Sub

        Private Sub CopyTextToClipboard(text As String, successMessage As String)
            For attempt = 1 To 3
                Try
                    Clipboard.SetDataObject(text, True)
                    StatusMessage = successMessage
                    Return
                Catch ex As Exception When attempt < 3
                    Global.System.Threading.Thread.Sleep(80)
                Catch ex As Exception
                    StatusMessage = L("クリップボードにコピーできませんでした。ほかのアプリが使用中の可能性があります。", "Could not copy to the clipboard. Another app may be using it.")
                    Return
                End Try
            Next
        End Sub

        Private Async Function RunWithBusyAsync(message As String, work As Func(Of Task)) As Task
            IsBusy = True
            StatusMessage = message
            Try
                Await work()
            Catch ex As Exception
                StatusMessage = L("エラー: ", "Error: ") & ex.Message
            Finally
                IsBusy = False
            End Try
        End Function

        Private Function BuildOptions() As RankingOptions
            Dim options As New RankingOptions With {
                .Top = ParseInteger(TopText, 10),
                .ContextLength = InputParsers.ParseContextLength(ContextText, 4096),
                .Quant = If(QuantText, "").Trim(),
                .Speed = NormalizeChoice(SelectedSpeed, "any"),
                .Fit = NormalizeChoice(SelectedFit, "any"),
                .CpuOnly = CpuOnly,
                .Profile = ProfileForUseCase(NormalizeChoice(SelectedUseCase, "general")),
                .UseCase = NormalizeChoice(SelectedUseCase, "general"),
                .Evidence = NormalizeChoice(SelectedEvidence, "base"),
                .Refresh = Refresh,
                .GpuGroupKey = NormalizeChoice(SelectedGpuGroup, "auto"),
                .VramHeadroom = If(String.IsNullOrWhiteSpace(VramHeadroomText), "auto", VramHeadroomText.Trim()),
                .RamBudget = If(String.IsNullOrWhiteSpace(RamBudgetText), "available", RamBudgetText.Trim())
            }

            Dim minSpeed As Double
            If Double.TryParse(MinSpeedText, NumberStyles.Float, CultureInfo.InvariantCulture, minSpeed) Then
                options.MinSpeedTokPerSec = minSpeed
            End If

            Dim minParams As Double
            If Double.TryParse(MinParamsText, NumberStyles.Float, CultureInfo.InvariantCulture, minParams) Then
                options.MinParamsB = minParams
            End If

            options.SimulatedGpuInputs.AddRange(SplitInputs(SimulatedGpuText))
            If options.SimulatedGpuInputs.Count > 0 Then options.GpuGroupKey = "auto"
            If Not String.IsNullOrWhiteSpace(VramOverrideText) Then
                options.OverrideVramBytes = ParseGbOrBytes(VramOverrideText)
            End If
            If Not String.IsNullOrWhiteSpace(BandwidthOverrideText) Then
                Dim bandwidth As Double
                If Double.TryParse(BandwidthOverrideText, NumberStyles.Float, CultureInfo.InvariantCulture, bandwidth) Then
                    options.OverrideBandwidthGbps = bandwidth
                End If
            End If

            Return options
        End Function

        Private Async Function RefreshModelSuggestionsAsync() As Task
            If _loadingModelSuggestions OrElse _modelSuggestionsLoaded Then Return

            _loadingModelSuggestions = True
            Try
                Dim suggestions = Await _service.LoadModelSuggestionsAsync(BuildOptions())
                RememberModels(suggestions)
                _modelSuggestionsLoaded = True
                If Not IsBusy Then
                    StatusMessage = L($"モデル情報を準備しました（{_knownModelOptions.Count}件）", $"Prepared {_knownModelOptions.Count} model suggestions")
                End If
            Catch
                ' モデル候補の取得は入力補助なので、オフライン環境では既存候補のまま続行する。
            Finally
                _loadingModelSuggestions = False
            End Try
        End Function

        Private Sub RememberModels(models As IEnumerable(Of ModelInfo))
            Dim changed = False
            For Each model In models
                If model Is Nothing OrElse String.IsNullOrWhiteSpace(model.RepoId) Then Continue For
                If Not _knownModelOptions.Any(Function(existing) existing.Equals(model.RepoId, StringComparison.OrdinalIgnoreCase)) Then
                    _knownModelOptions.Add(model.RepoId)
                    changed = True
                End If
            Next
            If changed Then
                RefreshOptions(PlanModelOptions, _knownModelOptions, PlanQuery, ModelSuggestionLimit)
                RefreshOptions(SnippetModelOptions, _knownModelOptions, SnippetQuery, ModelSuggestionLimit)
            End If
        End Sub

        Private Sub RefreshSuggestionLists()
            RefreshOptions(QuantOptions, _allQuantOptions, QuantText)
            RefreshOptions(PlanQuantOptions, _allQuantOptions, PlanQuant)
            RefreshOptions(SnippetQuantOptions, _allQuantOptions, SnippetQuant)
            RefreshOptions(GpuOptions, _allGpuOptions, SimulatedGpuText, GpuSuggestionLimit)
            RefreshOptions(UpgradeGpuOptions, _allGpuOptions, UpgradeTargetsText, GpuSuggestionLimit)
            RefreshOptions(PlanModelOptions, _knownModelOptions, PlanQuery, ModelSuggestionLimit)
            RefreshOptions(SnippetModelOptions, _knownModelOptions, SnippetQuery, ModelSuggestionLimit)
        End Sub

        Private Sub RefreshLocalizedChoiceLists()
            ReplaceOptions(LanguageOptions, New List(Of ComboOption) From {
                New ComboOption With {.Label = "日本語", .Value = AppText.Japanese},
                New ComboOption With {.Label = "English", .Value = AppText.English}
            })
            ReplaceOptions(UseCases, New List(Of ComboOption) From {
                New ComboOption With {.Label = L("日常の文章と調べ物", "Everyday writing and research"), .Value = "general"},
                New ComboOption With {.Label = L("会話", "Chat"), .Value = "chat"},
                New ComboOption With {.Label = L("プログラミング", "Coding"), .Value = "coding"},
                New ComboOption With {.Label = L("論理・数学", "Reasoning and math"), .Value = "reasoning"},
                New ComboOption With {.Label = L("画像も使う", "Images too"), .Value = "multimodal"},
                New ComboOption With {.Label = L("検索・分類", "Search and classification"), .Value = "embedding"},
                New ComboOption With {.Label = L("すべて見る", "Show everything"), .Value = "any"}
            })
            ReplaceOptions(EvidenceModes, New List(Of ComboOption) From {
                New ComboOption With {.Label = L("直接・近いモデルの根拠", "Direct or close evidence"), .Value = "base"},
                New ComboOption With {.Label = L("直接ベンチマークのみ", "Direct benchmarks only"), .Value = "strict"},
                New ComboOption With {.Label = L("根拠なしも含める", "Include models without evidence"), .Value = "any"}
            })
            ReplaceOptions(FitModes, New List(Of ComboOption) From {
                New ComboOption With {.Label = L("CPUのみも含める", "Include CPU-only too"), .Value = "any"},
                New ComboOption With {.Label = L("GPUからあふれてもOK", "OK if it spills from GPU"), .Value = "gpu"},
                New ComboOption With {.Label = L("GPUだけで快適（VRAM内）", "Fits entirely in GPU memory"), .Value = "full-gpu"}
            })
            ReplaceOptions(SpeedModes, New List(Of ComboOption) From {
                New ComboOption With {.Label = L("速度で絞り込まない", "Do not filter by speed"), .Value = "any"},
                New ComboOption With {.Label = L("普段使い向け (10 tok/s以上)", "Everyday use (10+ tok/s)"), .Value = "usable"},
                New ComboOption With {.Label = L("高速応答 (30 tok/s以上)", "Fast response (30+ tok/s)"), .Value = "fast"}
            })
            RefreshGpuGroupOptions(_lastHardware)
            OnPropertyChanged(NameOf(SelectedUseCase))
            OnPropertyChanged(NameOf(SelectedEvidence))
            OnPropertyChanged(NameOf(SelectedFit))
            OnPropertyChanged(NameOf(SelectedSpeed))
            OnPropertyChanged(NameOf(SelectedUseCaseOption))
            OnPropertyChanged(NameOf(SelectedEvidenceOption))
            OnPropertyChanged(NameOf(SelectedFitOption))
            OnPropertyChanged(NameOf(SelectedSpeedOption))
            OnPropertyChanged(NameOf(SelectedGpuGroupOption))
        End Sub

        Private Sub RefreshGpuGroupOptions(hardware As HardwareInfo)
            Dim options As New List(Of ComboOption) From {
                New ComboOption With {.Label = L("自動（最大の互換グループ）", "Auto (largest compatible group)"), .Value = "auto"}
            }

            If hardware IsNot Nothing AndAlso hardware.Gpus.Count > 1 Then
                ' Combined multi-GPU groups (same compatibility key, 2+ devices that can be
                ' tensor-split for a single model). Single-device groups are represented by
                ' the explicit per-GPU entries below instead, to avoid duplicate choices.
                Dim groups = hardware.Gpus.
                    Where(Function(g) g IsNot Nothing AndAlso Not g.IsSharedMemory AndAlso Math.Max(0, g.EffectiveVramBytes) > 0).
                    GroupBy(Function(g) VramEstimator.MultiGpuCompatibilityKey(g), StringComparer.OrdinalIgnoreCase).
                    Where(Function(group) group.Count() >= 2).
                    Select(Function(group) New With {.Key = group.Key, .Gpus = group.ToList()}).
                    OrderByDescending(Function(group) group.Gpus.Sum(Function(g) Math.Max(0, g.EffectiveVramBytes))).
                    ThenByDescending(Function(group) group.Gpus.Count).
                    ThenByDescending(Function(group) group.Gpus.Sum(Function(g) If(g.MemoryBandwidthGbps, 0))).
                    ThenBy(Function(group) group.Key).
                    ToList()

                For Each group In groups
                    options.Add(New ComboOption With {
                        .Label = L("まとめて使う: ", "Combined: ") & BuildGpuGroupLabel(group.Gpus),
                        .Value = group.Key
                    })
                Next

                ' Explicit single-GPU targets: estimate as if only this one device is used.
                For i = 0 To hardware.Gpus.Count - 1
                    Dim gpu = hardware.Gpus(i)
                    If gpu Is Nothing Then Continue For
                    options.Add(New ComboOption With {
                        .Label = L($"GPU{i} のみ: ", $"GPU{i} only: ") & gpu.Name & $" ({Formatters.FormatBytes(gpu.VramBytes)})",
                        .Value = "gpu:" & i.ToString(CultureInfo.InvariantCulture)
                    })
                Next
            End If

            ReplaceOptions(GpuGroupOptions, options)
            If Not GpuGroupOptions.Any(Function(optionItem) optionItem.Value.Equals(SelectedGpuGroup, StringComparison.OrdinalIgnoreCase)) Then
                SelectedGpuGroup = "auto"
            Else
                OnPropertyChanged(NameOf(SelectedGpuGroupOption))
            End If
        End Sub

        Private Shared Function BuildGpuGroupLabel(gpus As IReadOnlyList(Of GpuInfo)) As String
            If gpus Is Nothing OrElse gpus.Count = 0 Then Return AppText.Text("GPUグループ", "GPU group")
            Dim counts = gpus.
                GroupBy(Function(g) If(g.Name, ""), StringComparer.OrdinalIgnoreCase).
                Select(Function(group) If(group.Count() > 1, $"{group.Key} x{group.Count()}", group.Key)).
                ToList()
            Dim rawBytes = gpus.Sum(Function(g) Math.Max(0, g.VramBytes))
            Dim key = VramEstimator.MultiGpuCompatibilityKey(gpus(0))
            Dim suffix = If(key.Contains(":", StringComparison.Ordinal), key.Split(":"c).Last(), key)
            Return $"{String.Join(" + ", counts)} ({suffix}, {Formatters.FormatBytes(rawBytes)})"
        End Function

        Private Shared Sub ReplaceOptions(target As ObservableCollection(Of ComboOption), values As IEnumerable(Of ComboOption))
            If target Is Nothing Then Return
            target.Clear()
            For Each value In values
                target.Add(value)
            Next
        End Sub

        Private Shared Function NormalizeChoice(value As String, fallback As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return fallback
            Return value.Trim()
        End Function

        Private Shared Function OptionFor(options As IEnumerable(Of ComboOption), selectedValue As String) As ComboOption
            If options Is Nothing Then Return Nothing
            Dim normalized = NormalizeChoice(selectedValue, "")
            Return options.FirstOrDefault(Function(optionItem) String.Equals(optionItem.Value, normalized, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Sub RefreshRowsForLanguage()
            If _lastRanking IsNot Nothing Then
                RecommendationHardwareSummary = BuildHardwareInline(_lastRanking.Hardware)
                Dim selectedModel = SelectedResult?.Model
                RecommendationRows.Clear()
                For Each row In _lastRanking.Models
                    RecommendationRows.Add(New RankedModelRow(row))
                Next
                SelectedResult = RecommendationRows.FirstOrDefault(Function(row) row.Model.Equals(If(selectedModel, ""), StringComparison.OrdinalIgnoreCase))
                If SelectedResult Is Nothing Then SelectedResult = RecommendationRows.FirstOrDefault()
                HasResults = RecommendationRows.Count > 0
                OnPropertyChanged(NameOf(TopPick))
            ElseIf _lastHardware IsNot Nothing Then
                RecommendationHardwareSummary = BuildHardwareInline(_lastHardware)
            Else
                RecommendationHardwareSummary = L("このPC: 検出中...", "This PC: detecting...")
            End If

            If _lastHardware IsNot Nothing Then
                HardwareRows.Clear()
                For Each gpu In _lastHardware.Gpus
                    HardwareRows.Add(New HardwareGpuRow(gpu))
                Next
                HardwareSummary = BuildHardwareSummary(_lastHardware)
            End If

            If _lastPlan IsNot Nothing Then
                PlanRows.Clear()
                For Each row In _lastPlan.Rows
                    PlanRows.Add(New PlanDisplayRow(row))
                Next
                PlanSummary = L($"候補: {_lastPlan.MatchedModel.RepoId} / 文脈長 {_lastPlan.ContextLength:N0}", $"Matched: {_lastPlan.MatchedModel.RepoId} / context {_lastPlan.ContextLength:N0}")
            End If

            If _lastUpgrade IsNot Nothing Then
                UpgradeRows.Clear()
                For Each row In _lastUpgrade.Rows
                    UpgradeRows.Add(New UpgradeDisplayRow(row))
                Next
                UpgradeSummary = L($"現在のPCの最有力: {_lastUpgrade.CurrentTopModel} ({_lastUpgrade.CurrentScore:0.0})", $"Current top model: {_lastUpgrade.CurrentTopModel} ({_lastUpgrade.CurrentScore:0.0})")
            End If
        End Sub

        Private Shared Sub RefreshOptions(target As ObservableCollection(Of String), candidates As IEnumerable(Of String), value As String, Optional limit As Integer = DefaultSuggestionLimit)
            If target Is Nothing Then Return
            Dim nextItems = FilterSuggestions(candidates, value, limit).ToList()
            target.Clear()
            For Each item In nextItems
                target.Add(item)
            Next
        End Sub

        Private Shared Function FilterSuggestions(candidates As IEnumerable(Of String), value As String, limit As Integer) As IEnumerable(Of String)
            Dim segment = LastInputSegment(value)
            Dim normalizedSegment = NormalizeSuggestion(segment)
            Dim unique = candidates.
                Where(Function(candidate) Not String.IsNullOrWhiteSpace(candidate)).
                Distinct(StringComparer.OrdinalIgnoreCase).
                ToList()
            Dim maxItems = Math.Max(1, limit)

            If String.IsNullOrWhiteSpace(normalizedSegment) Then
                Return unique.Take(maxItems)
            End If

            Return unique.
                Select(Function(candidate) New With {.Value = candidate, .Score = SuggestionScore(candidate, normalizedSegment)}).
                Where(Function(item) item.Score > 0).
                OrderByDescending(Function(item) item.Score).
                ThenBy(Function(item) item.Value.Length).
                Select(Function(item) item.Value).
                Take(maxItems)
        End Function

        Private Shared Function SuggestionScore(candidate As String, normalizedSegment As String) As Integer
            Dim normalizedCandidate = NormalizeSuggestion(candidate)
            If normalizedCandidate.Equals(normalizedSegment, StringComparison.Ordinal) Then Return 100
            If normalizedCandidate.StartsWith(normalizedSegment, StringComparison.Ordinal) Then Return 85
            If normalizedCandidate.Contains(normalizedSegment, StringComparison.Ordinal) Then Return 65

            Dim terms = normalizedSegment.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            If terms.Length > 1 AndAlso terms.All(Function(term) normalizedCandidate.Contains(term, StringComparison.Ordinal)) Then Return 48
            Return 0
        End Function

        Private Shared Function LastInputSegment(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return ""
            Dim parts = value.Split({","c, ";"c, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Return value.Trim()
            Return parts(parts.Length - 1).Trim()
        End Function

        Private Shared Function NormalizeSuggestion(value As String) As String
            Return If(value, "").Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ")
        End Function

        Private Shared Function DefaultQuantOptions() As List(Of String)
            Dim values As New List(Of String)(QuantizationRules.PreferredQuants())
            values.AddRange(New String() {"Q4_K_S", "Q5_K_S", "Q3_K_L", "Q3_K_M", "Q2_K", "Q8_0", "QAT", "AWQ", "GPTQ", "FP8", "FP16", "BF16", "F16"})
            Return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        End Function

        Private Shared Function DefaultModelOptions() As List(Of String)
            Return New List(Of String) From {
                "Qwen/Qwen3-4B",
                "Qwen/Qwen3-8B",
                "Qwen/Qwen3-14B",
                "Qwen/Qwen2.5-7B-Instruct",
                "Qwen/Qwen2.5-Coder-7B-Instruct",
                "google/gemma-3-4b-it",
                "google/gemma-3-12b-it",
                "google/gemma-3-27b-it",
                "meta-llama/Llama-3.1-8B-Instruct",
                "meta-llama/Llama-3.2-3B-Instruct",
                "mistralai/Mistral-7B-Instruct-v0.3",
                "microsoft/Phi-4-mini-instruct",
                "microsoft/Phi-3.5-mini-instruct",
                "deepseek-ai/DeepSeek-R1-Distill-Qwen-7B",
                "BAAI/bge-small-en-v1.5",
                "BAAI/bge-m3"
            }
        End Function

        Private Shared Function ParseInteger(value As String, fallback As Integer) As Integer
            Dim parsed As Integer
            If Integer.TryParse(value, parsed) AndAlso parsed > 0 Then Return parsed
            Return fallback
        End Function

        Private Shared Function BuildHardwareSummary(hardware As HardwareInfo) As String
            Dim lines As New List(Of String) From {
                $"CPU: {hardware.CpuName}",
                AppText.Text($"物理コア: {hardware.PhysicalCores} / AVX2: {hardware.SupportsAvx2} / AVX-512: {hardware.SupportsAvx512}", $"Physical cores: {hardware.PhysicalCores} / AVX2: {hardware.SupportsAvx2} / AVX-512: {hardware.SupportsAvx512}"),
                AppText.Text($"メモリ: 合計 {Formatters.FormatBytes(hardware.TotalRamBytes)} / 空き {Formatters.FormatBytes(hardware.AvailableRamBytes)}", $"RAM: total {Formatters.FormatBytes(hardware.TotalRamBytes)} / free {Formatters.FormatBytes(hardware.AvailableRamBytes)}"),
                AppText.Text($"ディスク空き: {Formatters.FormatBytes(hardware.FreeDiskBytes)}", $"Free disk: {Formatters.FormatBytes(hardware.FreeDiskBytes)}")
            }
            lines.AddRange(hardware.DetectionNotes.Concat(hardware.BudgetNotes).Select(AddressOf FriendlyHardwareNote))
            Return String.Join(Environment.NewLine, lines.Where(Function(line) Not String.IsNullOrWhiteSpace(line)))
        End Function

        Private Shared Function BuildHardwareInline(hardware As HardwareInfo) As String
            Dim cpu = If(String.IsNullOrWhiteSpace(hardware.CpuName), AppText.Text("未検出", "not detected"), hardware.CpuName)
            Dim gpuText = If(hardware.Gpus.Count = 0,
                AppText.Text("GPU: 未検出", "GPU: not detected"),
                "GPU: " & String.Join(", ", hardware.Gpus.Select(Function(g) $"{g.Name} / VRAM {Formatters.FormatBytes(g.VramBytes)}")))
            Return AppText.Text(
                $"CPU: {cpu} / RAM: {Formatters.FormatBytes(hardware.TotalRamBytes)} (空き {Formatters.FormatBytes(hardware.AvailableRamBytes)}) / {gpuText}",
                $"CPU: {cpu} / RAM: {Formatters.FormatBytes(hardware.TotalRamBytes)} (free {Formatters.FormatBytes(hardware.AvailableRamBytes)}) / {gpuText}")
        End Function

        Private Shared Function FriendlyHardwareNote(note As String) As String
            Select Case note
                Case "RAM budget capped to current available memory."
                    Return AppText.Text("計算に使うメモリ上限は、現在の空きメモリに合わせています。", "The memory budget is capped to currently available RAM.")
                Case "Using simulated GPU input."
                    Return AppText.Text("手入力したGPUを使って計算しています。", "Using the manually entered GPU for calculation.")
                Case "CPU-only mode is enabled; GPU detection was skipped for ranking."
                    Return AppText.Text("CPUのみモードのため、推薦ではGPUを使わずに計算します。", "CPU-only mode is enabled; recommendations ignore GPU acceleration.")
                Case "Manual RAM budget applied."
                    Return AppText.Text("手入力したメモリ上限を使っています。", "Using the manually entered RAM budget.")
                Case "Invalid RAM budget input; conservative default applied."
                    Return AppText.Text("メモリ上限の入力を読み取れなかったため、安全寄りの値を使っています。", "Could not read the RAM budget input; using a conservative default.")
                Case Else
                    Return note
            End Select
        End Function

        Private Shared Function L(ja As String, en As String) As String
            Return AppText.Text(ja, en)
        End Function

        Private Shared Function ProfileForUseCase(useCase As String) As String
            Select Case If(useCase, "").Trim().ToLowerInvariant()
                Case "coding"
                    Return "coding"
                Case "reasoning"
                    Return "math"
                Case "multimodal"
                    Return "vision"
                Case "embedding", "any"
                    Return "any"
                Case Else
                    Return "general"
            End Select
        End Function

        Private Shared Function ParseGbOrBytes(value As String) As Long
            Dim text = value.Trim()
            Dim gb As Double
            If Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, gb) Then
                Return CLng(gb * 1024 * 1024 * 1024)
            End If
            Return InputParsers.ParseBytes(text)
        End Function

        Private Shared Function SplitInputs(value As String) As List(Of String)
            If String.IsNullOrWhiteSpace(value) Then Return New List(Of String)()
            Return value.Split({","c, ";"c, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(v) v.Trim()).
                Where(Function(v) v.Length > 0).
                ToList()
        End Function
    End Class
End Namespace
