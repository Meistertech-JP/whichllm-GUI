Option Strict On
Option Explicit On

Namespace Dto
    Public Class RankingOptions
        Public Property Top As Integer = 10
        Public Property ContextLength As Integer = 4096
        Public Property Quant As String = ""
        Public Property MinSpeedTokPerSec As Double?
        Public Property Speed As String = "any"
        Public Property Fit As String = "any"
        Public Property CpuOnly As Boolean
        Public Property Profile As String = "general"
        Public Property UseCase As String = "general"
        Public Property Evidence As String = "base"
        Public Property MinParamsB As Double?
        Public Property Refresh As Boolean
        Public Property Details As Boolean
        Public Property SimulatedGpuInputs As New List(Of String)
        Public Property OverrideVramBytes As Long?
        Public Property OverrideBandwidthGbps As Double?
        Public Property OverrideRamBandwidthGbps As Double?
        Public Property GpuIndex As Integer?
        Public Property VramHeadroom As String = "auto"
        Public Property RamBudget As String = "available"
    End Class

    Public Class RankedModel
        Public Property Rank As Integer
        Public Property Model As ModelInfo = New ModelInfo()
        Public Property SelectedVariant As ModelVariant = New ModelVariant()
        Public Property Score As Double
        Public Property FitType As String = ""
        Public Property VramRequiredBytes As Long
        Public Property VramAvailableBytes As Long
        Public Property UsesMultiGpu As Boolean
        Public Property MultiGpuEffectiveVramBytes As Long?
        Public Property EstimatedTokPerSec As Double
        Public Property SpeedConfidence As String = "medium"
        Public Property SpeedRangeLowTokPerSec As Double?
        Public Property SpeedRangeHighTokPerSec As Double?
        Public Property SpeedNotes As New List(Of String)
        Public Property Benchmark As BenchmarkEvidence = New BenchmarkEvidence()
        Public Property MemoryNotes As New List(Of String)
        Public Property UseCase As String = "general"
    End Class

    Public Class RankingResult
        Public Property Hardware As HardwareInfo = New HardwareInfo()
        Public Property Models As New List(Of RankedModel)
        Public Property GeneratedAt As DateTimeOffset = DateTimeOffset.Now
        Public Property Warnings As New List(Of String)
    End Class

    Public Class PlanRow
        Public Property Quantization As String = ""
        Public Property RequiredBytes As Long
        Public Property FitsCurrentHardware As Boolean
        Public Property FitsCommonGpus As New Dictionary(Of String, Boolean)
        Public Property RecommendedGpus As New List(Of GpuRecommendation)
    End Class

    Public Class GpuRecommendation
        Public Property Name As String = ""
        Public Property Vendor As String = ""
        Public Property FitType As String = ""
        Public Property VramGb As Double
        Public Property BandwidthGbps As Double?
        Public Property BalanceScore As Double
    End Class

    Public Class PlanResult
        Public Property MatchedModel As ModelInfo = New ModelInfo()
        Public Property ContextLength As Integer
        Public Property Rows As New List(Of PlanRow)
        Public Property Warnings As New List(Of String)
    End Class

    Public Class UpgradeRow
        Public Property TargetGpu As String = ""
        Public Property TopModel As String = ""
        Public Property TopScore As Double
        Public Property FitType As String = ""
        Public Property EstimatedTokPerSec As Double
        Public Property GainVsCurrent As Double
    End Class

    Public Class UpgradeResult
        Public Property CurrentTopModel As String = ""
        Public Property CurrentScore As Double
        Public Property Rows As New List(Of UpgradeRow)
        Public Property Warnings As New List(Of String)
    End Class

    Public Class SnippetResult
        Public Property Model As ModelInfo = New ModelInfo()
        Public Property SelectedVariant As ModelVariant = New ModelVariant()
        Public Property CommandLine As String = ""
        Public Property Code As String = ""
        Public Property Warnings As New List(Of String)
    End Class
End Namespace
