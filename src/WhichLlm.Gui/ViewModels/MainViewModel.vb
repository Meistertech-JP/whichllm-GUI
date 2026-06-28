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
        Private _isBusy As Boolean
        Private _statusMessage As String = "準備完了"
        Private _selectedResult As RankedModelRow
        Private _selectedUseCase As String = "general"
        Private _selectedEvidence As String = "base"
        Private _selectedFit As String = "any"
        Private _selectedSpeed As String = "any"
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

        Public Sub New()
            _service = WhichLlmApplicationService.CreateDefault()

            UseCases = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "日常の文章と調べ物", .Value = "general"},
                New ComboOption With {.Label = "会話", .Value = "chat"},
                New ComboOption With {.Label = "プログラミング", .Value = "coding"},
                New ComboOption With {.Label = "論理・数学", .Value = "reasoning"},
                New ComboOption With {.Label = "画像も使う", .Value = "multimodal"},
                New ComboOption With {.Label = "検索・分類", .Value = "embedding"},
                New ComboOption With {.Label = "すべて見る", .Value = "any"}
            })
            EvidenceModes = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "直接・近いモデルの根拠", .Value = "base"},
                New ComboOption With {.Label = "直接ベンチマークのみ", .Value = "strict"},
                New ComboOption With {.Label = "根拠なしも含める", .Value = "any"}
            })
            FitModes = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "このPCで動けばOK", .Value = "any"},
                New ComboOption With {.Label = "GPUを使うものだけ", .Value = "gpu"},
                New ComboOption With {.Label = "GPUメモリ内に収まるものだけ", .Value = "full-gpu"}
            })
            SpeedModes = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "速度で絞り込まない", .Value = "any"},
                New ComboOption With {.Label = "普段使い向け (10 tok/s以上)", .Value = "usable"},
                New ComboOption With {.Label = "高速応答 (30 tok/s以上)", .Value = "fast"}
            })

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
                If SetProperty(_simulatedGpuText, If(value, "")) Then RefreshOptions(GpuOptions, _allGpuOptions, _simulatedGpuText)
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
                If SetProperty(_upgradeTargetsText, If(value, "")) Then RefreshOptions(UpgradeGpuOptions, _allGpuOptions, _upgradeTargetsText)
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
                SetProperty(_selectedUseCase, value)
            End Set
        End Property

        Public Property SelectedEvidence As String
            Get
                Return _selectedEvidence
            End Get
            Set(value As String)
                SetProperty(_selectedEvidence, value)
            End Set
        End Property

        Public Property SelectedFit As String
            Get
                Return _selectedFit
            End Get
            Set(value As String)
                SetProperty(_selectedFit, value)
            End Set
        End Property

        Public Property SelectedSpeed As String
            Get
                Return _selectedSpeed
            End Get
            Set(value As String)
                SetProperty(_selectedSpeed, value)
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
                If SelectedResult Is Nothing Then Return "「おすすめを探す」を押すと、このPCで使えるモデルの詳細がここに出ます。"
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
                Return If(IsBusy, "処理中", "待機中")
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

        Private _recommendationHardwareSummary As String = "このPC: 検出中..."
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

        Public Async Function InitializeAsync() As Task
            Await DetectHardwareAsync()
            Await RefreshModelSuggestionsAsync()
            ' 起動直後に一度おすすめを出して、空の画面ではなく「答え」を見せる。
            Await RunRecommendationAsync()
        End Function

        Private Async Function RunRecommendationAsync() As Task
            Await RunWithBusyAsync("推薦を計算しています...", Async Function()
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
                    $"おすすめが出ました（{RecommendationRows.Count}件）",
                    "条件に合うモデルが見つかりませんでした。詳細条件をゆるめてお試しください。")
                CopyMarkdownCommand.RaiseCanExecuteChanged()
                CopyJsonCommand.RaiseCanExecuteChanged()
            End Function)
        End Function

        Private Async Function DetectHardwareAsync() As Task
            Await RunWithBusyAsync("ハードウェアを検出しています...", Async Function()
                Dim hardware = Await _service.DetectHardwareAsync(BuildOptions())
                HardwareRows.Clear()
                For Each gpu In hardware.Gpus
                    HardwareRows.Add(New HardwareGpuRow(gpu))
                Next
                HardwareSummary = BuildHardwareSummary(hardware)
                RecommendationHardwareSummary = BuildHardwareInline(hardware)
                StatusMessage = "ハードウェア検出完了"
            End Function)
        End Function

        Private Async Function RunPlanAsync() As Task
            Await RunWithBusyAsync("プランを作成しています...", Async Function()
                Await RefreshModelSuggestionsAsync()
                Dim options = BuildOptions()
                Dim contextLength = InputParsers.ParseContextLength(PlanContextText, options.ContextLength)
                Dim result = Await _service.PlanAsync(PlanQuery, PlanQuant, contextLength, options)
                RememberModels(New List(Of ModelInfo) From {result.MatchedModel})
                PlanRows.Clear()
                For Each row In result.Rows
                    PlanRows.Add(New PlanDisplayRow(row))
                Next
                PlanSummary = $"候補: {result.MatchedModel.RepoId} / 文脈長 {result.ContextLength:N0}"
                StatusMessage = "プランを作成しました"
            End Function)
        End Function

        Private Async Function RunUpgradeAsync() As Task
            Await RunWithBusyAsync("GPU比較を計算しています...", Async Function()
                Dim targets = SplitInputs(UpgradeTargetsText)
                Dim result = Await _service.UpgradeAsync(targets, BuildOptions())
                UpgradeRows.Clear()
                For Each row In result.Rows
                    UpgradeRows.Add(New UpgradeDisplayRow(row))
                Next
                UpgradeSummary = $"現在のPCの最有力: {result.CurrentTopModel} ({result.CurrentScore:0.0})"
                StatusMessage = "GPU比較を作成しました"
            End Function)
        End Function

        Private Async Function GenerateSnippetAsync() As Task
            Await RunWithBusyAsync("スニペットを生成しています...", Async Function()
                Await RefreshModelSuggestionsAsync()
                Dim result = Await _service.SnippetAsync(SnippetQuery, SnippetQuant, BuildOptions())
                RememberModels(New List(Of ModelInfo) From {result.Model})
                SnippetCommandLine = result.CommandLine
                SnippetCode = result.Code
                StatusMessage = $"スニペット生成完了: {result.Model.RepoId}"
            End Function)
        End Function

        Private Sub CopyMarkdown()
            If _lastRanking Is Nothing Then Return
            CopyTextToClipboard(_export.RankingToMarkdown(_lastRanking), "Markdown表をコピーしました")
        End Sub

        Private Sub CopyJson()
            If _lastRanking Is Nothing Then Return
            CopyTextToClipboard(_export.RankingToJson(_lastRanking), "JSONをコピーしました")
        End Sub

        Private Sub CopySnippet()
            CopyTextToClipboard(SnippetCommandLine & Environment.NewLine & Environment.NewLine & SnippetCode, "スニペットをコピーしました")
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
                    StatusMessage = "クリップボードにコピーできませんでした。ほかのアプリが使用中の可能性があります。"
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
                StatusMessage = "エラー: " & ex.Message
            Finally
                IsBusy = False
            End Try
        End Function

        Private Function BuildOptions() As RankingOptions
            Dim options As New RankingOptions With {
                .Top = ParseInteger(TopText, 10),
                .ContextLength = InputParsers.ParseContextLength(ContextText, 4096),
                .Quant = If(QuantText, "").Trim(),
                .Speed = SelectedSpeed,
                .Fit = SelectedFit,
                .CpuOnly = CpuOnly,
                .Profile = ProfileForUseCase(SelectedUseCase),
                .UseCase = SelectedUseCase,
                .Evidence = SelectedEvidence,
                .Refresh = Refresh,
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
                    StatusMessage = $"モデル情報を準備しました（{_knownModelOptions.Count}件）"
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
            RefreshOptions(GpuOptions, _allGpuOptions, SimulatedGpuText)
            RefreshOptions(UpgradeGpuOptions, _allGpuOptions, UpgradeTargetsText)
            RefreshOptions(PlanModelOptions, _knownModelOptions, PlanQuery, ModelSuggestionLimit)
            RefreshOptions(SnippetModelOptions, _knownModelOptions, SnippetQuery, ModelSuggestionLimit)
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
                $"物理コア: {hardware.PhysicalCores} / AVX2: {hardware.SupportsAvx2} / AVX-512: {hardware.SupportsAvx512}",
                $"メモリ: 合計 {Formatters.FormatBytes(hardware.TotalRamBytes)} / 空き {Formatters.FormatBytes(hardware.AvailableRamBytes)}",
                $"ディスク空き: {Formatters.FormatBytes(hardware.FreeDiskBytes)}"
            }
            lines.AddRange(hardware.DetectionNotes.Concat(hardware.BudgetNotes).Select(AddressOf FriendlyHardwareNote))
            Return String.Join(Environment.NewLine, lines.Where(Function(line) Not String.IsNullOrWhiteSpace(line)))
        End Function

        Private Shared Function BuildHardwareInline(hardware As HardwareInfo) As String
            Dim cpu = If(String.IsNullOrWhiteSpace(hardware.CpuName), "未検出", hardware.CpuName)
            Dim gpuText = If(hardware.Gpus.Count = 0,
                "GPU: 未検出",
                "GPU: " & String.Join(", ", hardware.Gpus.Select(Function(g) $"{g.Name} / VRAM {Formatters.FormatBytes(g.VramBytes)}")))
            Return $"CPU: {cpu} / RAM: {Formatters.FormatBytes(hardware.TotalRamBytes)} (空き {Formatters.FormatBytes(hardware.AvailableRamBytes)}) / {gpuText}"
        End Function

        Private Shared Function FriendlyHardwareNote(note As String) As String
            Select Case note
                Case "RAM budget capped to current available memory."
                    Return "計算に使うメモリ上限は、現在の空きメモリに合わせています。"
                Case "Using simulated GPU input."
                    Return "手入力したGPUを使って計算しています。"
                Case "CPU-only mode is enabled; GPU detection was skipped for ranking."
                    Return "CPUのみモードのため、推薦ではGPUを使わずに計算します。"
                Case "Manual RAM budget applied."
                    Return "手入力したメモリ上限を使っています。"
                Case "Invalid RAM budget input; conservative default applied."
                    Return "メモリ上限の入力を読み取れなかったため、安全寄りの値を使っています。"
                Case Else
                    Return note
            End Select
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
