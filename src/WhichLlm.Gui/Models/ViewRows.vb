Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Services
Imports WhichLlm.Core.Utilities
Imports WhichLlm.Gui.Infrastructure

Namespace Models
    Public Class ComboOption
        Public Property Label As String = ""
        Public Property Value As String = ""

        Public Overrides Function ToString() As String
            Return Label
        End Function
    End Class

    Public Class RankedModelRow
        Public Sub New(source As RankedModel)
            Rank = source.Rank
            Model = source.Model.RepoId
            ModelName = FriendlyModelName(source.Model.RepoId)
            RankLabel = If(source.Rank = 1, L("最有力", "Best"), L($"候補 {source.Rank}", $"Candidate {source.Rank}"))
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
            PopularityLabel = L(FormatJapaneseCount(source.Model.Downloads) & "回ダウンロード", $"{source.Model.Downloads:N0} downloads")
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
            Dim published = If(source.Model.PublishedDate.HasValue, source.Model.PublishedDate.Value.ToString("yyyy-MM-dd"), L("不明", "Unknown"))
            Dim lines As New List(Of String) From {
                L($"モデル: {source.Model.RepoId}", $"Model: {source.Model.RepoId}"),
                L($"用途: {FriendlyUseCase(source.UseCase)}", $"Use case: {FriendlyUseCase(source.UseCase)}"),
                L($"おすすめ度: {Formatters.FormatScore(source.Score)} / 100", $"Recommendation score: {Formatters.FormatScore(source.Score)} / 100"),
                L($"ライセンス: {If(String.IsNullOrWhiteSpace(source.Model.License), "未記載", source.Model.License)}", $"License: {If(String.IsNullOrWhiteSpace(source.Model.License), "not listed", source.Model.License)}"),
                L($"人気: ダウンロード {source.Model.Downloads:N0}回 / いいね {source.Model.Likes:N0}", $"Popularity: {source.Model.Downloads:N0} downloads / {source.Model.Likes:N0} likes"),
                L($"規模: {FriendlyParams(source.Model.ParameterCountB)}", $"Size: {FriendlyParams(source.Model.ParameterCountB)}"),
                L($"動作見込み: {FriendlyFit(source.FitType)}", $"Expected fit: {FriendlyFit(source.FitType)}"),
                L($"量子化（圧縮形式）: {source.SelectedVariant.Quantization}", $"Quantization: {source.SelectedVariant.Quantization}"),
                L($"必要なGPUメモリ: {Formatters.FormatBytes(source.VramRequiredBytes)}", $"Required GPU memory: {Formatters.FormatBytes(source.VramRequiredBytes)}"),
                L($"使えるGPUメモリ: {Formatters.FormatBytes(source.VramAvailableBytes)}", $"Usable GPU memory: {Formatters.FormatBytes(source.VramAvailableBytes)}"),
                L($"速度の目安: {FriendlySpeedWord(source.EstimatedTokPerSec)}（約 {source.EstimatedTokPerSec:0} tok/s）", $"Estimated speed: {FriendlySpeedWord(source.EstimatedTokPerSec)} (about {source.EstimatedTokPerSec:0} tok/s)"),
                L($"速度の確かさ: {FriendlyConfidence(source.SpeedConfidence)}", $"Speed confidence: {FriendlyConfidence(source.SpeedConfidence)}"),
                L($"根拠: {FriendlyEvidence(source.Benchmark)}", $"Evidence: {FriendlyEvidence(source.Benchmark)}"),
                L($"根拠スコア: {source.Benchmark.Score:0.#} / 信頼度 {source.Benchmark.Confidence:0.00} / 種別 {FriendlyEvidenceSource(source.Benchmark.Source)}", $"Evidence score: {source.Benchmark.Score:0.#} / confidence {source.Benchmark.Confidence:0.00} / type {FriendlyEvidenceSource(source.Benchmark.Source)}"),
                L($"公開日: {published}", $"Published: {published}")
            }
            If ShouldShowBenchmarkNotes(source.Benchmark.Notes) Then
                lines.Add(L($"根拠メモ: {source.Benchmark.Notes}", $"Evidence notes: {source.Benchmark.Notes}"))
            End If
            lines.AddRange(source.MemoryNotes.Select(Function(n) L("メモリ: ", "Memory: ") & FriendlyRuntimeNote(n)))
            lines.AddRange(source.SpeedNotes.Select(Function(n) L("速度: ", "Speed: ") & FriendlyRuntimeNote(n)))
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
                    Return L("会話", "Chat")
                Case "coding"
                    Return L("プログラミング", "Coding")
                Case "reasoning"
                    Return L("論理・数学", "Reasoning")
                Case "multimodal"
                    Return L("画像も扱う", "Multimodal")
                Case "embedding"
                    Return L("検索・分類", "Embedding")
                Case Else
                    Return L("日常", "Everyday")
            End Select
        End Function

        Private Shared Function FriendlyFit(fitType As String) As String
            Select Case If(fitType, "").Trim().ToLowerInvariant()
                Case "full_gpu"
                    Return L("快適", "Comfortable")
                Case "partial_offload"
                    Return L("動くが重め", "Runs, but heavy")
                Case "cpu_only"
                    Return L("CPUで動作", "CPU only")
                Case Else
                    Return L("要確認", "Check")
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
            If tokPerSec >= 30 Then Return L("とても速い", "Very fast")
            If tokPerSec >= 10 Then Return L("普段使いに十分", "Good for everyday use")
            If tokPerSec > 0 Then Return L("ややゆっくり", "A bit slow")
            Return L("速度不明", "Unknown speed")
        End Function

        Private Shared Function FriendlyConfidence(confidence As String) As String
            Select Case If(confidence, "").Trim().ToLowerInvariant()
                Case "high"
                    Return L("高い", "High")
                Case "low"
                    Return L("目安", "Rough")
                Case Else
                    Return L("まずまず", "Moderate")
            End Select
        End Function

        Private Shared Function FriendlyParams(countB As Double) As String
            If countB <= 0 Then Return L("規模不明", "Unknown size")
            Return L($"約{(countB * 10):0.#}億パラメータ", $"about {countB:0.#}B parameters")
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
                    Return L($"このPCのGPUメモリ（{usable}）に収まるので、追加の負担なく快適に動かせる見込みです。", $"It fits in this PC's GPU memory ({usable}), so it should run comfortably.")
                Case "partial_offload"
                    Return L($"必要メモリの目安は{required}。一部をCPUに分担すれば動きますが、速度は環境によって変わります。", $"Estimated memory is {required}. It can run by spilling part of the work to CPU/RAM, but speed depends on the system.")
                Case "cpu_only"
                    Return L("GPUがなくてもCPUだけで動かせます（速度は控えめになりがちです）。", "It can run on CPU only, though speed will usually be modest.")
                Case Else
                    Return L($"必要メモリの目安は{required}です。", $"Estimated memory is {required}.")
            End Select
        End Function

        Private Shared Function FriendlyEvidence(benchmark As BenchmarkEvidence) As String
            Select Case If(benchmark.Source, "").Trim().ToLowerInvariant()
                Case "direct"
                    Return L("ベンチマーク", "Benchmark")
                Case "base_model"
                    Return L("ベースモデル", "Base model")
                Case "variant"
                    Return L("近いモデルから推定", "Estimated from similar model")
                Case "line_interp"
                    Return L("同系列から推定", "Estimated from same line")
                Case "self_reported"
                    Return L("自己申告", "Self-reported")
                Case "none"
                    Return L("根拠なし", "No evidence")
                Case Else
                    Return L("推定", "Estimate")
            End Select
        End Function

        Private Shared Function FriendlyEvidenceSource(source As String) As String
            Select Case If(source, "").Trim().ToLowerInvariant()
                Case "direct"
                    Return L("同一モデルのベンチマーク", "Benchmark for the same model")
                Case "base_model"
                    Return L("ベースモデルのベンチマーク", "Benchmark for the base model")
                Case "variant"
                    Return L("近い派生モデルからの推定", "Estimate from a close variant")
                Case "line_interp"
                    Return L("同じモデル系列からのサイズ補間", "Size interpolation from the same model line")
                Case "self_reported"
                    Return L("モデルカードの自己申告", "Self-reported model card result")
                Case "none"
                    Return L("公開根拠なし", "No public evidence")
                Case Else
                    Return source
            End Select
        End Function

        Private Shared Function FriendlyRuntimeNote(note As String) As String
            Select Case note
                Case "Partial offload estimates vary strongly by runtime and PCIe bandwidth."
                    Return L("一部だけGPUに載せる場合、実際の速度は環境によって大きく変わります。", "Partial GPU offload speed varies strongly by runtime and PCIe bandwidth.")
                Case "CPU-only speed is a planning estimate."
                    Return L("CPUだけで動かす速度は計画用の目安です。", "CPU-only speed is a planning estimate.")
                Case "Multi-GPU speed depends on backend split mode."
                    Return L("複数GPUでは、使う実行環境の分割方法で速度が変わります。", "Multi-GPU speed depends on the backend split mode.")
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
            SharedMemory = If(source.IsSharedMemory, L("共有", "Shared"), L("専用", "Dedicated"))
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
                    Return L("Windowsから専用メモリを確認", "Dedicated memory read from Windows")
                Case "VRAM read from AMD hipInfo."
                    Return L("AMD hipInfoから専用メモリを確認", "Dedicated memory read from AMD hipInfo")
                Case "GPU read from Intel xpu-smi."
                    Return L("Intel xpu-smiからGPU情報を確認", "GPU information read from Intel xpu-smi")
                Case "Manual VRAM override applied."
                    Return L("手入力のGPUメモリを使用", "Manual VRAM override applied")
                Case "Manual bandwidth override applied."
                    Return L("手入力の帯域を使用", "Manual bandwidth override applied")
                Case "Shared-memory GPU detected; system RAM will be considered for fit checks."
                    Return L("メインメモリ共有型として計算", "Calculated as a shared-memory GPU")
                Case "Unknown GPU; using manual VRAM/bandwidth overrides when present."
                    Return L("未登録GPU。必要なら手入力値を使用", "Unknown GPU; manual overrides will be used if present")
                Case Else
                    If note.StartsWith("HIP arch:", StringComparison.OrdinalIgnoreCase) Then
                        Return L("HIPアーキテクチャ: ", "HIP architecture: ") & note.Substring("HIP arch:".Length).Trim()
                    End If
                    Return note
            End Select
        End Function
    End Class

    Public Class PlanDisplayRow
        Public Sub New(source As PlanRow)
            Quantization = source.Quantization
            Required = Formatters.FormatBytes(source.RequiredBytes)
            Current = If(source.FitsCurrentHardware, L("このPCでOK", "OK on this PC"), L("このPCでは厳しい", "Hard on this PC"))
            If source.RecommendedGpus.Count > 0 Then
                CommonGpus = String.Join(", ", source.RecommendedGpus.Select(AddressOf FormatGpuRecommendation))
            Else
                CommonGpus = String.Join(", ", source.FitsCommonGpus.Where(Function(p) p.Value).Select(Function(p) p.Key))
            End If
            If CommonGpus.Length = 0 Then CommonGpus = L("該当なし", "None")
        End Sub

        Public Property Quantization As String = ""
        Public Property Required As String = ""
        Public Property Current As String = ""
        Public Property CommonGpus As String = ""

        Private Shared Function FormatGpuRecommendation(gpu As GpuRecommendation) As String
            Dim fit = If(gpu.FitType = "full_gpu", "", L(" / CPU併用", " / CPU offload"))
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
                    Return L("快適", "Comfortable")
                Case "partial_offload"
                    Return L("動くが重め", "Runs, but heavy")
                Case "cpu_only"
                    Return L("CPUで動作", "CPU only")
                Case Else
                    Return L("要確認", "Check")
            End Select
        End Function
    End Class

    Friend Module ViewRowText
        Friend Function L(ja As String, en As String) As String
            Return AppText.Text(ja, en)
        End Function
    End Module
End Namespace
