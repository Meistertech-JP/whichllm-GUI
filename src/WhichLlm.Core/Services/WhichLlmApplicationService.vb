Option Strict On
Option Explicit On

Imports System.Text.RegularExpressions
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

        Public Async Function LoadModelSuggestionsAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of IReadOnlyList(Of ModelInfo))
            Dim models = Await _modelFetcher.LoadModelsAsync(options, cancellationToken)
            Dim suggestions As New List(Of ModelInfo)
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each model In models.
                Where(Function(m) m IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(m.RepoId)).
                OrderByDescending(Function(m) ModelSuggestionScore(m, options)).
                ThenByDescending(Function(m) m.Downloads).
                ThenBy(Function(m) m.RepoId)

                If seen.Add(model.RepoId) Then
                    suggestions.Add(model)
                End If
                If suggestions.Count >= 600 Then Exit For
            Next

            Return suggestions
        End Function

        Public Async Function PlanAsync(query As String, quant As String, contextLength As Integer, options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of PlanResult)
            Dim models = Await _modelFetcher.LoadModelsAsync(options, cancellationToken)
            Dim model = FindModel(models, query)
            If model Is Nothing Then Throw New InvalidOperationException("No model matched the plan query.")

            Dim hardware = Await _hardwareDetector.DetectAsync(options, cancellationToken)
            Dim targetQuants = PlanQuantsForModel(model, quant)
            Dim commonGpus = _gpuCatalog.CommonGpuNames().ToList()
            Dim result As New PlanResult With {.MatchedModel = model, .ContextLength = contextLength}

            For Each q In targetQuants
                Dim modelVariant = SelectPlanVariant(model, q)
                Dim required = _vram.EstimateRequiredBytes(model, modelVariant, contextLength)
                Dim row As New PlanRow With {
                    .Quantization = q,
                    .RequiredBytes = required,
                    .FitsCurrentHardware = _vram.ClassifyFit(required, hardware, options).IsRunnable
                }

                For Each recommendation In RecommendGpus(required, hardware, options, commonGpus)
                    row.RecommendedGpus.Add(recommendation)
                    row.FitsCommonGpus(recommendation.Name) = recommendation.FitType <> "unfit"
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

        Public Function AllGpuNames() As IReadOnlyList(Of String)
            Return _gpuCatalog.CommonGpuNames()
        End Function

        Private Shared Function ModelSuggestionScore(model As ModelInfo, options As RankingOptions) As Double
            Dim score = Math.Min(44.0R, Math.Log10(Math.Max(1, model.Downloads)) * 8.0R)
            score += Math.Min(18.0R, Math.Log10(Math.Max(1, model.Likes)) * 3.0R)

            If model.ParameterCountB <= 0 Then
                score -= 4.0R
            ElseIf model.ParameterCountB < 1.0R Then
                score -= 26.0R
            ElseIf model.ParameterCountB < 2.0R Then
                score -= 8.0R
            ElseIf model.ParameterCountB <= 14.0R Then
                score += 22.0R
            ElseIf model.ParameterCountB <= 32.0R Then
                score += 12.0R
            Else
                score -= 4.0R
            End If

            If IsInstructionTuned(model) Then score += 12.0R
            If IsTrustedModelSource(model.RepoId) Then score += 10.0R
            If model.Variants.Any(Function(v) String.Equals(v.RuntimeKind, "gguf", StringComparison.OrdinalIgnoreCase)) Then score += 6.0R

            Dim requestedUseCase = If(options?.UseCase, "")
            If Not String.IsNullOrWhiteSpace(requestedUseCase) AndAlso
                Not String.Equals(requestedUseCase, "any", StringComparison.OrdinalIgnoreCase) AndAlso
                String.Equals(model.UseCase, requestedUseCase, StringComparison.OrdinalIgnoreCase) Then
                score += 14.0R
            End If

            Return score
        End Function

        Private Shared Function FindModel(models As IEnumerable(Of ModelInfo), query As String) As ModelInfo
            If String.IsNullOrWhiteSpace(query) Then Return Nothing
            Return models.
                Select(Function(m) New With {.Model = m, .Score = ModelMatchScore(m, query)}).
                Where(Function(x) x.Score > 0).
                OrderByDescending(Function(x) x.Score).
                Select(Function(x) x.Model).
                FirstOrDefault()
        End Function

        Private Function RecommendGpus(requiredBytes As Long, baseHardware As HardwareInfo, options As RankingOptions, candidateNames As IEnumerable(Of String)) As List(Of GpuRecommendation)
            Dim recommendations As New List(Of GpuRecommendation)
            Dim fitOptions = CloneOptions(options)
            fitOptions.CpuOnly = False
            For Each gpuName In candidateNames.Distinct(StringComparer.OrdinalIgnoreCase)
                Dim gpu = _gpuCatalog.Resolve(gpuName)
                If Not IsUsefulPlanningGpu(gpu) Then Continue For

                gpu.UsableVramBytes = ApplyPlanningHeadroom(gpu.VramBytes, fitOptions.VramHeadroom)
                Dim simHardware = baseHardware.Clone()
                simHardware.Gpus = New List(Of GpuInfo) From {gpu}
                Dim fit = _vram.ClassifyFit(requiredBytes, simHardware, fitOptions)
                If Not fit.IsRunnable Then Continue For

                recommendations.Add(New GpuRecommendation With {
                    .Name = gpu.Name,
                    .Vendor = gpu.Vendor,
                    .FitType = fit.FitType,
                    .VramGb = gpu.VramBytes / 1024.0R / 1024.0R / 1024.0R,
                    .BandwidthGbps = gpu.MemoryBandwidthGbps,
                    .BalanceScore = GpuBalanceScore(requiredBytes, gpu, fit)
                })
            Next

            Return recommendations.
                OrderByDescending(Function(g) g.BalanceScore).
                ThenBy(Function(g) g.VramGb).
                Take(8).
                ToList()
        End Function

        Private Shared Function PlanQuantsForModel(model As ModelInfo, requestedQuant As String) As IReadOnlyList(Of String)
            If Not String.IsNullOrWhiteSpace(requestedQuant) Then
                Return New List(Of String) From {requestedQuant.Trim()}
            End If

            If IsQatModel(model) Then
                Return New List(Of String) From {"QAT"}
            End If

            Return QuantizationRules.PreferredQuants()
        End Function

        Private Shared Function SelectPlanVariant(model As ModelInfo, quant As String) As ModelVariant
            Dim match = model.Variants.
                Where(Function(v) VariantMatchesQuant(v, quant)).
                OrderBy(Function(v) If(v.IsSynthetic, 1, 0)).
                FirstOrDefault()

            If match IsNot Nothing Then Return match
            Return New ModelVariant With {.Quantization = quant, .RuntimeKind = "gguf", .IsSynthetic = True}
        End Function

        Private Shared Function VariantMatchesQuant(modelVariant As ModelVariant, quant As String) As Boolean
            If modelVariant Is Nothing Then Return False
            If String.Equals(modelVariant.Quantization, quant, StringComparison.OrdinalIgnoreCase) Then Return True
            If QuantizationRules.IsQat(quant) Then
                Return QuantizationRules.IsQat(modelVariant.Quantization) OrElse ContainsQatToken(modelVariant.FileName)
            End If
            Return False
        End Function

        Private Shared Function IsQatModel(model As ModelInfo) As Boolean
            If model Is Nothing Then Return False

            Dim fields As New List(Of String) From {
                model.RepoId,
                model.DisplayName,
                model.BaseModel,
                model.Architecture,
                model.PipelineTag
            }
            fields.AddRange(model.Tags)
            fields.AddRange(model.Variants.Select(Function(v) $"{v.Quantization} {v.FileName}"))

            Return fields.Any(AddressOf ContainsQatToken)
        End Function

        Private Shared Function ContainsQatToken(value As String) As Boolean
            If String.IsNullOrWhiteSpace(value) Then Return False
            Return Regex.IsMatch(value, "(^|[^a-z0-9])qat([^a-z0-9]|$)", RegexOptions.IgnoreCase)
        End Function

        Private Shared Function IsUsefulPlanningGpu(gpu As GpuInfo) As Boolean
            If gpu Is Nothing Then Return False
            If String.Equals(gpu.Vendor, "Apple", StringComparison.OrdinalIgnoreCase) Then Return False
            Dim name = gpu.Name.ToLowerInvariant()
            If name.Contains("h100", StringComparison.Ordinal) OrElse name.Contains("a100", StringComparison.Ordinal) Then Return False
            Return gpu.VramBytes > 0
        End Function

        Private Shared Function ApplyPlanningHeadroom(vramBytes As Long, headroom As String) As Long
            If vramBytes <= 0 Then Return 0
            If String.IsNullOrWhiteSpace(headroom) OrElse String.Equals(headroom, "auto", StringComparison.OrdinalIgnoreCase) Then
                Dim reserve = Math.Max(512L * 1024 * 1024, CLng(vramBytes * 0.06R))
                Return Math.Max(0, vramBytes - reserve)
            End If
            If String.Equals(headroom, "none", StringComparison.OrdinalIgnoreCase) Then Return vramBytes

            Try
                Dim reserve = InputParsers.ParseBytes(headroom, vramBytes)
                Return Math.Max(0, vramBytes - reserve)
            Catch
                Return vramBytes
            End Try
        End Function

        Private Shared Function GpuBalanceScore(requiredBytes As Long, gpu As GpuInfo, fit As FitDecision) As Double
            Dim usable = Math.Max(1.0R, CDbl(gpu.EffectiveVramBytes))
            Dim required = Math.Max(1.0R, CDbl(requiredBytes))
            Dim requiredGb = required / 1024.0R / 1024.0R / 1024.0R
            Dim ratio = usable / required
            Dim targetRatio = If(requiredGb < 2.0R, 3.0R, If(requiredGb < 8.0R, 2.0R, 1.45R))
            Dim fitBase = If(fit.FitType = "full_gpu", 70.0R, If(fit.FitType = "partial_offload", 42.0R, 20.0R))
            Dim bandwidth = If(gpu.MemoryBandwidthGbps, 160.0R)
            Dim bandwidthScore = Math.Min(18.0R, Math.Log(Math.Max(2.0R, bandwidth), 2) * 2.1R)
            Dim balancePenalty = Math.Min(60.0R, Math.Abs(Math.Log(Math.Max(0.1R, ratio / targetRatio))) * 22.0R)
            Dim sharedPenalty = If(gpu.IsSharedMemory, 12.0R, 0.0R)
            Dim headroomBonus = If(ratio >= 1.15R, 8.0R, 0.0R)
            Return Math.Round(fitBase + bandwidthScore + headroomBonus - balancePenalty - sharedPenalty, 2)
        End Function

        Private Shared Function ModelMatchScore(model As ModelInfo, query As String) As Double
            Dim trimmed = query.Trim()
            Dim repo = If(model.RepoId, "")
            Dim display = If(model.DisplayName, "")
            Dim matches = repo.Equals(trimmed, StringComparison.OrdinalIgnoreCase) OrElse
                display.Equals(trimmed, StringComparison.OrdinalIgnoreCase) OrElse
                repo.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) OrElse
                Formatters.ContainsAllTerms(repo, trimmed)
            If Not matches Then Return 0

            Dim score As Double = 0
            If repo.Equals(trimmed, StringComparison.OrdinalIgnoreCase) Then score += 1000
            If display.Equals(trimmed, StringComparison.OrdinalIgnoreCase) Then score += 850
            If repo.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) Then score += 420
            If Formatters.ContainsAllTerms(repo, trimmed) Then score += 180
            score += Math.Min(24.0R, Math.Log10(Math.Max(1, model.Downloads)) * 4.0R)
            score += Math.Min(10.0R, Math.Log10(Math.Max(1, model.Likes)) * 2.0R)
            score += ModelSizeScore(model, trimmed)
            score += If(IsInstructionTuned(model), 18.0R, 0.0R)
            score += If(IsTrustedModelSource(repo), 10.0R, 0.0R)
            Return score
        End Function

        Private Shared Function ModelSizeScore(model As ModelInfo, query As String) As Double
            Dim requestedSize = ParseRequestedSizeB(query)
            If requestedSize.HasValue Then
                Dim distance = Math.Abs(Math.Log(Math.Max(0.1R, model.ParameterCountB / requestedSize.Value), 2))
                Return Math.Max(-80.0R, 80.0R - distance * 55.0R)
            End If

            If model.ParameterCountB < 0.5R Then Return -80.0R
            If model.ParameterCountB < 1.0R Then Return -55.0R
            If model.ParameterCountB < 2.0R Then Return -20.0R
            If model.ParameterCountB <= 12.0R Then Return 28.0R
            If model.ParameterCountB <= 30.0R Then Return 14.0R
            Return -8.0R
        End Function

        Private Shared Function ParseRequestedSizeB(query As String) As Double?
            Dim match = Regex.Match(query, "(\d+(?:\.\d+)?)\s*([bm])", RegexOptions.IgnoreCase)
            If Not match.Success Then Return Nothing
            Dim value = Double.Parse(match.Groups(1).Value, System.Globalization.CultureInfo.InvariantCulture)
            If match.Groups(2).Value.Equals("m", StringComparison.OrdinalIgnoreCase) Then value /= 1000.0R
            Return value
        End Function

        Private Shared Function IsInstructionTuned(model As ModelInfo) As Boolean
            Dim text = (model.RepoId & " " & String.Join(" ", model.Tags)).ToLowerInvariant()
            Return text.Contains("instruct", StringComparison.Ordinal) OrElse text.Contains("-it", StringComparison.Ordinal) OrElse text.Contains("chat", StringComparison.Ordinal)
        End Function

        Private Shared Function IsTrustedModelSource(repoId As String) As Boolean
            Dim text = If(repoId, "").ToLowerInvariant()
            Return text.StartsWith("qwen/", StringComparison.Ordinal) OrElse
                text.StartsWith("google/", StringComparison.Ordinal) OrElse
                text.StartsWith("meta-llama/", StringComparison.Ordinal) OrElse
                text.StartsWith("mistralai/", StringComparison.Ordinal) OrElse
                text.StartsWith("microsoft/", StringComparison.Ordinal)
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
