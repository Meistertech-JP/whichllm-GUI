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

        Private Shared Function BuildRanker() As IRanker
            Dim vram As IVramEstimator = New VramEstimator()
            Dim speed As IPerformanceEstimator = New PerformanceEstimator()
            Dim grouper As IModelGrouper = New ModelGrouper()
            Return New Ranker(vram, speed, grouper)
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
    End Class
End Namespace
