Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Engine
Imports WhichLlm.Core.Services
Imports WhichLlm.Core.Utilities

Namespace WhichLlm.Tests
    <TestClass>
    Public Class CoreTests
        <TestMethod>
        Public Sub ParseContextLengthSupportsKShorthand()
            Assert.AreEqual(64000, InputParsers.ParseContextLength("64k"))
            Assert.AreEqual(4096, InputParsers.ParseContextLength("", 4096))
        End Sub

        <TestMethod>
        Public Sub ParseBytesSupportsGbAndPercent()
            Assert.AreEqual(8L * 1000 * 1000 * 1000, InputParsers.ParseBytes("8GB"))
            Assert.AreEqual(4L * 1024 * 1024 * 1024, InputParsers.ParseBytes("50%", 8L * 1024 * 1024 * 1024))
        End Sub

        <TestMethod>
        Public Sub GpuCatalogResolvesCountShorthand()
            Dim catalog As IGpuCatalog = New GpuCatalog()
            Dim gpus = catalog.ResolveMany(New String() {"2x RTX 4090"})

            Assert.AreEqual(2, gpus.Count)
            Assert.AreEqual("NVIDIA", gpus(0).Vendor)
            Assert.IsTrue(gpus(0).VramBytes > 20L * 1024 * 1024 * 1024)
        End Sub

        <TestMethod>
        Public Sub GpuCatalogResolvesRadeonRx6900Xt()
            Dim catalog As IGpuCatalog = New GpuCatalog()
            Dim gpu = catalog.Resolve("AMD Radeon RX 6900 XT")

            Assert.AreEqual("AMD", gpu.Vendor)
            Assert.IsTrue(gpu.VramBytes >= 16L * 1024 * 1024 * 1024)
            Assert.IsTrue(gpu.MemoryBandwidthGbps.HasValue)
        End Sub

        <TestMethod>
        Public Sub GpuCatalogIncludesOlderRtxAndVega20OrNewerRadeonCards()
            Dim catalog As IGpuCatalog = New GpuCatalog()

            Dim rtx2060Super = catalog.Resolve("GeForce RTX 2060 SUPER")
            Dim rtx2080Ti = catalog.Resolve("RTX 2080 Ti")
            Dim radeonVII = catalog.Resolve("AMD Radeon VII")
            Dim rx5700Xt = catalog.Resolve("Radeon RX 5700 XT")

            Assert.AreEqual("NVIDIA", rtx2060Super.Vendor)
            Assert.AreEqual("8.0 GB", Formatters.FormatBytes(rtx2060Super.VramBytes))
            Assert.AreEqual(448.0R, rtx2060Super.MemoryBandwidthGbps.Value)
            Assert.AreEqual("11.0 GB", Formatters.FormatBytes(rtx2080Ti.VramBytes))
            Assert.AreEqual("AMD", radeonVII.Vendor)
            Assert.AreEqual("16.0 GB", Formatters.FormatBytes(radeonVII.VramBytes))
            Assert.AreEqual(1024.0R, radeonVII.MemoryBandwidthGbps.Value)
            Assert.AreEqual("8.0 GB", Formatters.FormatBytes(rx5700Xt.VramBytes))
            CollectionAssert.Contains(catalog.CommonGpuNames().ToList(), "RTX 2080 Ti")
            CollectionAssert.Contains(catalog.CommonGpuNames().ToList(), "Radeon VII")
        End Sub

        <TestMethod>
        Public Sub GpuCatalogDoesNotGuessAppleForAnyNameContainingM()
            Dim catalog As IGpuCatalog = New GpuCatalog()
            Dim gpu = catalog.Resolve("Some Memory Accelerator")

            Assert.AreEqual("Unknown", gpu.Vendor)
        End Sub

        <TestMethod>
        Public Sub HipInfoParserReadsRadeonVramAndArchitecture()
            Dim sample = String.Join(Environment.NewLine, New String() {
                "device#                           0",
                "Name:                             AMD Radeon RX 6900 XT",
                "totalGlobalMem:                   15.98 GB",
                "memInfo.total:                    15.98 GB",
                "memInfo.free:                     15.85 GB (99%)",
                "gcnArchName:                      gfx1030"
            })

            Dim gpus = WindowsHardwareDetector.ParseHipInfoOutput(sample, New GpuCatalog())

            Assert.AreEqual(1, gpus.Count)
            Assert.AreEqual("AMD Radeon RX 6900 XT", gpus(0).Name)
            Assert.AreEqual("AMD", gpus(0).Vendor)
            Assert.AreEqual("16.0 GB", Formatters.FormatBytes(gpus(0).VramBytes))
            Assert.AreEqual("gfx1030", gpus(0).ComputeCapability)
            Assert.AreEqual("hipInfo", gpus(0).RuntimeVersion)
        End Sub

        <TestMethod>
        Public Sub VendorProbeSkipsNvidiaSmiForRadeon()
            Dim radeonNames = New String() {"AMD Radeon RX 6900 XT"}
            Dim nvidiaNames = New String() {"NVIDIA GeForce RTX 4090"}
            Dim intelNames = New String() {"Intel Arc A770 Graphics"}

            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeNvidia(radeonNames))
            Assert.IsTrue(WindowsHardwareDetector.ShouldProbeHipInfo(radeonNames, False))
            Assert.IsTrue(WindowsHardwareDetector.ShouldProbeNvidia(nvidiaNames))
            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeHipInfo(nvidiaNames, False))
            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeXpuSmi(radeonNames, False))
            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeNvidia(intelNames))
            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeHipInfo(intelNames, False))
            Assert.IsTrue(WindowsHardwareDetector.ShouldProbeXpuSmi(intelNames, False))
            Assert.IsTrue(WindowsHardwareDetector.ShouldProbeNvidia(Array.Empty(Of String)()))
            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeHipInfo(Array.Empty(Of String)(), False))
            Assert.IsFalse(WindowsHardwareDetector.ShouldProbeXpuSmi(Array.Empty(Of String)(), False))
        End Sub

        <TestMethod>
        Public Sub XpuSmiParserReadsIntelArcVram()
            Dim sample = "{""device_list"":[{""device_id"":0,""device_name"":""Intel Arc A770 Graphics"",""memory_physical_size"":""16 GB""}]}"

            Dim gpus = WindowsHardwareDetector.ParseXpuSmiOutput(sample, New GpuCatalog())

            Assert.AreEqual(1, gpus.Count)
            Assert.AreEqual("Intel Arc A770 Graphics", gpus(0).Name)
            Assert.AreEqual("Intel", gpus(0).Vendor)
            Assert.AreEqual("16.0 GB", Formatters.FormatBytes(gpus(0).VramBytes))
            Assert.AreEqual("xpu-smi", gpus(0).RuntimeVersion)
        End Sub

        <TestMethod>
        Public Sub VramEstimatorClassifiesFullGpuFit()
            Dim estimator As IVramEstimator = New VramEstimator()
            Dim model = New ModelInfo With {.RepoId = "test/model-7b", .ParameterCountB = 7}
            Dim modelVariant = New ModelVariant With {.Quantization = "Q4_K_M"}
            Dim hardware = HardwareWithGpu("RTX 4090", 24)
            Dim required = estimator.EstimateRequiredBytes(model, modelVariant, 4096)
            Dim fit = estimator.ClassifyFit(required, hardware, New RankingOptions())

            Assert.AreEqual("full_gpu", fit.FitType)
            Assert.IsTrue(fit.IsRunnable)
        End Sub

        <TestMethod>
        Public Sub SnippetGeneratorCreatesGgufCommand()
            Dim generator As ISnippetGenerator = New SnippetGenerator()
            Dim model = New ModelInfo With {.RepoId = "example/model-GGUF", .ParameterCountB = 7}
            model.Variants.Add(New ModelVariant With {.Quantization = "Q4_K_M", .FileName = "model.Q4_K_M.gguf", .RuntimeKind = "gguf"})

            Dim snippet = generator.Generate(model, "Q4_K_M")

            StringAssert.Contains(snippet.CommandLine, "llama-cpp-python")
            StringAssert.Contains(snippet.Code, "hf_hub_download")
        End Sub

        <TestMethod>
        Public Sub SnippetGeneratorDoesNotTrustRemoteCodeByDefault()
            Dim generator As ISnippetGenerator = New SnippetGenerator()
            Dim model = New ModelInfo With {.RepoId = "example/transformer-model", .ParameterCountB = 7}
            model.Variants.Add(New ModelVariant With {.Quantization = "FP16", .RuntimeKind = "transformers"})

            Dim snippet = generator.Generate(model)

            StringAssert.Contains(snippet.CommandLine, "transformers")
            StringAssert.Contains(snippet.Code, "trust_remote_code=False")
            Assert.IsFalse(snippet.Code.Contains("trust_remote_code=True", StringComparison.Ordinal))
        End Sub

        <TestMethod>
        Public Async Function RankingUsesCodingUseCase() As Task
            Dim ranker = BuildRanker()
            Dim models = New List(Of ModelInfo) From {
                TestModel("Qwen/Qwen2.5-Coder-7B-Instruct", 7, "coding"),
                TestModel("meta-llama/Llama-3.1-8B-Instruct", 8, "chat")
            }
            Dim options = New RankingOptions With {.Profile = "coding", .UseCase = "coding", .Top = 5}
            Dim result = Await ranker.RankAsync(models, HardwareWithGpu("RTX 4090", 24), SeedBenchmarks(), options)

            Assert.IsTrue(result.Models.Count > 0)
            Assert.AreEqual("coding", result.Models(0).UseCase)
            StringAssert.Contains(result.Models(0).Model.RepoId.ToLowerInvariant(), "coder")
        End Function

        <TestMethod>
        Public Async Function RankingFiltersEmbeddingUseCase() As Task
            Dim ranker = BuildRanker()
            Dim models = New List(Of ModelInfo) From {
                TestModel("BAAI/bge-small-en-v1.5", 0.3, "embedding"),
                TestModel("meta-llama/Llama-3.1-8B-Instruct", 8, "chat")
            }
            Dim options = New RankingOptions With {.Profile = "any", .UseCase = "embedding", .Top = 5}
            Dim result = Await ranker.RankAsync(models, HardwareWithGpu("RTX 4090", 24), SeedBenchmarks(), options)

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("embedding", result.Models(0).UseCase)
        End Function

        <TestMethod>
        Public Async Function PlanPrefersPracticalModelAndBalancedGpuSuggestions() As Task
            Dim models = New List(Of ModelInfo) From {
                TestModel("Qwen/Qwen3-0.6B", 0.6, "chat"),
                TestModel("Qwen/Qwen3-4B", 4, "chat")
            }
            Dim service = BuildApplicationService(models)

            Dim result = Await service.PlanAsync("qwen", "Q4_K_M", 4096, New RankingOptions())
            Dim recommendations = result.Rows(0).RecommendedGpus

            Assert.AreEqual("Qwen/Qwen3-4B", result.MatchedModel.RepoId)
            Assert.IsTrue(recommendations.Count > 0)
            Assert.AreNotEqual("RTX 5090", recommendations(0).Name)
            Assert.IsTrue(recommendations.Any(Function(g) g.Vendor = "AMD"))
        End Function

        <TestMethod>
        Public Async Function ModelSuggestionsUseFetchedModels() As Task
            Dim models = New List(Of ModelInfo) From {
                TestModel("unknown/tiny-270m", 0.27, "chat"),
                TestModel("Qwen/Qwen3-4B", 4, "chat"),
                TestModel("google/gemma-3-12b-it", 12, "chat")
            }
            Dim service = BuildApplicationService(models)

            Dim suggestions = Await service.LoadModelSuggestionsAsync(New RankingOptions With {.UseCase = "chat"})

            Assert.AreEqual(3, suggestions.Count)
            Assert.AreNotEqual("unknown/tiny-270m", suggestions(0).RepoId)
            Assert.IsTrue(suggestions.Any(Function(model) model.RepoId = "Qwen/Qwen3-4B"))
            Assert.IsTrue(suggestions.Any(Function(model) model.RepoId = "google/gemma-3-12b-it"))
        End Function

        Private Shared Function BuildRanker() As IRanker
            Dim vram As IVramEstimator = New VramEstimator()
            Dim speed As IPerformanceEstimator = New PerformanceEstimator()
            Dim grouper As IModelGrouper = New ModelGrouper()
            Return New Ranker(vram, speed, grouper)
        End Function

        Private Shared Function BuildApplicationService(models As List(Of ModelInfo)) As WhichLlmApplicationService
            Dim gpuCatalog As IGpuCatalog = New GpuCatalog()
            Dim vram As IVramEstimator = New VramEstimator()
            Return New WhichLlmApplicationService(
                New FakeHardwareDetector(HardwareWithGpu("RTX 3060", 12)),
                New FakeModelFetcher(models),
                New FakeBenchmarkProvider(),
                BuildRanker(),
                vram,
                gpuCatalog,
                New SnippetGenerator())
        End Function

        Private Shared Function TestModel(repoId As String, paramsB As Double, useCase As String) As ModelInfo
            Dim model = New ModelInfo With {
                .RepoId = repoId,
                .DisplayName = repoId.Split("/"c).Last(),
                .ParameterCountB = paramsB,
                .UseCase = useCase,
                .Downloads = 10000,
                .Likes = 100,
                .ContextLength = 8192
            }
            model.Variants.Add(New ModelVariant With {.Quantization = "Q4_K_M", .RuntimeKind = "gguf", .IsSynthetic = True})
            Return model
        End Function

        Private Shared Function HardwareWithGpu(name As String, vramGb As Double) As HardwareInfo
            Return New HardwareInfo With {
                .CpuName = "Test CPU",
                .PhysicalCores = 8,
                .SupportsAvx2 = True,
                .TotalRamBytes = 64L * 1024 * 1024 * 1024,
                .AvailableRamBytes = 48L * 1024 * 1024 * 1024,
                .RamBudgetBytes = 48L * 1024 * 1024 * 1024,
                .FreeDiskBytes = 500L * 1024 * 1024 * 1024,
                .Gpus = New List(Of GpuInfo) From {
                    New GpuInfo With {
                        .Name = name,
                        .Vendor = "NVIDIA",
                        .VramBytes = CLng(vramGb * 1024 * 1024 * 1024),
                        .UsableVramBytes = CLng(vramGb * 1024 * 1024 * 1024),
                        .MemoryBandwidthGbps = 1000
                    }
                }
            }
        End Function

        Private Shared Function SeedBenchmarks() As Dictionary(Of String, BenchmarkEvidence)
            Return New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase) From {
                {"qwen-qwen2-5-coder-7b-instruct", New BenchmarkEvidence With {.Source = "direct", .Score = 78, .Confidence = 1.0R}},
                {"meta-llama-llama-3-1-8b-instruct", New BenchmarkEvidence With {.Source = "direct", .Score = 76, .Confidence = 1.0R}},
                {"baai-bge-small-en-v1-5", New BenchmarkEvidence With {.Source = "direct", .Score = 70, .Confidence = 1.0R}}
            }
        End Function

        Private Class FakeHardwareDetector
            Implements IHardwareDetector

            Private ReadOnly _hardware As HardwareInfo

            Public Sub New(hardware As HardwareInfo)
                _hardware = hardware
            End Sub

            Public Function DetectAsync(options As RankingOptions, Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of HardwareInfo) Implements IHardwareDetector.DetectAsync
                Return Task.FromResult(_hardware.Clone())
            End Function
        End Class

        Private Class FakeModelFetcher
            Implements IModelFetcher

            Private ReadOnly _models As List(Of ModelInfo)

            Public Sub New(models As List(Of ModelInfo))
                _models = models
            End Sub

            Public Function LoadModelsAsync(options As RankingOptions, Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IModelFetcher.LoadModelsAsync
                Return Task.FromResult(_models)
            End Function
        End Class

        Private Class FakeBenchmarkProvider
            Implements IBenchmarkProvider

            Public Function LoadBenchmarksAsync(refresh As Boolean, Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence)) Implements IBenchmarkProvider.LoadBenchmarksAsync
                Return Task.FromResult(New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase))
            End Function
        End Class
    End Class
End Namespace
