Option Strict On
Option Explicit On

Imports System.Text.RegularExpressions
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Services
Imports WhichLlm.Core.Utilities

Namespace Engine
    Public Class Ranker
        Implements IRanker

        Private ReadOnly _vram As IVramEstimator
        Private ReadOnly _speed As IPerformanceEstimator
        Private ReadOnly _grouper As IModelGrouper

        Public Sub New(vram As IVramEstimator, speed As IPerformanceEstimator, grouper As IModelGrouper)
            _vram = vram
            _speed = speed
            _grouper = grouper
        End Sub

        Public Function RankAsync(models As IEnumerable(Of ModelInfo), hardware As HardwareInfo, benchmarks As Dictionary(Of String, BenchmarkEvidence), options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of RankingResult) Implements IRanker.RankAsync
            Dim rows As New List(Of RankedModel)

            For Each model In models
                cancellationToken.ThrowIfCancellationRequested()
                If Not PassesProfile(model, options.Profile) Then Continue For
                If Not PassesUseCase(model, options.UseCase) Then Continue For
                If options.MinParamsB.HasValue AndAlso model.ParameterCountB < options.MinParamsB.Value Then Continue For

                For Each modelVariant In BuildVariants(model, options)
                    Dim required = _vram.EstimateRequiredBytes(model, modelVariant, options.ContextLength)
                    Dim fit = _vram.ClassifyFit(required, hardware, options)
                    If Not fit.IsRunnable Then Continue For
                    If Not PassesFit(fit.FitType, options) Then Continue For

                    Dim speedEstimate = _speed.Estimate(model, modelVariant, fit, hardware)
                    If Not PassesSpeed(speedEstimate.TokPerSec, options) Then Continue For

                    Dim evidence = ResolveBenchmark(model, benchmarks, options.Evidence)
                    Dim score = ComputeScore(model, modelVariant, evidence, fit, speedEstimate, options.UseCase)
                    rows.Add(New RankedModel With {
                        .Model = model,
                        .SelectedVariant = modelVariant,
                        .Score = score,
                        .FitType = fit.FitType,
                        .VramRequiredBytes = fit.VramRequiredBytes,
                        .VramAvailableBytes = fit.VramAvailableBytes,
                        .UsesMultiGpu = fit.UsesMultiGpu,
                        .MultiGpuEffectiveVramBytes = fit.MultiGpuEffectiveVramBytes,
                        .EstimatedTokPerSec = speedEstimate.TokPerSec,
                        .SpeedConfidence = speedEstimate.Confidence,
                        .SpeedRangeLowTokPerSec = speedEstimate.RangeLow,
                        .SpeedRangeHighTokPerSec = speedEstimate.RangeHigh,
                        .SpeedNotes = speedEstimate.Notes,
                        .Benchmark = evidence,
                        .MemoryNotes = fit.Notes,
                        .UseCase = If(String.IsNullOrWhiteSpace(model.UseCase), "general", model.UseCase)
                    })
                Next
            Next

            If rows.Any(Function(r) r.EstimatedTokPerSec >= 5.0R) Then
                rows = rows.Where(Function(r) r.EstimatedTokPerSec >= 1.5R).ToList()
            End If

            Dim bestByFamily = rows.
                GroupBy(Function(r) _grouper.FamilyKey(r.Model)).
                Select(Function(group) group.OrderByDescending(Function(r) FamilySelectionScore(r)).First()).
                OrderByDescending(Function(r) SortScore(r)).
                Take(Math.Max(1, options.Top)).
                ToList()

            For index = 0 To bestByFamily.Count - 1
                bestByFamily(index).Rank = index + 1
            Next

            Dim result As New RankingResult With {.Hardware = hardware, .Models = bestByFamily}
            If bestByFamily.Count = 0 Then result.Warnings.Add("No runnable models matched the selected filters.")
            Return Task.FromResult(result)
        End Function

        Private Shared Function BuildVariants(model As ModelInfo, options As RankingOptions) As IEnumerable(Of ModelVariant)
            Dim hasRealVariants = model.Variants.Any(Function(v) Not v.IsSynthetic)
            Dim variants = If(model.Variants.Count > 0, model.Variants, SyntheticVariantsFor(model))
            If Not String.IsNullOrWhiteSpace(options.Quant) Then
                Dim requestedQuant = options.Quant.Trim()
                Dim matching = variants.
                    Where(Function(v) VariantMatchesRequestedQuant(v, requestedQuant)).
                    ToList()

                If matching.Count > 0 Then Return matching
                If Not hasRealVariants AndAlso CanSynthesizeRequestedQuant(requestedQuant) Then
                    Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = requestedQuant, .RuntimeKind = "gguf", .IsSynthetic = True}}
                End If

                Return New List(Of ModelVariant)()
            End If

            Return variants.
                Where(Function(v) Not IsExtremeLowBit(v.Quantization)).
                OrderBy(Function(v) QuantPreferenceIndex(v.Quantization)).
                ToList()
        End Function

        Private Shared Function SyntheticVariantsFor(model As ModelInfo) As List(Of ModelVariant)
            Dim id = model.RepoId.ToLowerInvariant()
            If ContainsQatToken(id) Then Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = "QAT", .RuntimeKind = "gguf", .IsSynthetic = True}}
            If id.Contains("awq", StringComparison.Ordinal) Then Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = "AWQ", .RuntimeKind = "transformers", .IsSynthetic = True}}
            If id.Contains("gptq", StringComparison.Ordinal) Then Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = "GPTQ", .RuntimeKind = "transformers", .IsSynthetic = True}}
            If id.Contains("fp8", StringComparison.Ordinal) Then Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = "FP8", .RuntimeKind = "transformers", .IsSynthetic = True}}
            If id.Contains("bf16", StringComparison.Ordinal) Then Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = "BF16", .RuntimeKind = "transformers", .IsSynthetic = True}}
            If id.Contains("fp16", StringComparison.Ordinal) Then Return New List(Of ModelVariant) From {New ModelVariant With {.Quantization = "FP16", .RuntimeKind = "transformers", .IsSynthetic = True}}
            Return QuantizationRules.PreferredQuants().Select(Function(q) New ModelVariant With {.Quantization = q, .RuntimeKind = "gguf", .IsSynthetic = True}).ToList()
        End Function

        Private Shared Function VariantMatchesRequestedQuant(modelVariant As ModelVariant, requestedQuant As String) As Boolean
            If modelVariant Is Nothing Then Return False
            If modelVariant.Quantization.Equals(requestedQuant, StringComparison.OrdinalIgnoreCase) Then Return True
            If QuantizationRules.IsQat(requestedQuant) Then
                Return QuantizationRules.IsQat(modelVariant.Quantization) OrElse ContainsQatToken(modelVariant.FileName)
            End If
            Return False
        End Function

        Private Shared Function CanSynthesizeRequestedQuant(requestedQuant As String) As Boolean
            If String.IsNullOrWhiteSpace(requestedQuant) Then Return False
            If QuantizationRules.IsQat(requestedQuant) Then Return False

            Dim text = requestedQuant.Trim().ToUpperInvariant()
            If text.Contains("AWQ", StringComparison.Ordinal) OrElse
                text.Contains("GPTQ", StringComparison.Ordinal) OrElse
                text.Contains("FP8", StringComparison.Ordinal) Then
                Return False
            End If

            Return Regex.IsMatch(text, "^(IQ[0-9]|Q[0-9]|F16|BF16|FP16)")
        End Function

        Private Shared Function ContainsQatToken(value As String) As Boolean
            If String.IsNullOrWhiteSpace(value) Then Return False
            Return Regex.IsMatch(value, "(^|[^a-z0-9])qat([^a-z0-9]|$)", RegexOptions.IgnoreCase)
        End Function

        Private Shared Function IsExtremeLowBit(quant As String) As Boolean
            Return quant.StartsWith("Q1", StringComparison.OrdinalIgnoreCase) OrElse quant.StartsWith("Q2", StringComparison.OrdinalIgnoreCase) OrElse quant.StartsWith("IQ2", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function QuantPreferenceIndex(quant As String) As Integer
            Dim preferred = QuantizationRules.PreferredQuants().ToList()
            Dim index = preferred.FindIndex(Function(q) q.Equals(quant, StringComparison.OrdinalIgnoreCase))
            If index >= 0 Then Return index
            Return 100
        End Function

        Private Shared Function PassesProfile(model As ModelInfo, profile As String) As Boolean
            Dim name = model.RepoId.ToLowerInvariant()
            Select Case profile.ToLowerInvariant()
                Case "coding"
                    Return name.Contains("code", StringComparison.Ordinal) OrElse name.Contains("coder", StringComparison.Ordinal) OrElse name.Contains("deepseek", StringComparison.Ordinal) OrElse Not IsSpecialized(name)
                Case "vision"
                    Return name.Contains("vision", StringComparison.Ordinal) OrElse name.Contains("vl", StringComparison.Ordinal) OrElse name.Contains("llava", StringComparison.Ordinal) OrElse model.PipelineTag.Contains("image", StringComparison.OrdinalIgnoreCase)
                Case "math"
                    Return name.Contains("math", StringComparison.Ordinal) OrElse name.Contains("r1", StringComparison.Ordinal) OrElse name.Contains("reason", StringComparison.Ordinal)
                Case "any"
                    Return True
                Case Else
                    Return Not IsSpecialized(name)
            End Select
        End Function

        Private Shared Function IsSpecialized(name As String) As Boolean
            Return name.Contains("coder", StringComparison.Ordinal) OrElse name.Contains("code-", StringComparison.Ordinal) OrElse name.Contains("vision", StringComparison.Ordinal) OrElse name.Contains("vl-", StringComparison.Ordinal) OrElse name.Contains("math", StringComparison.Ordinal)
        End Function

        Private Shared Function PassesUseCase(model As ModelInfo, requestedUseCase As String) As Boolean
            Dim requested = NormalizeUseCase(requestedUseCase)
            If requested = "any" Then Return True
            Dim actual = NormalizeUseCase(model.UseCase)
            Select Case requested
                Case "general"
                    Return actual = "general" OrElse actual = "chat"
                Case "chat"
                    Return actual = "chat" OrElse actual = "general"
                Case "coding"
                    Return actual = "coding" OrElse actual = "general"
                Case "reasoning"
                    Return actual = "reasoning" OrElse actual = "general"
                Case "multimodal"
                    Return actual = "multimodal"
                Case "embedding"
                    Return actual = "embedding"
                Case Else
                    Return True
            End Select
        End Function

        Private Shared Function PassesFit(fitType As String, options As RankingOptions) As Boolean
            If options.CpuOnly Then Return fitType = "cpu_only"
            Select Case options.Fit.ToLowerInvariant()
                Case "gpu"
                    Return fitType = "full_gpu" OrElse fitType = "partial_offload"
                Case "full-gpu"
                    Return fitType = "full_gpu"
                Case Else
                    Return fitType = "full_gpu" OrElse fitType = "partial_offload" OrElse fitType = "cpu_only"
            End Select
        End Function

        Private Shared Function PassesSpeed(tokPerSec As Double, options As RankingOptions) As Boolean
            Dim floor = If(options.MinSpeedTokPerSec, 0)
            Select Case options.Speed.ToLowerInvariant()
                Case "usable"
                    floor = Math.Max(floor, 10)
                Case "fast"
                    floor = Math.Max(floor, 30)
            End Select
            Return tokPerSec >= floor
        End Function

        Private Shared Function ResolveBenchmark(model As ModelInfo, benchmarks As Dictionary(Of String, BenchmarkEvidence), evidenceMode As String) As BenchmarkEvidence
            Dim keys = New List(Of String) From {
                Formatters.NormalizeModelName(model.RepoId),
                Formatters.NormalizeModelName(model.DisplayName),
                Formatters.NormalizeModelName(model.BaseModel)
            }.Where(Function(k) Not String.IsNullOrWhiteSpace(k)).Distinct().ToList()

            For Each key In keys
                For Each pair In benchmarks
                    If key.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) OrElse pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase) Then
                        If EvidenceAllowed(pair.Value.Source, evidenceMode) Then
                            Return CloneEvidence(pair.Value)
                        End If
                    End If
                Next
            Next

            If model.EvalScore.HasValue AndAlso EvidenceAllowed("self_reported", evidenceMode) Then
                Return New BenchmarkEvidence With {.Source = "self_reported", .Score = model.EvalScore.Value, .Confidence = 0.45R, .Status = "!sr", .Notes = "HuggingFace model-card evalResults only."}
            End If

            Return New BenchmarkEvidence With {.Source = "none", .Score = 0, .Confidence = 0, .Status = "?", .Notes = "No benchmark evidence."}
        End Function

        Private Shared Function EvidenceAllowed(source As String, mode As String) As Boolean
            Select Case mode.ToLowerInvariant()
                Case "strict"
                    Return source.Equals("direct", StringComparison.OrdinalIgnoreCase)
                Case "base"
                    Return source.Equals("direct", StringComparison.OrdinalIgnoreCase) OrElse source.Equals("variant", StringComparison.OrdinalIgnoreCase) OrElse source.Equals("base_model", StringComparison.OrdinalIgnoreCase)
                Case Else
                    Return True
            End Select
        End Function

        Private Shared Function CloneEvidence(value As BenchmarkEvidence) As BenchmarkEvidence
            Return New BenchmarkEvidence With {.Source = value.Source, .Score = value.Score, .Confidence = value.Confidence, .Status = value.Status, .Notes = value.Notes}
        End Function

        Private Shared Function ComputeScore(model As ModelInfo, modelVariant As ModelVariant, evidence As BenchmarkEvidence, fit As FitDecision, speed As SpeedEstimate, requestedUseCase As String) As Double
            Dim useCase = NormalizeUseCase(requestedUseCase)
            Dim sizeScore = Math.Min(35.0R, 4.2R * Math.Log(Math.Max(0.5R, model.ParameterCountB), 2) + 9)
            Dim weight = EvidenceWeight(evidence.Source)
            Dim evidenceScore = evidence.Score * Math.Max(0, Math.Min(1, evidence.Confidence))
            Dim quality = evidenceScore * weight + sizeScore * (1 - weight)
            quality *= 1 - QuantizationRules.QuantPenalty(modelVariant.Quantization)
            quality *= EvidenceConfidenceMultiplier(evidence.Source)
            quality += TaskAlignmentBump(model, useCase)
            quality += SourceTrustAdjustment(model.RepoId)
            quality += PopularityAdjustment(model, evidence.Source)
            quality += LineageAdjustment(model.RepoId)
            quality = Math.Clamp(quality, 0, 100)

            Dim speedScore = SpeedComponent(speed.TokPerSec, useCase)
            Dim fitComponent = FitMultiplier(fit) * 100
            Dim contextScore = ContextComponent(model, useCase)
            Dim weights = UseCaseWeights(useCase)
            Dim core = quality * weights.Quality + speedScore * weights.Speed + fitComponent * weights.Fit + contextScore * weights.Context
            core += SpeedAdjustment(speed.TokPerSec, fit.FitType) * 0.35R
            Return Math.Clamp(Math.Round(core, 1), 0, 100)
        End Function

        Private Shared Function NormalizeUseCase(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return "general"
            Select Case value.Trim().ToLowerInvariant()
                Case "all", "any"
                    Return "any"
                Case "code", "coding"
                    Return "coding"
                Case "reason", "reasoning", "math"
                    Return "reasoning"
                Case "chat"
                    Return "chat"
                Case "vision", "multimodal", "image"
                    Return "multimodal"
                Case "embed", "embedding"
                    Return "embedding"
                Case Else
                    Return "general"
            End Select
        End Function

        Private Shared Function UseCaseWeights(useCase As String) As (Quality As Double, Speed As Double, Fit As Double, Context As Double)
            Select Case useCase
                Case "coding"
                    Return (0.5R, 0.2R, 0.15R, 0.15R)
                Case "reasoning"
                    Return (0.55R, 0.15R, 0.15R, 0.15R)
                Case "chat"
                    Return (0.4R, 0.35R, 0.15R, 0.1R)
                Case "multimodal"
                    Return (0.5R, 0.2R, 0.15R, 0.15R)
                Case "embedding"
                    Return (0.3R, 0.4R, 0.2R, 0.1R)
                Case Else
                    Return (0.45R, 0.3R, 0.15R, 0.1R)
            End Select
        End Function

        Private Shared Function TaskAlignmentBump(model As ModelInfo, useCase As String) As Double
            Dim name = model.RepoId.ToLowerInvariant()
            Select Case useCase
                Case "coding"
                    If name.Contains("code", StringComparison.Ordinal) OrElse name.Contains("coder", StringComparison.Ordinal) OrElse name.Contains("starcoder", StringComparison.Ordinal) OrElse name.Contains("wizardcoder", StringComparison.Ordinal) Then Return 8
                Case "reasoning"
                    If name.Contains("r1", StringComparison.Ordinal) OrElse name.Contains("reason", StringComparison.Ordinal) OrElse name.Contains("math", StringComparison.Ordinal) Then Return 7
                    If model.ParameterCountB >= 13 Then Return 4
                Case "chat"
                    If name.Contains("chat", StringComparison.Ordinal) OrElse name.Contains("instruct", StringComparison.Ordinal) Then Return 5
                Case "multimodal"
                    If name.Contains("vision", StringComparison.Ordinal) OrElse name.Contains("-vl", StringComparison.Ordinal) OrElse name.Contains("llava", StringComparison.Ordinal) OrElse model.UseCase = "multimodal" Then Return 8
                Case "embedding"
                    If model.UseCase = "embedding" Then Return 8
            End Select
            Return 0
        End Function

        Private Shared Function SpeedComponent(tokPerSec As Double, useCase As String) As Double
            Dim target = If(useCase = "reasoning", 25.0R, If(useCase = "embedding", 200.0R, 40.0R))
            Return Math.Clamp(tokPerSec / target * 100.0R, 0, 100)
        End Function

        Private Shared Function ContextComponent(model As ModelInfo, useCase As String) As Double
            Dim target = 4096
            Select Case useCase
                Case "coding", "reasoning"
                    target = 8192
                Case "embedding"
                    target = 512
            End Select
            Dim context = If(model.ContextLength, 4096)
            If context >= target Then Return 100
            Return Math.Clamp(context / CDbl(target) * 100.0R, 0, 100)
        End Function

        Private Shared Function EvidenceWeight(source As String) As Double
            Select Case source
                Case "direct"
                    Return 0.62R
                Case "base_model"
                    Return 0.55R
                Case "variant"
                    Return 0.5R
                Case "line_interp"
                    Return 0.4R
                Case "self_reported"
                    Return 0.3R
                Case Else
                    Return 0
            End Select
        End Function

        Private Shared Function EvidenceConfidenceMultiplier(source As String) As Double
            Select Case source
                Case "direct"
                    Return 1.0R
                Case "variant", "base_model", "line_interp"
                    Return 0.78R
                Case Else
                    Return 0.55R
            End Select
        End Function

        Private Shared Function FitMultiplier(fit As FitDecision) As Double
            Select Case fit.FitType
                Case "full_gpu"
                    Return 1.0R
                Case "partial_offload"
                    Dim spill = Math.Clamp((fit.VramRequiredBytes - fit.VramAvailableBytes) / Math.Max(1.0R, CDbl(fit.VramRequiredBytes)), 0, 1)
                    Return Math.Clamp(0.88R - spill * 0.46R, 0.42R, 0.88R)
                Case "cpu_only"
                    Return 0.5R
                Case Else
                    Return 0
            End Select
        End Function

        Private Shared Function SpeedAdjustment(tokPerSec As Double, fitType As String) As Double
            Dim required = If(fitType = "full_gpu", 8.0R, If(fitType = "partial_offload", 4.0R, 1.5R))
            Dim ratio = tokPerSec / required
            If ratio < 1 Then
                Return -8.0R * (1.0R - Math.Clamp(ratio, 0, 1))
            End If
            Return Math.Min(8.0R, Math.Log(ratio + 1, 2) * 4.0R)
        End Function

        Private Shared Function SourceTrustAdjustment(repoId As String) As Double
            Dim text = repoId.ToLowerInvariant()
            If text.StartsWith("meta-llama/", StringComparison.Ordinal) OrElse text.StartsWith("mistralai/", StringComparison.Ordinal) OrElse text.StartsWith("qwen/", StringComparison.Ordinal) OrElse text.StartsWith("google/", StringComparison.Ordinal) OrElse text.StartsWith("microsoft/", StringComparison.Ordinal) Then
                Return 1.5R
            End If
            If text.Contains("thebloke/", StringComparison.Ordinal) OrElse text.Contains("bartowski/", StringComparison.Ordinal) OrElse text.Contains("lmstudio-community/", StringComparison.Ordinal) Then
                Return 0.8R
            End If
            Return 0
        End Function

        Private Shared Function PopularityAdjustment(model As ModelInfo, source As String) As Double
            If source = "direct" Then Return 0
            Dim downloads = Math.Log10(Math.Max(1, model.Downloads))
            Dim likes = Math.Log10(Math.Max(1, model.Likes))
            Return Math.Min(4.0R, downloads * 0.45R + likes * 0.35R)
        End Function

        Private Shared Function LineageAdjustment(repoId As String) As Double
            Dim text = repoId.ToLowerInvariant()
            If text.Contains("qwen3", StringComparison.Ordinal) OrElse text.Contains("llama-3.3", StringComparison.Ordinal) OrElse text.Contains("gemma-3", StringComparison.Ordinal) OrElse text.Contains("phi-4", StringComparison.Ordinal) Then Return 2
            If text.Contains("qwen1", StringComparison.Ordinal) OrElse text.Contains("llama-2", StringComparison.Ordinal) OrElse text.Contains("mistral-7b-v0.1", StringComparison.Ordinal) Then Return -2
            Return 0
        End Function

        Private Shared Function FamilySelectionScore(row As RankedModel) As Double
            Return row.Score + If(row.Benchmark.Source = "direct", 1.0R, 0) - If(row.FitType = "cpu_only", 1.0R, 0)
        End Function

        Private Shared Function SortScore(row As RankedModel) As Double
            Return row.Score + If(row.Benchmark.Source = "direct", 0.8R, 0) - If(row.FitType = "cpu_only", 1.2R, 0)
        End Function
    End Class
End Namespace
