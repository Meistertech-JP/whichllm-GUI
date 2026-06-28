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
            ModelName = FriendlyModelName(source.Model.RepoId)
            RankLabel = If(source.Rank = 1, "最有力", $"候補 {source.Rank}")
            UseCase = source.UseCase
            UseCaseLabel = FriendlyUseCase(source.UseCase)
            Quantization = source.SelectedVariant.Quantization
            Score = Formatters.FormatScore(source.Score)
            Fit = source.FitType
            FitLabel = FriendlyFit(source.FitType)
            Memory = Formatters.FormatBytes(source.VramRequiredBytes)
            Speed = source.EstimatedTokPerSec.ToString("0.0") & " tok/s"
            SpeedWord = FriendlySpeedWord(source.EstimatedTokPerSec)
            FitKind = FitKindOf(source.FitType)
            UsableMemory = Formatters.FormatBytes(source.VramAvailableBytes)
            PopularityLabel = FormatJapaneseCount(source.Model.Downloads) & "回ダウンロード"
            WhyLine = BuildWhyLine(source)
            Evidence = source.Benchmark.Source & source.Benchmark.Status
            EvidenceLabel = FriendlyEvidence(source.Benchmark)
            Published = If(source.Model.PublishedDate.HasValue, source.Model.PublishedDate.Value.ToString("yyyy-MM-dd"), "")
            Details = BuildDetails(source)
        End Sub

        Public Property Rank As Integer
        Public Property RankLabel As String = ""
        Public Property Model As String = ""
        Public Property ModelName As String = ""
        Public Property UseCase As String = ""
        Public Property UseCaseLabel As String = ""
        Public Property Quantization As String = ""
        Public Property Score As String = ""
        Public Property Fit As String = ""
        Public Property FitLabel As String = ""
        Public Property Memory As String = ""
        Public Property Speed As String = ""
        Public Property SpeedWord As String = ""
        Public Property FitKind As String = ""
        Public Property UsableMemory As String = ""
        Public Property PopularityLabel As String = ""
        Public Property WhyLine As String = ""
        Public Property Evidence As String = ""
        Public Property EvidenceLabel As String = ""
        Public Property Published As String = ""
        Public Property Details As String = ""

        Private Shared Function BuildDetails(source As RankedModel) As String
            Dim lines As New List(Of String) From {
                $"モデル: {source.Model.RepoId}",
                $"用途: {FriendlyUseCase(source.UseCase)}",
                $"ライセンス: {If(String.IsNullOrWhiteSpace(source.Model.License), "未記載", source.Model.License)}",
                $"人気: ダウンロード {source.Model.Downloads:N0}回 / いいね {source.Model.Likes:N0}",
                $"規模: {FriendlyParams(source.Model.ParameterCountB)}",
                $"動作見込み: {FriendlyFit(source.FitType)}",
                $"必要なGPUメモリ: {Formatters.FormatBytes(source.VramRequiredBytes)}",
                $"使えるGPUメモリ: {Formatters.FormatBytes(source.VramAvailableBytes)}",
                $"速度の目安: {FriendlySpeedWord(source.EstimatedTokPerSec)}（約 {source.EstimatedTokPerSec:0} tok/s）",
                $"速度の確かさ: {FriendlyConfidence(source.SpeedConfidence)}",
                $"根拠: {FriendlyEvidence(source.Benchmark)}",
                $"根拠スコア: {source.Benchmark.Score:0.#} / 信頼度 {source.Benchmark.Confidence:0.00} / 種別 {FriendlyEvidenceSource(source.Benchmark.Source)}"
            }
            If ShouldShowBenchmarkNotes(source.Benchmark.Notes) Then
                lines.Add($"根拠メモ: {source.Benchmark.Notes}")
            End If
            lines.AddRange(source.MemoryNotes.Select(Function(n) "メモリ: " & FriendlyRuntimeNote(n)))
            lines.AddRange(source.SpeedNotes.Select(Function(n) "速度: " & FriendlyRuntimeNote(n)))
            Return String.Join(Environment.NewLine, lines)
        End Function

        Private Shared Function FriendlyModelName(repoId As String) As String
            If String.IsNullOrWhiteSpace(repoId) Then Return ""
            Dim parts = repoId.Split("/"c)
            Return parts(parts.Length - 1)
        End Function

        Private Shared Function FriendlyUseCase(useCase As String) As String
            Select Case If(useCase, "").Trim().ToLowerInvariant()
                Case "chat"
                    Return "会話"
                Case "coding"
                    Return "プログラミング"
                Case "reasoning"
                    Return "論理・数学"
                Case "multimodal"
                    Return "画像も扱う"
                Case "embedding"
                    Return "検索・分類"
                Case Else
                    Return "日常"
            End Select
        End Function

        Private Shared Function FriendlyFit(fitType As String) As String
            Select Case If(fitType, "").Trim().ToLowerInvariant()
                Case "full_gpu"
                    Return "快適"
                Case "partial_offload"
                    Return "動くが重め"
                Case "cpu_only"
                    Return "CPUで動作"
                Case Else
                    Return "要確認"
            End Select
        End Function

        Private Shared Function FitKindOf(fitType As String) As String
            Select Case If(fitType, "").Trim().ToLowerInvariant()
                Case "full_gpu"
                    Return "ok"
                Case "partial_offload"
                    Return "warn"
                Case "cpu_only"
                    Return "cpu"
                Case Else
                    Return "unknown"
            End Select
        End Function

        Private Shared Function FriendlySpeedWord(tokPerSec As Double) As String
            If tokPerSec >= 30 Then Return "とても速い"
            If tokPerSec >= 10 Then Return "普段使いに十分"
            If tokPerSec > 0 Then Return "ややゆっくり"
            Return "速度不明"
        End Function

        Private Shared Function FriendlyConfidence(confidence As String) As String
            Select Case If(confidence, "").Trim().ToLowerInvariant()
                Case "high"
                    Return "高い"
                Case "low"
                    Return "目安"
                Case Else
                    Return "まずまず"
            End Select
        End Function

        Private Shared Function FriendlyParams(countB As Double) As String
            If countB <= 0 Then Return "規模不明"
            Return $"約{(countB * 10):0.#}億パラメータ"
        End Function

        Private Shared Function FormatJapaneseCount(value As Long) As String
            If value >= 100000000L Then Return (value / 100000000.0).ToString("0.#") & "億"
            If value >= 10000L Then Return (value / 10000.0).ToString("0.#") & "万"
            Return value.ToString("N0")
        End Function

        Private Shared Function BuildWhyLine(source As RankedModel) As String
            Dim required = Formatters.FormatBytes(source.VramRequiredBytes)
            Dim usable = Formatters.FormatBytes(source.VramAvailableBytes)
            Select Case If(source.FitType, "").Trim().ToLowerInvariant()
                Case "full_gpu"
                    Return $"このPCのGPUメモリ（{usable}）に収まるので、追加の負担なく快適に動かせる見込みです。"
                Case "partial_offload"
                    Return $"必要メモリの目安は{required}。一部をCPUに分担すれば動きますが、速度は環境によって変わります。"
                Case "cpu_only"
                    Return "GPUがなくてもCPUだけで動かせます（速度は控えめになりがちです）。"
                Case Else
                    Return $"必要メモリの目安は{required}です。"
            End Select
        End Function

        Private Shared Function FriendlyEvidence(benchmark As BenchmarkEvidence) As String
            Select Case If(benchmark.Source, "").Trim().ToLowerInvariant()
                Case "direct"
                    Return "内蔵ベンチ"
                Case "base_model"
                    Return "ベースモデル"
                Case "variant"
                    Return "近いモデルから推定"
                Case "self_reported"
                    Return "自己申告"
                Case "none"
                    Return "根拠なし"
                Case Else
                    Return "推定"
            End Select
        End Function

        Private Shared Function FriendlyEvidenceSource(source As String) As String
            Select Case If(source, "").Trim().ToLowerInvariant()
                Case "direct"
                    Return "同系モデルのベンチマーク"
                Case "base_model"
                    Return "ベースモデルのベンチマーク"
                Case "variant"
                    Return "近い派生モデルからの推定"
                Case "self_reported"
                    Return "モデルカードの自己申告"
                Case "none"
                    Return "公開根拠なし"
                Case Else
                    Return source
            End Select
        End Function

        Private Shared Function FriendlyRuntimeNote(note As String) As String
            Select Case note
                Case "Partial offload estimates vary strongly by runtime and PCIe bandwidth."
                    Return "一部だけGPUに載せる場合、実際の速度は環境によって大きく変わります。"
                Case "CPU-only speed is a planning estimate."
                    Return "CPUだけで動かす速度は計画用の目安です。"
                Case "Multi-GPU speed depends on backend split mode."
                    Return "複数GPUでは、使う実行環境の分割方法で速度が変わります。"
                Case Else
                    Return note
            End Select
        End Function

        Private Shared Function ShouldShowBenchmarkNotes(notes As String) As Boolean
            If String.IsNullOrWhiteSpace(notes) Then Return False
            Return Not notes.Equals("Curated GUI seed benchmark used when live benchmark feeds are unavailable.", StringComparison.OrdinalIgnoreCase)
        End Function
    End Class

    Public Class HardwareGpuRow
        Public Sub New(source As GpuInfo)
            Name = source.Name
            Vendor = source.Vendor
            Vram = Formatters.FormatBytes(source.VramBytes)
            UsableVram = Formatters.FormatBytes(source.EffectiveVramBytes)
            Bandwidth = If(source.MemoryBandwidthGbps.HasValue, source.MemoryBandwidthGbps.Value.ToString("0") & " GB/s", "")
            SharedMemory = If(source.IsSharedMemory, "共有", "専用")
            Notes = String.Join("; ", source.Notes.Select(AddressOf FriendlyGpuNote))
        End Sub

        Public Property Name As String = ""
        Public Property Vendor As String = ""
        Public Property Vram As String = ""
        Public Property UsableVram As String = ""
        Public Property Bandwidth As String = ""
        Public Property SharedMemory As String = ""
        Public Property Notes As String = ""

        Private Shared Function FriendlyGpuNote(note As String) As String
            Select Case note
                Case "VRAM read from Windows registry."
                    Return "Windowsから専用メモリを確認"
                Case "VRAM read from AMD hipInfo."
                    Return "AMD hipInfoから専用メモリを確認"
                Case "GPU read from Intel xpu-smi."
                    Return "Intel xpu-smiからGPU情報を確認"
                Case "Manual VRAM override applied."
                    Return "手入力のGPUメモリを使用"
                Case "Manual bandwidth override applied."
                    Return "手入力の帯域を使用"
                Case "Shared-memory GPU detected; system RAM will be considered for fit checks."
                    Return "メインメモリ共有型として計算"
                Case "Unknown GPU; using manual VRAM/bandwidth overrides when present."
                    Return "未登録GPU。必要なら手入力値を使用"
                Case Else
                    If note.StartsWith("HIP arch:", StringComparison.OrdinalIgnoreCase) Then
                        Return "HIPアーキテクチャ: " & note.Substring("HIP arch:".Length).Trim()
                    End If
                    Return note
            End Select
        End Function
    End Class

    Public Class PlanDisplayRow
        Public Sub New(source As PlanRow)
            Quantization = source.Quantization
            Required = Formatters.FormatBytes(source.RequiredBytes)
            Current = If(source.FitsCurrentHardware, "このPCでOK", "このPCでは厳しい")
            If source.RecommendedGpus.Count > 0 Then
                CommonGpus = String.Join(", ", source.RecommendedGpus.Select(AddressOf FormatGpuRecommendation))
            Else
                CommonGpus = String.Join(", ", source.FitsCommonGpus.Where(Function(p) p.Value).Select(Function(p) p.Key))
            End If
            If CommonGpus.Length = 0 Then CommonGpus = "該当なし"
        End Sub

        Public Property Quantization As String = ""
        Public Property Required As String = ""
        Public Property Current As String = ""
        Public Property CommonGpus As String = ""

        Private Shared Function FormatGpuRecommendation(gpu As GpuRecommendation) As String
            Dim fit = If(gpu.FitType = "full_gpu", "", " / CPU併用")
            Return $"{gpu.Name} ({gpu.VramGb:0.#}GB{fit})"
        End Function
    End Class

    Public Class UpgradeDisplayRow
        Public Sub New(source As UpgradeRow)
            TargetGpu = source.TargetGpu
            TopModel = source.TopModel
            TopScore = source.TopScore.ToString("0.0")
            Fit = FriendlyFit(source.FitType)
            Speed = source.EstimatedTokPerSec.ToString("0.0") & " tok/s"
            Gain = source.GainVsCurrent.ToString("+0.0;-0.0;0.0")
        End Sub

        Public Property TargetGpu As String = ""
        Public Property TopModel As String = ""
        Public Property TopScore As String = ""
        Public Property Fit As String = ""
        Public Property Speed As String = ""
        Public Property Gain As String = ""

        Private Shared Function FriendlyFit(fitType As String) As String
            Select Case If(fitType, "").Trim().ToLowerInvariant()
                Case "full_gpu"
                    Return "快適"
                Case "partial_offload"
                    Return "動くが重め"
                Case "cpu_only"
                    Return "CPUで動作"
                Case Else
                    Return "要確認"
            End Select
        End Function
    End Class
End Namespace
