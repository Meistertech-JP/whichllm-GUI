Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.Net
Imports System.Net.Http
Imports System.Text
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
            Dim radeonProVII = catalog.Resolve("Radeon Pro VII")
            Dim mi60 = catalog.Resolve("MI60")
            Dim rx5700Xt = catalog.Resolve("Radeon RX 5700 XT")
            Dim rxVega64 = catalog.Resolve("Vega 64")

            Assert.AreEqual("NVIDIA", rtx2060Super.Vendor)
            Assert.AreEqual("8.0 GB", Formatters.FormatBytes(rtx2060Super.VramBytes))
            Assert.AreEqual(448.0R, rtx2060Super.MemoryBandwidthGbps.Value)
            Assert.AreEqual("11.0 GB", Formatters.FormatBytes(rtx2080Ti.VramBytes))
            Assert.AreEqual("AMD", radeonVII.Vendor)
            Assert.AreEqual("16.0 GB", Formatters.FormatBytes(radeonVII.VramBytes))
            Assert.AreEqual(1024.0R, radeonVII.MemoryBandwidthGbps.Value)
            Assert.AreEqual("16.0 GB", Formatters.FormatBytes(radeonProVII.VramBytes))
            Assert.AreEqual("32.0 GB", Formatters.FormatBytes(mi60.VramBytes))
            Assert.AreEqual("8.0 GB", Formatters.FormatBytes(rx5700Xt.VramBytes))
            Assert.AreEqual("8.0 GB", Formatters.FormatBytes(rxVega64.VramBytes))
            CollectionAssert.Contains(catalog.CommonGpuNames().ToList(), "RTX 2080 Ti")
            CollectionAssert.Contains(catalog.CommonGpuNames().ToList(), "Radeon VII")
            CollectionAssert.Contains(catalog.CommonGpuNames().ToList(), "Radeon Pro VII")
            CollectionAssert.Contains(catalog.CommonGpuNames().ToList(), "Radeon Instinct MI60")
        End Sub

        <TestMethod>
        Public Sub GpuCatalogCommonNamesAreGroupedByGeneration()
            Dim catalog As IGpuCatalog = New GpuCatalog()
            Dim names = catalog.CommonGpuNames().ToList()

            Assert.IsTrue(names.IndexOf("RX 7600") < names.IndexOf("RX 6950 XT"))
            Assert.IsTrue(names.IndexOf("RX 5500 XT 4GB") < names.IndexOf("Radeon VII"))
            Assert.IsTrue(names.IndexOf("Radeon Instinct MI50") < names.IndexOf("RX Vega 64"))
            Assert.IsTrue(names.IndexOf("Intel Arc B580") < names.IndexOf("Intel Arc A770"))
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
        Public Sub VramEstimatorUsesArchitectureKvCacheForLongContext()
            Dim estimator As IVramEstimator = New VramEstimator()
            Dim model = New ModelInfo With {.RepoId = "meta-llama/Llama-3.1-8B-Instruct", .ParameterCountB = 8}
            Dim modelVariant = New ModelVariant With {.Quantization = "Q4_K_M"}

            Dim kv4k = VramEstimator.EstimateKvCacheBytes(model, 4096)
            Dim kv128k = VramEstimator.EstimateKvCacheBytes(model, 128000)
            Dim required128k = estimator.EstimateRequiredBytes(model, modelVariant, 128000)
            Dim fit = estimator.ClassifyFit(required128k, HardwareWithGpu("RX 6900 XT", 16), New RankingOptions())

            Assert.AreEqual(512L * 1024 * 1024, kv4k)
            Assert.IsTrue(kv128k > 15L * 1024 * 1024 * 1024)
            Assert.IsTrue(required128k > 20L * 1024 * 1024 * 1024)
            Assert.AreNotEqual("full_gpu", fit.FitType)
            Assert.AreEqual("partial_offload", fit.FitType)
        End Sub

        <TestMethod>
        Public Sub VramEstimatorDoesNotUseMoeActiveParamsForKvCache()
            Dim model = New ModelInfo With {
                .RepoId = "deepseek-ai/DeepSeek-V3",
                .ParameterCountB = 671,
                .ActiveParameterCountB = 37
            }

            Dim kv64k = VramEstimator.EstimateKvCacheBytes(model, 65536)

            Assert.IsTrue(kv64k > 48L * 1024 * 1024 * 1024)
        End Sub

        <TestMethod>
        Public Sub VramEstimatorAddsCompatibilityWarningsForOldNvidiaComputeCapability()
            Dim estimator As IVramEstimator = New VramEstimator()
            Dim hardware = HardwareWithGpu("GTX 780", 3)
            hardware.OsName = "Windows 11"
            hardware.Gpus(0).ComputeCapability = "3.5"
            Dim required = 1L * 1024 * 1024 * 1024

            Dim fit = estimator.ClassifyFit(required, hardware, New RankingOptions())

            Assert.IsTrue(fit.Notes.Any(Function(note) note.Contains("Compute capability", StringComparison.Ordinal)))
            Assert.IsTrue(fit.Notes.Any(Function(note) note.Contains("Legacy Kepler", StringComparison.Ordinal)))
        End Sub

        <TestMethod>
        Public Sub QuantizationRulesTreatsQatAsKnownQuant()
            Assert.IsTrue(QuantizationRules.IsQat("QAT"))
            Assert.AreEqual(0.57R, QuantizationRules.BytesPerParam("QAT"), 0.0001R)
            Assert.AreEqual(0.05R, QuantizationRules.QuantPenalty("QAT"), 0.0001R)
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
        Public Async Function RankingDoesNotSynthesizeQatForPlainModels() As Task
            Dim ranker = BuildRanker()
            Dim models = New List(Of ModelInfo) From {
                TestModel("Qwen/Qwen3-14B", 14, "chat"),
                TestModel("Qwen/Qwen3-8B", 8, "chat")
            }
            Dim options = New RankingOptions With {.Profile = "general", .UseCase = "general", .Quant = "QAT", .Evidence = "any", .Top = 5}

            Dim result = Await ranker.RankAsync(models, HardwareWithGpu("RTX 4090", 24), SeedBenchmarks(), options)

            Assert.AreEqual(0, result.Models.Count)
        End Function

        <TestMethod>
        Public Async Function RankingAllowsQatWhenQatVariantExists() As Task
            Dim ranker = BuildRanker()
            Dim model = TestModel("unsloth/gemma-4-26B-A4B-it-qat-GGUF", 26, "chat")
            model.Variants.Clear()
            model.Variants.Add(New ModelVariant With {.Quantization = "QAT", .FileName = "gemma-4-26B-A4B-it-qat-UD-Q4_K_XL.gguf", .RuntimeKind = "gguf"})
            Dim options = New RankingOptions With {.Profile = "general", .UseCase = "general", .Quant = "QAT", .Evidence = "any", .Top = 5}

            Dim result = Await ranker.RankAsync(New List(Of ModelInfo) From {model}, HardwareWithGpu("RTX 4090", 24), SeedBenchmarks(), options)

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("QAT", result.Models(0).SelectedVariant.Quantization)
        End Function

        <TestMethod>
        Public Async Function RankingStillSynthesizesOrdinaryGgufQuantForBaseModels() As Task
            Dim ranker = BuildRanker()
            Dim model = TestModel("Qwen/Qwen3-8B", 8, "chat")
            model.Variants.Clear()
            Dim options = New RankingOptions With {.Profile = "general", .UseCase = "general", .Quant = "Q5_0", .Evidence = "any", .Top = 5}

            Dim result = Await ranker.RankAsync(New List(Of ModelInfo) From {model}, HardwareWithGpu("RTX 4090", 24), SeedBenchmarks(), options)

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("Q5_0", result.Models(0).SelectedVariant.Quantization)
            Assert.IsTrue(result.Models(0).SelectedVariant.IsSynthetic)
        End Function

        <TestMethod>
        Public Async Function RankingResolvesVariantBenchmarkEvidence() As Task
            Dim ranker = BuildRanker()
            Dim model = TestModel("Qwen/Qwen3-8B-GGUF", 8, "chat")
            Dim benchmarks = New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase) From {
                {"Qwen/Qwen3-8B", New BenchmarkEvidence With {.Source = "direct", .Score = 70, .Confidence = 1.0R}}
            }

            Dim result = Await ranker.RankAsync(New List(Of ModelInfo) From {model}, HardwareWithGpu("RTX 4090", 24), benchmarks, New RankingOptions With {.Evidence = "any"})

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("variant", result.Models(0).Benchmark.Source)
            Assert.AreEqual(0.55R, result.Models(0).Benchmark.Confidence, 0.001R)
        End Function

        <TestMethod>
        Public Async Function RankingResolvesBaseModelBenchmarkEvidence() As Task
            Dim ranker = BuildRanker()
            Dim model = TestModel("some-org/custom-qwen3-8b", 8, "chat")
            model.BaseModel = "Qwen/Qwen3-8B"
            Dim benchmarks = New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase) From {
                {"Qwen/Qwen3-8B", New BenchmarkEvidence With {.Source = "direct", .Score = 70, .Confidence = 1.0R}}
            }

            Dim result = Await ranker.RankAsync(New List(Of ModelInfo) From {model}, HardwareWithGpu("RTX 4090", 24), benchmarks, New RankingOptions With {.Evidence = "any"})

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("base_model", result.Models(0).Benchmark.Source)
            Assert.AreEqual(0.6R, result.Models(0).Benchmark.Confidence, 0.001R)
        End Function

        <TestMethod>
        Public Async Function RankingGeneratesLineInterpolationBenchmarkEvidence() As Task
            Dim ranker = BuildRanker()
            Dim model = TestModel("Qwen/Qwen3-10B", 10, "chat")
            Dim benchmarks = New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase) From {
                {"Qwen/Qwen3-8B", New BenchmarkEvidence With {.Source = "direct", .Score = 60, .Confidence = 1.0R}},
                {"Qwen/Qwen3-14B", New BenchmarkEvidence With {.Source = "direct", .Score = 70, .Confidence = 1.0R}}
            }

            Dim result = Await ranker.RankAsync(New List(Of ModelInfo) From {model}, HardwareWithGpu("RTX 4090", 24), benchmarks, New RankingOptions With {.Evidence = "base"})

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("line_interp", result.Models(0).Benchmark.Source)
            Assert.IsTrue(result.Models(0).Benchmark.Score > 60)
            Assert.IsTrue(result.Models(0).Benchmark.Confidence > 0)
        End Function

        <TestMethod>
        Public Async Function RankingRejectsBenchmarkInheritanceWhenParametersDifferByMoreThanTwoX() As Task
            Dim ranker = BuildRanker()
            Dim model = TestModel("community/qwen3-mtp-draft-6b", 6, "chat")
            model.BaseModel = "Qwen/Qwen3-158B-A22B"
            Dim benchmarks = New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase) From {
                {"Qwen/Qwen3-158B-A22B", New BenchmarkEvidence With {.Source = "direct", .Score = 95, .Confidence = 1.0R}}
            }

            Dim result = Await ranker.RankAsync(New List(Of ModelInfo) From {model}, HardwareWithGpu("RTX 4090", 24), benchmarks, New RankingOptions With {.Evidence = "any"})

            Assert.AreEqual(1, result.Models.Count)
            Assert.AreEqual("none", result.Models(0).Benchmark.Source)
            Assert.AreEqual(0, result.Models(0).Benchmark.Confidence)
        End Function

        <TestMethod>
        Public Sub ModelGrouperDoesNotGroupMtpDraftWithLargeBaseModel()
            Dim grouper As IModelGrouper = New ModelGrouper()
            Dim model = TestModel("community/qwen3-mtp-draft-6b", 6, "chat")
            model.BaseModel = "Qwen/Qwen3-158B-A22B"

            Dim key = grouper.FamilyKey(model)

            Assert.AreEqual(Formatters.NormalizeModelName(model.RepoId), key)
        End Sub

        <TestMethod>
        Public Async Function BenchmarkProviderKeepsCurrentFallbacksWhenLiveHttpSourcesFail() As Task
            Dim cache = New FakeBenchmarkCache(False)
            Dim client = New HttpClient(New StaticHttpHandler(HttpStatusCode.InternalServerError, ""))
            Dim provider As IBenchmarkProvider = New BenchmarkProvider(cache, client)

            Dim benchmarks = Await provider.LoadBenchmarksAsync(True)

            Assert.IsTrue(benchmarks.Count > 0)
            Assert.IsTrue(benchmarks.Values.Any(Function(e) e.BenchmarkTier = "current"))
        End Function

        <TestMethod>
        Public Async Function BenchmarkProviderRefreshesOldUntieredCacheSchema() As Task
            Dim cache = New FakeBenchmarkCache(True)
            cache.SetValue(New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase) From {
                {"qwen3", New BenchmarkEvidence With {.Source = "direct", .Score = 87, .Confidence = 1.0R}}
            })
            Dim client = New HttpClient(New StaticHttpHandler(HttpStatusCode.InternalServerError, ""))
            Dim provider As IBenchmarkProvider = New BenchmarkProvider(cache, client)

            Dim benchmarks = Await provider.LoadBenchmarksAsync(False)

            Assert.IsTrue(benchmarks.Values.Any(Function(e) Not String.IsNullOrWhiteSpace(e.BenchmarkTier)))
            Assert.IsFalse(benchmarks.ContainsKey("qwen3"))
        End Function


        <TestMethod>
        Public Async Function HuggingFaceClientFetchesRecentlyUpdatedAndTrendingModels() As Task
            Dim handler = New RecordingHttpHandler("[]")
            Dim client = New HuggingFaceClient(New HttpClient(handler))

            Await client.FetchModelsAsync("general", "general", True)

            Assert.IsTrue(handler.RequestedUrls.Any(Function(url) url.Contains("sort=lastModified", StringComparison.Ordinal)))
            Assert.IsTrue(handler.RequestedUrls.Any(Function(url) url.Contains("sort=trending", StringComparison.Ordinal)))
        End Function

        <TestMethod>
        Public Async Function ModelFetcherUsesFallbackModelsWhenCacheAndNetworkAreUnavailable() As Task
            Dim fetcher As IModelFetcher = New ModelFetcher(New FakeModelCache(False), New FailingHuggingFaceClient())

            Dim models = Await fetcher.LoadModelsAsync(New RankingOptions())

            Assert.IsTrue(models.Count > 0)
            Assert.IsTrue(models.Any(Function(model) model.Tags.Contains("whichllm-gui-fallback")))
            Assert.IsTrue(models.Any(Function(model) model.RepoId = "Qwen/Qwen3-8B"))
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
        Public Async Function PlanUsesQatForQatModelsWhenQuantBlank() As Task
            Dim qatModel = TestModel("unsloth/gemma-4-26B-A4B-it-qat-GGUF", 26, "chat")
            qatModel.Variants.Clear()
            qatModel.Variants.Add(New ModelVariant With {.Quantization = "QAT", .RuntimeKind = "gguf", .IsSynthetic = True})
            Dim service = BuildApplicationService(New List(Of ModelInfo) From {qatModel})

            Dim result = Await service.PlanAsync("gemma 4 26b qat", "", 4096, New RankingOptions())

            Assert.AreEqual("unsloth/gemma-4-26B-A4B-it-qat-GGUF", result.MatchedModel.RepoId)
            Assert.AreEqual(1, result.Rows.Count)
            Assert.AreEqual("QAT", result.Rows(0).Quantization)
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

        Private Class FakeModelCache
            Implements IModelCache

            Private ReadOnly _fresh As Boolean
            Private _models As New List(Of ModelInfo)

            Public Sub New(fresh As Boolean)
                _fresh = fresh
            End Sub

            Public Function LoadAsync(Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IModelCache.LoadAsync
                Return Task.FromResult(_models)
            End Function

            Public Function SaveAsync(models As IEnumerable(Of ModelInfo), Optional cancellationToken As Threading.CancellationToken = Nothing) As Task Implements IModelCache.SaveAsync
                _models = models.ToList()
                Return Task.CompletedTask
            End Function

            Public Function IsFresh() As Boolean Implements IModelCache.IsFresh
                Return _fresh
            End Function
        End Class

        Private Class FailingHuggingFaceClient
            Implements IHuggingFaceClient

            Public Function FetchModelsAsync(profile As String, useCase As String, refresh As Boolean, Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IHuggingFaceClient.FetchModelsAsync
                Throw New HttpRequestException("offline")
            End Function
        End Class

        Private Class FakeBenchmarkProvider
            Implements IBenchmarkProvider

            Public Function LoadBenchmarksAsync(refresh As Boolean, Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence)) Implements IBenchmarkProvider.LoadBenchmarksAsync
                Return Task.FromResult(New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase))
            End Function
        End Class

        Private Class FakeBenchmarkCache
            Implements IBenchmarkCache

            Private ReadOnly _fresh As Boolean
            Private _value As Dictionary(Of String, BenchmarkEvidence) = New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)

            Public Sub New(fresh As Boolean)
                _fresh = fresh
            End Sub

            Public Function LoadAsync(Optional cancellationToken As Threading.CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence)) Implements IBenchmarkCache.LoadAsync
                Return Task.FromResult(_value)
            End Function

            Public Function SaveAsync(evidence As Dictionary(Of String, BenchmarkEvidence), Optional cancellationToken As Threading.CancellationToken = Nothing) As Task Implements IBenchmarkCache.SaveAsync
                _value = evidence
                Return Task.CompletedTask
            End Function

            Public Function IsFresh() As Boolean Implements IBenchmarkCache.IsFresh
                Return _fresh
            End Function

            Public Sub SetValue(value As Dictionary(Of String, BenchmarkEvidence))
                _value = value
            End Sub
        End Class

        Private Class StaticHttpHandler
            Inherits HttpMessageHandler

            Private ReadOnly _statusCode As HttpStatusCode
            Private ReadOnly _content As String

            Public Sub New(statusCode As HttpStatusCode, content As String)
                _statusCode = statusCode
                _content = content
            End Sub

            Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As Threading.CancellationToken) As Task(Of HttpResponseMessage)
                Return Task.FromResult(New HttpResponseMessage(_statusCode) With {
                    .Content = New StringContent(_content, Encoding.UTF8, "application/json")
                })
            End Function
        End Class

        Private Class RecordingHttpHandler
            Inherits HttpMessageHandler

            Private ReadOnly _content As String

            Public Sub New(content As String)
                _content = content
                RequestedUrls = New List(Of String)()
            End Sub

            Public ReadOnly Property RequestedUrls As List(Of String)

            Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As Threading.CancellationToken) As Task(Of HttpResponseMessage)
                RequestedUrls.Add(request.RequestUri.ToString())
                Return Task.FromResult(New HttpResponseMessage(HttpStatusCode.OK) With {
                    .Content = New StringContent(_content, Encoding.UTF8, "application/json")
                })
            End Function
        End Class
    End Class
End Namespace
