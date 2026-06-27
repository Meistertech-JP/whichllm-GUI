Option Strict On
Option Explicit On

Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Windows
Imports System.Windows.Input
Imports WhichLlm.Core.Dto
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
        Private _selectedProfile As String = "general"
        Private _selectedUseCase As String = "general"
        Private _selectedEvidence As String = "base"
        Private _selectedFit As String = "any"
        Private _selectedSpeed As String = "any"

        Public Sub New()
            _service = WhichLlmApplicationService.CreateDefault()

            Profiles = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "一般", .Value = "general"},
                New ComboOption With {.Label = "コーディング", .Value = "coding"},
                New ComboOption With {.Label = "画像/マルチモーダル", .Value = "vision"},
                New ComboOption With {.Label = "数学", .Value = "math"},
                New ComboOption With {.Label = "すべて", .Value = "any"}
            })
            UseCases = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "日常", .Value = "general"},
                New ComboOption With {.Label = "会話", .Value = "chat"},
                New ComboOption With {.Label = "コーディング", .Value = "coding"},
                New ComboOption With {.Label = "推論・数学", .Value = "reasoning"},
                New ComboOption With {.Label = "画像対応", .Value = "multimodal"},
                New ComboOption With {.Label = "埋め込み/検索", .Value = "embedding"},
                New ComboOption With {.Label = "用途で絞らない", .Value = "any"}
            })
            EvidenceModes = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "base", .Value = "base"},
                New ComboOption With {.Label = "strict", .Value = "strict"},
                New ComboOption With {.Label = "any", .Value = "any"}
            })
            FitModes = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "any", .Value = "any"},
                New ComboOption With {.Label = "gpu", .Value = "gpu"},
                New ComboOption With {.Label = "full-gpu", .Value = "full-gpu"}
            })
            SpeedModes = New ObservableCollection(Of ComboOption)(New List(Of ComboOption) From {
                New ComboOption With {.Label = "any", .Value = "any"},
                New ComboOption With {.Label = "usable", .Value = "usable"},
                New ComboOption With {.Label = "fast", .Value = "fast"}
            })

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
        Public Property QuantText As String = ""
        Public Property MinSpeedText As String = ""
        Public Property MinParamsText As String = ""
        Public Property CpuOnly As Boolean
        Public Property Refresh As Boolean
        Public Property SimulatedGpuText As String = ""
        Public Property VramOverrideText As String = ""
        Public Property BandwidthOverrideText As String = ""
        Public Property VramHeadroomText As String = "auto"
        Public Property RamBudgetText As String = "available"
        Public Property PlanQuery As String = "llama 3 8b"
        Public Property PlanQuant As String = ""
        Public Property PlanContextText As String = "4096"
        Public Property UpgradeTargetsText As String = "RTX 4090, RTX 5090"
        Public Property SnippetQuery As String = "qwen 7b"
        Public Property SnippetQuant As String = "Q4_K_M"

        Public ReadOnly Property Profiles As ObservableCollection(Of ComboOption)
        Public ReadOnly Property UseCases As ObservableCollection(Of ComboOption)
        Public ReadOnly Property EvidenceModes As ObservableCollection(Of ComboOption)
        Public ReadOnly Property FitModes As ObservableCollection(Of ComboOption)
        Public ReadOnly Property SpeedModes As ObservableCollection(Of ComboOption)
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

        Public Property SelectedProfile As String
            Get
                Return _selectedProfile
            End Get
            Set(value As String)
                SetProperty(_selectedProfile, value)
            End Set
        End Property

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
                If SelectedResult Is Nothing Then Return "結果を選択すると詳細が表示されます。"
                Return SelectedResult.Details
            End Get
        End Property

        Public Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Set(value As Boolean)
                SetProperty(_isBusy, value)
            End Set
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

        Public Async Function InitializeAsync() As Task
            Await DetectHardwareAsync()
        End Function

        Private Async Function RunRecommendationAsync() As Task
            Await RunWithBusyAsync("推薦を計算しています...", Async Function()
                Dim options = BuildOptions()
                Dim result = Await _service.RankAsync(options)
                _lastRanking = result
                RecommendationRows.Clear()
                For Each row In result.Models
                    RecommendationRows.Add(New RankedModelRow(row))
                Next
                SelectedResult = RecommendationRows.FirstOrDefault()
                StatusMessage = $"推薦完了: {RecommendationRows.Count}件"
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
                HardwareSummary = $"CPU: {hardware.CpuName}{Environment.NewLine}Cores: {hardware.PhysicalCores}, AVX2: {hardware.SupportsAvx2}, AVX-512: {hardware.SupportsAvx512}{Environment.NewLine}RAM: {Formatters.FormatBytes(hardware.TotalRamBytes)} total / {Formatters.FormatBytes(hardware.AvailableRamBytes)} available{Environment.NewLine}Disk free: {Formatters.FormatBytes(hardware.FreeDiskBytes)}{Environment.NewLine}{String.Join(Environment.NewLine, hardware.DetectionNotes.Concat(hardware.BudgetNotes))}"
                StatusMessage = "ハードウェア検出完了"
            End Function)
        End Function

        Private Async Function RunPlanAsync() As Task
            Await RunWithBusyAsync("導入計画を作成しています...", Async Function()
                Dim options = BuildOptions()
                Dim contextLength = InputParsers.ParseContextLength(PlanContextText, options.ContextLength)
                Dim result = Await _service.PlanAsync(PlanQuery, PlanQuant, contextLength, options)
                PlanRows.Clear()
                For Each row In result.Rows
                    PlanRows.Add(New PlanDisplayRow(row))
                Next
                PlanSummary = $"Matched: {result.MatchedModel.RepoId} / context {result.ContextLength:N0}"
                StatusMessage = "導入計画を作成しました"
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
                UpgradeSummary = $"Current top: {result.CurrentTopModel} ({result.CurrentScore:0.0})"
                StatusMessage = "GPU比較を作成しました"
            End Function)
        End Function

        Private Async Function GenerateSnippetAsync() As Task
            Await RunWithBusyAsync("スニペットを生成しています...", Async Function()
                Dim result = Await _service.SnippetAsync(SnippetQuery, SnippetQuant, BuildOptions())
                SnippetCommandLine = result.CommandLine
                SnippetCode = result.Code
                StatusMessage = $"スニペット生成完了: {result.Model.RepoId}"
            End Function)
        End Function

        Private Sub CopyMarkdown()
            If _lastRanking Is Nothing Then Return
            Clipboard.SetText(_export.RankingToMarkdown(_lastRanking))
            StatusMessage = "Markdown表をコピーしました"
        End Sub

        Private Sub CopyJson()
            If _lastRanking Is Nothing Then Return
            Clipboard.SetText(_export.RankingToJson(_lastRanking))
            StatusMessage = "JSONをコピーしました"
        End Sub

        Private Sub CopySnippet()
            Clipboard.SetText(SnippetCommandLine & Environment.NewLine & Environment.NewLine & SnippetCode)
            StatusMessage = "スニペットをコピーしました"
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
                .Profile = SelectedProfile,
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

        Private Shared Function ParseInteger(value As String, fallback As Integer) As Integer
            Dim parsed As Integer
            If Integer.TryParse(value, parsed) AndAlso parsed > 0 Then Return parsed
            Return fallback
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
            Return value.Split({ControlChars.Cr, ControlChars.Lf, ";"c}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(v) v.Trim()).
                Where(Function(v) v.Length > 0).
                ToList()
        End Function
    End Class
End Namespace
