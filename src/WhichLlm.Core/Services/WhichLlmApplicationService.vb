Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Engine
Imports WhichLlm.Core.Utilities

Namespace Services
    Public Class WhichLlmApplicationService
        Private ReadOnly _hardwareDetector As IHardwareDetector
        Private ReadOnly _modelFetcher As IModelFetcher
        Private ReadOnly _benchmarkProvider As IBenchmarkProvider
        Private ReadOnly _ranker As IRanker
        Private ReadOnly _vram As IVramEstimator
        Private ReadOnly _gpuCatalog As IGpuCatalog
        Private ReadOnly _snippetGenerator As ISnippetGenerator

        Public Sub New(hardwareDetector As IHardwareDetector, modelFetcher As IModelFetcher, benchmarkProvider As IBenchmarkProvider, ranker As IRanker, vram As IVramEstimator, gpuCatalog As IGpuCatalog, snippetGenerator As ISnippetGenerator)
            _hardwareDetector = hardwareDetector
            _modelFetcher = modelFetcher
            _benchmarkProvider = benchmarkProvider
            _ranker = ranker
            _vram = vram
            _gpuCatalog = gpuCatalog
            _snippetGenerator = snippetGenerator
        End Sub

        Public Shared Function CreateDefault() As WhichLlmApplicationService
            Dim gpuCatalog As IGpuCatalog = New GpuCatalog()
            Dim hardware As IHardwareDetector = New WindowsHardwareDetector(gpuCatalog)
            Dim modelCache As IModelCache = New ModelCache()
            Dim benchmarkCache As IBenchmarkCache = New BenchmarkCache()
            Dim hf As IHuggingFaceClient = New HuggingFaceClient()
            Dim fetcher As IModelFetcher = New ModelFetcher(modelCache, hf)
            Dim benchmark As IBenchmarkProvider = New BenchmarkProvider(benchmarkCache)
            Dim grouper As IModelGrouper = New ModelGrouper()
            Dim vram As IVramEstimator = New VramEstimator()
            Dim speed As IPerformanceEstimator = New PerformanceEstimator()
            Dim ranker As IRanker = New Ranker(vram, speed, grouper)
            Dim snippet As ISnippetGenerator = New SnippetGenerator()
            Return New WhichLlmApplicationService(hardware, fetcher, benchmark, ranker, vram, gpuCatalog, snippet)
        End Function

        Public Async Function RankAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of RankingResult)
            Dim hardware = Await _hardwareDetector.DetectAsync(options, cancellationToken)
            Dim models = Await _modelFetcher.LoadModelsAsync(options, cancellationToken)
            Dim benchmarks = Await _benchmarkProvider.LoadBenchmarksAsync(options.Refresh, cancellationToken)
            Return Await _ranker.RankAsync(models, hardware, benchmarks, options, cancellationToken)
        End Function

        Public Function DetectHardwareAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of HardwareInfo)
            Return _hardwareDetector.DetectAsync(options, cancellationToken)
        End Function

        Public Async Function PlanAsync(query As String, quant As String, contextLength As Integer, options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of PlanResult)
            Dim models = Await _modelFetcher.LoadModelsAsync(options, cancellationToken)
            Dim model = FindModel(models, query)
            If model Is Nothing Then Throw New InvalidOperationException("No model matched the plan query.")

            Dim hardware = Await _hardwareDetector.DetectAsync(options, cancellationToken)
            Dim targetQuants = If(String.IsNullOrWhiteSpace(quant), QuantizationRules.PreferredQuants(), New List(Of String) From {quant})
            Dim commonGpus = _gpuCatalog.CommonGpuNames().Take(8).ToList()
            Dim result As New PlanResult With {.MatchedModel = model, .ContextLength = contextLength}

            For Each q In targetQuants
                Dim modelVariant As New ModelVariant With {.Quantization = q, .RuntimeKind = "gguf", .IsSynthetic = True}
                Dim required = _vram.EstimateRequiredBytes(model, modelVariant, contextLength)
                Dim row As New PlanRow With {
                    .Quantization = q,
                    .RequiredBytes = required,
                    .FitsCurrentHardware = _vram.ClassifyFit(required, hardware, options).IsRunnable
                }

                For Each gpuName In commonGpus
                    Dim simOptions = CloneOptions(options)
                    simOptions.SimulatedGpuInputs.Clear()
                    simOptions.SimulatedGpuInputs.Add(gpuName)
                    Dim simHardware = Await _hardwareDetector.DetectAsync(simOptions, cancellationToken)
                    row.FitsCommonGpus(gpuName) = _vram.ClassifyFit(required, simHardware, simOptions).IsRunnable
                Next

                result.Rows.Add(row)
            Next

            Return result
        End Function

        Public Async Function UpgradeAsync(targetGpus As IEnumerable(Of String), options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of UpgradeResult)
            Dim baseOptions = CloneOptions(options)
            baseOptions.Top = Math.Max(1, options.Top)
            Dim current = Await RankAsync(baseOptions, cancellationToken)
            Dim currentTop = current.Models.FirstOrDefault()
            Dim result As New UpgradeResult With {
                .CurrentTopModel = If(currentTop Is Nothing, "", currentTop.Model.RepoId),
                .CurrentScore = If(currentTop Is Nothing, 0, currentTop.Score)
            }

            Dim models = Await _modelFetcher.LoadModelsAsync(options, cancellationToken)
            Dim benchmarks = Await _benchmarkProvider.LoadBenchmarksAsync(options.Refresh, cancellationToken)
            For Each gpuInput In targetGpus.Where(Function(g) Not String.IsNullOrWhiteSpace(g))
                Dim simOptions = CloneOptions(options)
                simOptions.SimulatedGpuInputs.Clear()
                simOptions.SimulatedGpuInputs.Add(gpuInput)
                Dim simHardware = Await _hardwareDetector.DetectAsync(simOptions, cancellationToken)
                Dim ranked = Await _ranker.RankAsync(models, simHardware, benchmarks, simOptions, cancellationToken)
                Dim top = ranked.Models.FirstOrDefault()
                result.Rows.Add(New UpgradeRow With {
                    .TargetGpu = gpuInput,
                    .TopModel = If(top Is Nothing, "", top.Model.RepoId),
                    .TopScore = If(top Is Nothing, 0, top.Score),
                    .FitType = If(top Is Nothing, "", top.FitType),
                    .EstimatedTokPerSec = If(top Is Nothing, 0, top.EstimatedTokPerSec),
                    .GainVsCurrent = If(top Is Nothing, 0, top.Score - result.CurrentScore)
                })
            Next

            Return result
        End Function

        Public Async Function SnippetAsync(query As String, quant As String, options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of SnippetResult)
            Dim models = Await _modelFetcher.LoadModelsAsync(options, cancellationToken)
            Dim model = FindModel(models, query)
            If model Is Nothing Then
                model = models.Where(Function(m) m.Variants.Any(Function(v) v.RuntimeKind = "gguf")).OrderByDescending(Function(m) m.Downloads).FirstOrDefault()
            End If
            If model Is Nothing Then Throw New InvalidOperationException("No model is available for snippet generation.")
            Return _snippetGenerator.Generate(model, quant)
        End Function

        Public Function SearchGpus(term As String) As IReadOnlyList(Of GpuCatalogEntry)
            Return _gpuCatalog.Search(term)
        End Function

        Private Shared Function FindModel(models As IEnumerable(Of ModelInfo), query As String) As ModelInfo
            If String.IsNullOrWhiteSpace(query) Then Return Nothing
            Return models.
                OrderByDescending(Function(m) If(m.RepoId.Equals(query, StringComparison.OrdinalIgnoreCase), 100, 0) + If(m.RepoId.EndsWith(query, StringComparison.OrdinalIgnoreCase), 50, 0) + If(Formatters.ContainsAllTerms(m.RepoId, query), 10, 0) + Math.Log10(Math.Max(1, m.Downloads))).
                FirstOrDefault(Function(m) m.RepoId.Equals(query, StringComparison.OrdinalIgnoreCase) OrElse m.RepoId.EndsWith(query, StringComparison.OrdinalIgnoreCase) OrElse Formatters.ContainsAllTerms(m.RepoId, query))
        End Function

        Private Shared Function CloneOptions(options As RankingOptions) As RankingOptions
            Return New RankingOptions With {
                .Top = options.Top,
                .ContextLength = options.ContextLength,
                .Quant = options.Quant,
                .MinSpeedTokPerSec = options.MinSpeedTokPerSec,
                .Speed = options.Speed,
                .Fit = options.Fit,
                .CpuOnly = options.CpuOnly,
                .Profile = options.Profile,
                .UseCase = options.UseCase,
                .Evidence = options.Evidence,
                .MinParamsB = options.MinParamsB,
                .Refresh = options.Refresh,
                .Details = options.Details,
                .SimulatedGpuInputs = New List(Of String)(options.SimulatedGpuInputs),
                .OverrideVramBytes = options.OverrideVramBytes,
                .OverrideBandwidthGbps = options.OverrideBandwidthGbps,
                .OverrideRamBandwidthGbps = options.OverrideRamBandwidthGbps,
                .GpuIndex = options.GpuIndex,
                .VramHeadroom = options.VramHeadroom,
                .RamBudget = options.RamBudget
            }
        End Function
    End Class
End Namespace
