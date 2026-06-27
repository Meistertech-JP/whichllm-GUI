Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto

Namespace Services
    Public Interface IHardwareDetector
        Function DetectAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of HardwareInfo)
    End Interface

    Public Interface IGpuCatalog
        Function Resolve(input As String, Optional overrideVramBytes As Long? = Nothing, Optional overrideBandwidthGbps As Double? = Nothing) As GpuInfo
        Function ResolveMany(inputs As IEnumerable(Of String)) As List(Of GpuInfo)
        Function Search(term As String) As IReadOnlyList(Of GpuCatalogEntry)
        Function CommonGpuNames() As IReadOnlyList(Of String)
    End Interface

    Public Interface IHuggingFaceClient
        Function FetchModelsAsync(profile As String, useCase As String, refresh As Boolean, Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo))
    End Interface

    Public Interface IModelCache
        Function LoadAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo))
        Function SaveAsync(models As IEnumerable(Of ModelInfo), Optional cancellationToken As CancellationToken = Nothing) As Task
        Function IsFresh() As Boolean
    End Interface

    Public Interface IBenchmarkCache
        Function LoadAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence))
        Function SaveAsync(evidence As Dictionary(Of String, BenchmarkEvidence), Optional cancellationToken As CancellationToken = Nothing) As Task
        Function IsFresh() As Boolean
    End Interface

    Public Interface IModelFetcher
        Function LoadModelsAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo))
    End Interface

    Public Interface IBenchmarkProvider
        Function LoadBenchmarksAsync(refresh As Boolean, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence))
    End Interface

    Public Interface IModelGrouper
        Function FamilyKey(model As ModelInfo) As String
    End Interface

    Public Interface IVramEstimator
        Function EstimateRequiredBytes(model As ModelInfo, modelVariant As ModelVariant, contextLength As Integer) As Long
        Function ClassifyFit(requiredBytes As Long, hardware As HardwareInfo, options As RankingOptions) As FitDecision
    End Interface

    Public Interface IPerformanceEstimator
        Function Estimate(model As ModelInfo, modelVariant As ModelVariant, fit As FitDecision, hardware As HardwareInfo) As SpeedEstimate
    End Interface

    Public Interface IRanker
        Function RankAsync(models As IEnumerable(Of ModelInfo), hardware As HardwareInfo, benchmarks As Dictionary(Of String, BenchmarkEvidence), options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of RankingResult)
    End Interface

    Public Interface ISnippetGenerator
        Function Generate(model As ModelInfo, Optional quant As String = "") As SnippetResult
    End Interface

    Public Class GpuCatalogEntry
        Public Property Name As String = ""
        Public Property Vendor As String = ""
        Public Property VramGb As Double
        Public Property BandwidthGbps As Double
        Public Property ComputeCapability As String = ""
        Public Property IsSharedMemory As Boolean
        Public Property Aliases As New List(Of String)
    End Class

    Public Class FitDecision
        Public Property FitType As String = ""
        Public Property VramRequiredBytes As Long
        Public Property VramAvailableBytes As Long
        Public Property UsesMultiGpu As Boolean
        Public Property MultiGpuEffectiveVramBytes As Long?
        Public Property IsRunnable As Boolean
        Public Property Notes As New List(Of String)
    End Class

    Public Class SpeedEstimate
        Public Property TokPerSec As Double
        Public Property Confidence As String = "medium"
        Public Property RangeLow As Double?
        Public Property RangeHigh As Double?
        Public Property Notes As New List(Of String)
    End Class
End Namespace
