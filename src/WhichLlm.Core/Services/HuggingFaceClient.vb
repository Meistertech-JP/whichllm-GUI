Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Net.Http
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports WhichLlm.Core.Dto

Namespace Services
    Public Class HuggingFaceClient
        Implements IHuggingFaceClient

        Private ReadOnly _httpClient As HttpClient
        Private ReadOnly _endpoint As String

        Public Sub New(Optional httpClient As HttpClient = Nothing)
            _httpClient = If(httpClient, New HttpClient())
            _httpClient.Timeout = TimeSpan.FromSeconds(40)
            _endpoint = Environment.GetEnvironmentVariable("HF_ENDPOINT")
            If String.IsNullOrWhiteSpace(_endpoint) Then
                _endpoint = "https://huggingface.co"
            End If
            _endpoint = _endpoint.TrimEnd("/"c)
        End Sub

        Public Async Function FetchModelsAsync(profile As String, useCase As String, refresh As Boolean, Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IHuggingFaceClient.FetchModelsAsync
            Dim urls = New List(Of String) From {
                $"{_endpoint}/api/models?pipeline_tag=text-generation&sort=downloads&direction=-1&limit=200&full=true",
                $"{_endpoint}/api/models?search=GGUF&sort=downloads&direction=-1&limit=200&full=true",
                $"{_endpoint}/api/models?search=Qwen%20GGUF&sort=downloads&direction=-1&limit=60&full=true",
                $"{_endpoint}/api/models?search=Llama%20GGUF&sort=downloads&direction=-1&limit=60&full=true",
                $"{_endpoint}/api/models?search=Gemma%20GGUF&sort=downloads&direction=-1&limit=60&full=true",
                $"{_endpoint}/api/models?search=Mistral%20GGUF&sort=downloads&direction=-1&limit=60&full=true",
                $"{_endpoint}/api/models?search=DeepSeek%20GGUF&sort=downloads&direction=-1&limit=60&full=true",
                $"{_endpoint}/api/models?search=Phi%20GGUF&sort=downloads&direction=-1&limit=60&full=true"
            }

            If String.Equals(profile, "vision", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(profile, "any", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(useCase, "multimodal", StringComparison.OrdinalIgnoreCase) Then
                urls.Add($"{_endpoint}/api/models?pipeline_tag=image-text-to-text&sort=downloads&direction=-1&limit=40&full=true")
            End If

            If String.Equals(useCase, "embedding", StringComparison.OrdinalIgnoreCase) Then
                urls.Add($"{_endpoint}/api/models?pipeline_tag=sentence-similarity&sort=downloads&direction=-1&limit=60&full=true")
                urls.Add($"{_endpoint}/api/models?pipeline_tag=feature-extraction&sort=downloads&direction=-1&limit=40&full=true")
            End If

            Dim byId As New Dictionary(Of String, ModelInfo)(StringComparer.OrdinalIgnoreCase)
            Dim lastError As Exception = Nothing
            For Each url In urls
                Try
                    Using response = Await _httpClient.GetAsync(url, cancellationToken)
                        If response.StatusCode = Net.HttpStatusCode.TooManyRequests Then
                            lastError = New HttpRequestException("Hugging Face API rate limit was reached.")
                            Await DelayForRetryAfterAsync(response, cancellationToken)
                            Continue For
                        End If

                        response.EnsureSuccessStatusCode()
                        Using stream = Await response.Content.ReadAsStreamAsync(cancellationToken)
                            Using document = Await JsonDocument.ParseAsync(stream, cancellationToken:=cancellationToken)
                                If document.RootElement.ValueKind <> JsonValueKind.Array Then
                                    Continue For
                                End If
                                For Each item In document.RootElement.EnumerateArray()
                                    Dim model = ParseModel(item)
                                    If model IsNot Nothing AndAlso Not byId.ContainsKey(model.RepoId) Then
                                        byId(model.RepoId) = model
                                    End If
                                Next
                            End Using
                        End Using
                    End Using
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    lastError = ex
                End Try
            Next

            Dim result = byId.Values.Where(Function(m) m.ParameterCountB > 0).ToList()
            If result.Count = 0 AndAlso lastError IsNot Nothing Then Throw lastError
            Return result
        End Function

        Private Shared Async Function DelayForRetryAfterAsync(response As HttpResponseMessage, cancellationToken As CancellationToken) As Task
            Dim delay = response.Headers.RetryAfter?.Delta
            If Not delay.HasValue AndAlso response.Headers.RetryAfter?.Date.HasValue Then
                delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow
            End If

            If delay.HasValue AndAlso delay.Value > TimeSpan.Zero Then
                Await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay.Value.TotalMilliseconds, 3000)), cancellationToken)
            End If
        End Function

        Private Shared Function ParseModel(item As JsonElement) As ModelInfo
            Dim id = ReadString(item, "id")
            If String.IsNullOrWhiteSpace(id) Then
                id = ReadString(item, "modelId")
            End If
            If String.IsNullOrWhiteSpace(id) Then
                Return Nothing
            End If

            Dim tags = ReadStringArray(item, "tags")
            Dim cardData As JsonElement
            Dim hasCard = item.TryGetProperty("cardData", cardData)
            Dim license = If(hasCard, ReadString(cardData, "license"), "")
            Dim baseModel = If(hasCard, ReadBaseModel(cardData), "")
            Dim paramsB = InferParamsB(id, tags, hasCard, cardData)

            Dim model As New ModelInfo With {
                .RepoId = id,
                .DisplayName = id.Split("/"c).Last(),
                .ParameterCountB = paramsB,
                .ActiveParameterCountB = InferActiveParamsB(id, paramsB),
                .Architecture = tags.FirstOrDefault(Function(t) t.StartsWith("transformers:", StringComparison.OrdinalIgnoreCase)),
                .ContextLength = InferContextLength(id, hasCard, cardData),
                .License = license,
                .Downloads = ReadLong(item, "downloads"),
                .Likes = ReadLong(item, "likes"),
                .PublishedDate = ReadDate(item, "createdAt"),
                .BaseModel = baseModel,
                .PipelineTag = ReadString(item, "pipeline_tag"),
                .UseCase = InferUseCase(id, tags, ReadString(item, "pipeline_tag")),
                .Tags = tags,
                .EvalScore = If(hasCard, ReadEvalScore(cardData), Nothing)
            }

            Dim siblings As JsonElement
            If item.TryGetProperty("siblings", siblings) AndAlso siblings.ValueKind = JsonValueKind.Array Then
                For Each sibling In siblings.EnumerateArray()
                    Dim fileName = ReadString(sibling, "rfilename")
                    If fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) Then
                        model.Variants.Add(New ModelVariant With {
                            .FileName = fileName,
                            .Quantization = InferQuantization(fileName),
                            .FileSizeBytes = ReadNullableLong(sibling, "size"),
                            .RuntimeKind = "gguf"
                        })
                    End If
                Next
            End If

            If model.Variants.Count = 0 AndAlso id.Contains("gguf", StringComparison.OrdinalIgnoreCase) Then
                Dim syntheticQuant = If(ContainsQatToken(id), "QAT", "Q4_K_M")
                model.Variants.Add(New ModelVariant With {.Quantization = syntheticQuant, .IsSynthetic = True, .RuntimeKind = "gguf"})
            End If

            Return model
        End Function

        Private Shared Function ReadString(element As JsonElement, propertyName As String) As String
            Dim prop As JsonElement
            If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(propertyName, prop) Then
                If prop.ValueKind = JsonValueKind.String Then Return prop.GetString()
                If prop.ValueKind = JsonValueKind.Number OrElse prop.ValueKind = JsonValueKind.True OrElse prop.ValueKind = JsonValueKind.False Then Return prop.ToString()
            End If
            Return ""
        End Function

        Private Shared Function ReadLong(element As JsonElement, propertyName As String) As Long
            Return If(ReadNullableLong(element, propertyName), 0)
        End Function

        Private Shared Function ReadNullableLong(element As JsonElement, propertyName As String) As Long?
            Dim prop As JsonElement
            If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(propertyName, prop) Then
                If prop.ValueKind = JsonValueKind.Number Then
                    Dim value As Long
                    If prop.TryGetInt64(value) Then Return value
                End If
            End If
            Return Nothing
        End Function

        Private Shared Function ReadStringArray(element As JsonElement, propertyName As String) As List(Of String)
            Dim result As New List(Of String)
            Dim prop As JsonElement
            If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(propertyName, prop) AndAlso prop.ValueKind = JsonValueKind.Array Then
                For Each item In prop.EnumerateArray()
                    If item.ValueKind = JsonValueKind.String Then result.Add(item.GetString())
                Next
            End If
            Return result
        End Function

        Private Shared Function ReadBaseModel(cardData As JsonElement) As String
            Dim prop As JsonElement
            If cardData.ValueKind <> JsonValueKind.Object OrElse Not cardData.TryGetProperty("base_model", prop) Then Return ""
            If prop.ValueKind = JsonValueKind.String Then Return prop.GetString()
            If prop.ValueKind = JsonValueKind.Array Then
                Dim first = prop.EnumerateArray().FirstOrDefault()
                If first.ValueKind = JsonValueKind.String Then Return first.GetString()
            End If
            Return ""
        End Function

        Private Shared Function ReadDate(element As JsonElement, propertyName As String) As DateTimeOffset?
            Dim value = ReadString(element, propertyName)
            Dim parsed As DateTimeOffset
            If DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, parsed) Then
                Return parsed
            End If
            Return Nothing
        End Function

        Private Shared Function ReadEvalScore(cardData As JsonElement) As Double?
            Dim evalResults As JsonElement
            If cardData.ValueKind <> JsonValueKind.Object OrElse Not cardData.TryGetProperty("eval_results", evalResults) Then Return Nothing
            If evalResults.ValueKind <> JsonValueKind.Array Then Return Nothing

            Dim scores As New List(Of Double)
            For Each result In evalResults.EnumerateArray()
                Dim metrics As JsonElement
                If result.TryGetProperty("metrics", metrics) AndAlso metrics.ValueKind = JsonValueKind.Array Then
                    For Each metric In metrics.EnumerateArray()
                        Dim value As JsonElement
                        If metric.TryGetProperty("value", value) AndAlso value.ValueKind = JsonValueKind.Number Then
                            Dim score As Double
                            If value.TryGetDouble(score) Then
                                If score <= 1 Then score *= 100
                                scores.Add(Math.Clamp(score, 0, 100))
                            End If
                        End If
                    Next
                End If
            Next

            If scores.Count = 0 Then Return Nothing
            Return scores.Average()
        End Function

        Private Shared Function InferQuantization(fileName As String) As String
            Dim match = Regex.Match(fileName, "(IQ[0-9]_[A-Z0-9_]+|Q[0-9]_[A-Z0-9_]+|QAT|F16|BF16|FP16|FP8)", RegexOptions.IgnoreCase)
            If match.Success Then
                Return match.Value.ToUpperInvariant()
            End If
            If ContainsQatToken(fileName) Then Return "QAT"
            Return "Q4_K_M"
        End Function

        Private Shared Function ContainsQatToken(value As String) As Boolean
            If String.IsNullOrWhiteSpace(value) Then Return False
            Return Regex.IsMatch(value, "(^|[^a-z0-9])qat([^a-z0-9]|$)", RegexOptions.IgnoreCase)
        End Function

        Private Shared Function InferParamsB(id As String, tags As IEnumerable(Of String), hasCard As Boolean, cardData As JsonElement) As Double
            Dim candidates As New List(Of String) From {id}
            candidates.AddRange(tags)

            If hasCard Then
                Dim modelIndex = ReadString(cardData, "model-index")
                If Not String.IsNullOrWhiteSpace(modelIndex) Then candidates.Add(modelIndex)
            End If

            For Each candidate In candidates
                Dim text = candidate.Replace("_", "-")
                Dim match = Regex.Match(text, "(\d+(?:\.\d+)?)\s*([bm])(?:[^a-z]|$)", RegexOptions.IgnoreCase)
                If match.Success Then
                    Dim value = Double.Parse(match.Groups(1).Value, CultureInfo.InvariantCulture)
                    If match.Groups(2).Value.Equals("m", StringComparison.OrdinalIgnoreCase) Then value /= 1000
                    If value > 0 Then Return value
                End If
            Next

            Return 0
        End Function

        Private Shared Function InferActiveParamsB(id As String, paramsB As Double) As Double?
            Dim text = id.ToLowerInvariant()
            If text.Contains("mixtral", StringComparison.Ordinal) AndAlso paramsB >= 40 Then Return Math.Max(12.0R, paramsB / 4)
            If text.Contains("deepseek", StringComparison.Ordinal) AndAlso paramsB >= 100 Then Return Math.Min(40.0R, paramsB / 10)
            If text.Contains("moe", StringComparison.Ordinal) AndAlso paramsB >= 20 Then Return paramsB / 4
            Return Nothing
        End Function

        Private Shared Function InferContextLength(id As String, hasCard As Boolean, cardData As JsonElement) As Integer?
            Dim match = Regex.Match(id, "(\d+)\s*k", RegexOptions.IgnoreCase)
            If match.Success Then
                Return Integer.Parse(match.Groups(1).Value) * 1000
            End If
            Return Nothing
        End Function

        Private Shared Function InferUseCase(id As String, tags As IEnumerable(Of String), pipelineTag As String) As String
            Dim text = (id & " " & pipelineTag & " " & String.Join(" ", tags)).ToLowerInvariant()
            If text.Contains("sentence-similarity", StringComparison.Ordinal) OrElse text.Contains("feature-extraction", StringComparison.Ordinal) OrElse text.Contains("embedding", StringComparison.Ordinal) OrElse text.Contains("embed", StringComparison.Ordinal) OrElse text.Contains("bge", StringComparison.Ordinal) OrElse text.Contains("e5-", StringComparison.Ordinal) OrElse text.Contains("nomic-embed", StringComparison.Ordinal) Then
                Return "embedding"
            End If
            If text.Contains("vision", StringComparison.Ordinal) OrElse text.Contains("image-text", StringComparison.Ordinal) OrElse text.Contains("multimodal", StringComparison.Ordinal) OrElse text.Contains("-vl", StringComparison.Ordinal) OrElse text.Contains("llava", StringComparison.Ordinal) OrElse text.Contains("pixtral", StringComparison.Ordinal) Then
                Return "multimodal"
            End If
            If text.Contains("coder", StringComparison.Ordinal) OrElse text.Contains("code", StringComparison.Ordinal) OrElse text.Contains("starcoder", StringComparison.Ordinal) OrElse text.Contains("wizardcoder", StringComparison.Ordinal) OrElse text.Contains("deepseek-coder", StringComparison.Ordinal) Then
                Return "coding"
            End If
            If text.Contains("reason", StringComparison.Ordinal) OrElse text.Contains("r1", StringComparison.Ordinal) OrElse text.Contains("math", StringComparison.Ordinal) OrElse text.Contains("orca", StringComparison.Ordinal) Then
                Return "reasoning"
            End If
            If text.Contains("chat", StringComparison.Ordinal) OrElse text.Contains("instruct", StringComparison.Ordinal) OrElse text.Contains("assistant", StringComparison.Ordinal) Then
                Return "chat"
            End If
            Return "general"
        End Function
    End Class
End Namespace
