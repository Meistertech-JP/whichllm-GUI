Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Services
Imports WhichLlm.Core.Utilities

Namespace Models
    Public Class ComboOption
        Public Property Label As String = ""
        Public Property Value As String = ""
    End Class

    Public Class RankedModelRow
        Public Sub New(source As RankedModel)
            Rank = source.Rank
            Model = source.Model.RepoId
            UseCase = source.UseCase
            Quantization = source.SelectedVariant.Quantization
            Score = Formatters.FormatScore(source.Score)
            Fit = source.FitType
            Memory = Formatters.FormatBytes(source.VramRequiredBytes)
            Speed = source.EstimatedTokPerSec.ToString("0.0") & " tok/s"
            Evidence = source.Benchmark.Source & source.Benchmark.Status
            Published = If(source.Model.PublishedDate.HasValue, source.Model.PublishedDate.Value.ToString("yyyy-MM-dd"), "")
            Details = BuildDetails(source)
        End Sub

        Public Property Rank As Integer
        Public Property Model As String = ""
        Public Property UseCase As String = ""
        Public Property Quantization As String = ""
        Public Property Score As String = ""
        Public Property Fit As String = ""
        Public Property Memory As String = ""
        Public Property Speed As String = ""
        Public Property Evidence As String = ""
        Public Property Published As String = ""
        Public Property Details As String = ""

        Private Shared Function BuildDetails(source As RankedModel) As String
            Dim lines As New List(Of String) From {
                $"Repo: {source.Model.RepoId}",
                $"Use case: {source.UseCase}",
                $"License: {source.Model.License}",
                $"Downloads/Likes: {source.Model.Downloads:N0} / {source.Model.Likes:N0}",
                $"Params: {source.Model.ParameterCountB:0.##}B",
                $"Fit: {source.FitType}",
                $"VRAM required: {Formatters.FormatBytes(source.VramRequiredBytes)}",
                $"VRAM available: {Formatters.FormatBytes(source.VramAvailableBytes)}",
                $"Speed confidence: {source.SpeedConfidence}",
                $"Benchmark: {source.Benchmark.Source} confidence {source.Benchmark.Confidence:0.00}",
                $"Benchmark notes: {source.Benchmark.Notes}"
            }
            lines.AddRange(source.MemoryNotes.Select(Function(n) "Memory: " & n))
            lines.AddRange(source.SpeedNotes.Select(Function(n) "Speed: " & n))
            Return String.Join(Environment.NewLine, lines)
        End Function
    End Class

    Public Class HardwareGpuRow
        Public Sub New(source As GpuInfo)
            Name = source.Name
            Vendor = source.Vendor
            Vram = Formatters.FormatBytes(source.VramBytes)
            UsableVram = Formatters.FormatBytes(source.EffectiveVramBytes)
            Bandwidth = If(source.MemoryBandwidthGbps.HasValue, source.MemoryBandwidthGbps.Value.ToString("0") & " GB/s", "")
            SharedMemory = If(source.IsSharedMemory, "Yes", "No")
            Notes = String.Join("; ", source.Notes)
        End Sub

        Public Property Name As String = ""
        Public Property Vendor As String = ""
        Public Property Vram As String = ""
        Public Property UsableVram As String = ""
        Public Property Bandwidth As String = ""
        Public Property SharedMemory As String = ""
        Public Property Notes As String = ""
    End Class

    Public Class PlanDisplayRow
        Public Sub New(source As PlanRow)
            Quantization = source.Quantization
            Required = Formatters.FormatBytes(source.RequiredBytes)
            Current = If(source.FitsCurrentHardware, "OK", "NG")
            CommonGpus = String.Join(", ", source.FitsCommonGpus.Where(Function(p) p.Value).Select(Function(p) p.Key))
            If CommonGpus.Length = 0 Then CommonGpus = "None"
        End Sub

        Public Property Quantization As String = ""
        Public Property Required As String = ""
        Public Property Current As String = ""
        Public Property CommonGpus As String = ""
    End Class

    Public Class UpgradeDisplayRow
        Public Sub New(source As UpgradeRow)
            TargetGpu = source.TargetGpu
            TopModel = source.TopModel
            TopScore = source.TopScore.ToString("0.0")
            Fit = source.FitType
            Speed = source.EstimatedTokPerSec.ToString("0.0") & " tok/s"
            Gain = source.GainVsCurrent.ToString("+0.0;-0.0;0.0")
        End Sub

        Public Property TargetGpu As String = ""
        Public Property TopModel As String = ""
        Public Property TopScore As String = ""
        Public Property Fit As String = ""
        Public Property Speed As String = ""
        Public Property Gain As String = ""
    End Class
End Namespace
