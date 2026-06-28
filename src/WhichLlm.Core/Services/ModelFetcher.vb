Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto

Namespace Services
    Public Class ModelFetcher
        Implements IModelFetcher

        Private ReadOnly _cache As IModelCache
        Private ReadOnly _client As IHuggingFaceClient

        Public Sub New(cache As IModelCache, client As IHuggingFaceClient)
            _cache = cache
            _client = client
        End Sub

        Public Async Function LoadModelsAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IModelFetcher.LoadModelsAsync
            If Not options.Refresh AndAlso _cache.IsFresh() Then
                Dim cached = Await _cache.LoadAsync(cancellationToken)
                If cached.Count > 0 Then Return cached
            End If

            Dim fetchError As Exception = Nothing
            Try
                Dim fetched = Await _client.FetchModelsAsync(options.Profile, options.UseCase, options.Refresh, cancellationToken)
                If fetched.Count > 0 Then
                    Await _cache.SaveAsync(fetched, cancellationToken)
                    Return fetched
                End If
            Catch ex As Exception
                fetchError = ex
            End Try

            Dim stale = Await _cache.LoadAsync(cancellationToken)
            If stale.Count > 0 Then Return stale
            Return BuildFallbackModels()
        End Function

        Friend Shared Function BuildFallbackModels() As List(Of ModelInfo)
            Return New List(Of ModelInfo) From {
                FallbackModel("Qwen/Qwen3-4B", 4, "chat", 8192, 3588509, 417),
                FallbackModel("Qwen/Qwen3-8B", 8, "chat", 8192, 2200000, 350),
                FallbackModel("Qwen/Qwen3-14B", 14, "chat", 8192, 1600000, 320),
                FallbackModel("Qwen/Qwen2.5-Coder-7B-Instruct", 7, "coding", 32768, 1200000, 260),
                FallbackModel("google/gemma-3-4b-it", 4, "chat", 8192, 900000, 210),
                FallbackModel("google/gemma-3-12b-it", 12, "chat", 8192, 820000, 190),
                FallbackModel("meta-llama/Llama-3.1-8B-Instruct", 8, "chat", 8192, 1500000, 400),
                FallbackModel("mistralai/Mistral-7B-Instruct-v0.3", 7, "chat", 32768, 900000, 260),
                FallbackModel("microsoft/Phi-4-mini-instruct", 3.8, "chat", 8192, 780000, 180),
                FallbackModel("deepseek-ai/DeepSeek-R1-Distill-Qwen-7B", 7, "reasoning", 32768, 700000, 160),
                FallbackModel("BAAI/bge-small-en-v1.5", 0.3, "embedding", 512, 5000000, 500)
            }
        End Function

        Private Shared Function FallbackModel(repoId As String, paramsB As Double, useCase As String, contextLength As Integer, downloads As Long, likes As Long) As ModelInfo
            Dim model = New ModelInfo With {
                .RepoId = repoId,
                .DisplayName = repoId.Split("/"c).Last(),
                .ParameterCountB = paramsB,
                .UseCase = useCase,
                .ContextLength = contextLength,
                .Downloads = downloads,
                .Likes = likes,
                .PipelineTag = If(useCase = "embedding", "sentence-similarity", "text-generation"),
                .Tags = New List(Of String) From {"whichllm-gui-fallback"}
            }
            If useCase = "embedding" Then
                model.Variants.Add(New ModelVariant With {.Quantization = "FP16", .RuntimeKind = "transformers", .IsSynthetic = True})
            Else
                For Each quant In New String() {"Q4_K_M", "Q5_K_M", "Q8_0"}
                    model.Variants.Add(New ModelVariant With {.Quantization = quant, .RuntimeKind = "gguf", .IsSynthetic = True})
                Next
            End If
            Return model
        End Function
    End Class
End Namespace
