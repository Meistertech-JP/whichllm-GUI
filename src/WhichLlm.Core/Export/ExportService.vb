Option Strict On
Option Explicit On

Imports System.Text
Imports System.Text.Json
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Utilities

Namespace Export
    Public Class ExportService
        Public Function RankingToMarkdown(result As RankingResult) As String
            Dim builder As New StringBuilder()
            builder.AppendLine("| Rank | Model | Use Case | Quant | Score | Fit | Memory | Speed | Evidence | Published |")
            builder.AppendLine("| ---: | --- | --- | --- | ---: | --- | ---: | ---: | --- | --- |")
            For Each row In result.Models
                builder.AppendLine($"| {row.Rank} | {EscapePipe(row.Model.RepoId)} | {row.UseCase} | {row.SelectedVariant.Quantization} | {Formatters.FormatScore(row.Score)} | {row.FitType} | {Formatters.FormatBytes(row.VramRequiredBytes)} | {row.EstimatedTokPerSec:0.0} tok/s | {row.Benchmark.Source}{row.Benchmark.Status} | {If(row.Model.PublishedDate.HasValue, row.Model.PublishedDate.Value.ToString("yyyy-MM-dd"), "")} |")
            Next
            Return builder.ToString()
        End Function

        Public Function RankingToJson(result As RankingResult) As String
            Dim payload = New With {
                .generated_at = result.GeneratedAt,
                .hardware = New With {
                    .cpu_name = result.Hardware.CpuName,
                    .physical_cores = result.Hardware.PhysicalCores,
                    .supports_avx2 = result.Hardware.SupportsAvx2,
                    .supports_avx512 = result.Hardware.SupportsAvx512,
                    .total_ram_bytes = result.Hardware.TotalRamBytes,
                    .available_ram_bytes = result.Hardware.AvailableRamBytes,
                    .free_disk_bytes = result.Hardware.FreeDiskBytes,
                    .os_name = result.Hardware.OsName,
                    .ram_budget_bytes = result.Hardware.RamBudgetBytes,
                    .budget_notes = result.Hardware.BudgetNotes,
                    .gpus = result.Hardware.Gpus.Select(Function(g) New With {
                        .name = g.Name,
                        .vendor = g.Vendor,
                        .vram_bytes = g.VramBytes,
                        .usable_vram_bytes = g.UsableVramBytes,
                        .compute_capability = g.ComputeCapability,
                        .runtime_version = g.RuntimeVersion,
                        .memory_bandwidth_gbps = g.MemoryBandwidthGbps,
                        .is_shared_memory = g.IsSharedMemory
                    })
                },
                .models = result.Models.Select(Function(row) New With {
                    .rank = row.Rank,
                    .repo_id = row.Model.RepoId,
                    .display_name = row.Model.DisplayName,
                    .use_case = row.UseCase,
                    .quantization = row.SelectedVariant.Quantization,
                    .score = row.Score,
                    .fit_type = row.FitType,
                    .vram_required_bytes = row.VramRequiredBytes,
                    .vram_available_bytes = row.VramAvailableBytes,
                    .uses_multi_gpu = row.UsesMultiGpu,
                    .multi_gpu_effective_vram_bytes = row.MultiGpuEffectiveVramBytes,
                    .estimated_tok_per_sec = row.EstimatedTokPerSec,
                    .speed_confidence = row.SpeedConfidence,
                    .speed_range_tok_per_sec = New Double?() {row.SpeedRangeLowTokPerSec, row.SpeedRangeHighTokPerSec},
                    .speed_notes = row.SpeedNotes,
                    .benchmark_status = row.Benchmark.Status,
                    .benchmark_source = row.Benchmark.Source,
                    .benchmark_confidence = row.Benchmark.Confidence,
                    .license = row.Model.License,
                    .downloads = row.Model.Downloads,
                    .likes = row.Model.Likes,
                    .published_date = row.Model.PublishedDate
                }),
                .warnings = result.Warnings
            }

            Return JsonSerializer.Serialize(payload, New JsonSerializerOptions With {.WriteIndented = True})
        End Function

        Private Shared Function EscapePipe(value As String) As String
            Return value.Replace("|", "\|")
        End Function
    End Class
End Namespace
