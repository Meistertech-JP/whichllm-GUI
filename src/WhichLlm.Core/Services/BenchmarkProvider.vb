Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Net.Http
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Utilities

Namespace Services
    Public Class BenchmarkProvider
        Implements IBenchmarkProvider

        Private Const OpenLlmRowsUrl As String = "https://datasets-server.huggingface.co/rows?dataset=open-llm-leaderboard%2Fcontents&config=default&split=train&length=100"
        Private Const ArenaRowsUrl As String = "https://datasets-server.huggingface.co/rows?dataset=mathewhe%2Fchatbot-arena-elo&config=default&split=train&length=100"
        Private Const AiderPolyglotUrl As String = "https://raw.githubusercontent.com/Aider-AI/aider/main/aider/website/_data/polyglot_leaderboard.yml"
        Private Const ArtificialAnalysisUrl As String = "https://artificialanalysis.ai/leaderboards/models"

        Private ReadOnly _cache As IBenchmarkCache
        Private ReadOnly _httpClient As HttpClient

        Public Sub New(cache As IBenchmarkCache, Optional httpClient As HttpClient = Nothing)
            _cache = cache
            If httpClient Is Nothing Then
                ' Self-created client: bound the buffered response size so a hostile or
                ' broken endpoint cannot exhaust memory through the GetStringAsync scrape
                ' paths (Artificial Analysis HTML / Aider YAML). Streaming reads used for
                ' the dataset feeds are unaffected by this cap.
                _httpClient = New HttpClient() With {
                    .Timeout = TimeSpan.FromSeconds(30),
                    .MaxResponseContentBufferSize = 16L * 1024 * 1024
                }
            Else
                _httpClient = httpClient
            End If
            If Not _httpClient.DefaultRequestHeaders.UserAgent.Any() Then
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("whichllm-gui/0.4")
            End If
        End Sub

        Public Async Function LoadBenchmarksAsync(refresh As Boolean, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence)) Implements IBenchmarkProvider.LoadBenchmarksAsync
            If Not refresh AndAlso _cache.IsFresh() Then
                Dim cached = Await _cache.LoadAsync(cancellationToken)
                If IsUsableCachedBenchmark(cached) Then Return cached
            End If

            Dim evidence As Dictionary(Of String, BenchmarkEvidence)
            Try
                evidence = Await FetchCombinedEvidenceAsync(cancellationToken)
            Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
                evidence = New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            End Try

            If evidence.Count = 0 Then
                evidence = BuildCuratedEvidence()
            End If

            Await _cache.SaveAsync(evidence, cancellationToken)
            Return evidence
        End Function

        Private Shared Function IsUsableCachedBenchmark(cached As Dictionary(Of String, BenchmarkEvidence)) As Boolean
            If cached Is Nothing OrElse cached.Count = 0 Then Return False

            ' v0.3 cache entries were generic normalized seeds such as "qwen3"
            ' with no tier metadata. v0.4 needs source/tier-aware keys so exact,
            ' variant, base_model, and line_interp resolution can work reliably.
            If cached.Values.Any(Function(e) Not String.IsNullOrWhiteSpace(e.BenchmarkTier)) Then Return True
            If cached.Keys.Any(Function(key) key.Contains("/", StringComparison.Ordinal)) Then Return True
            Return False
        End Function

        Private Async Function FetchCombinedEvidenceAsync(cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, BenchmarkEvidence))
            Dim frozen As New Dictionary(Of String, BenchmarkScoreEntry)(StringComparer.OrdinalIgnoreCase)
            Dim current As New Dictionary(Of String, BenchmarkScoreEntry)(StringComparer.OrdinalIgnoreCase)

            Await AddSafeAsync(Function() AddOpenLlmLeaderboardAsync(frozen, cancellationToken))
            Await AddSafeAsync(Function() AddArenaAsync(frozen, cancellationToken))

            AddLiveBench(current)
            Await AddSafeAsync(Function() AddArtificialAnalysisAsync(current, cancellationToken))
            Await AddSafeAsync(Function() AddAiderAsync(current, cancellationToken))

            Dim combined As New Dictionary(Of String, BenchmarkScoreEntry)(StringComparer.OrdinalIgnoreCase)
            For Each pair In frozen
                combined(pair.Key) = pair.Value
            Next
            For Each pair In current
                combined(pair.Key) = pair.Value
            Next

            For Each pair In frozen.ToList()
                If current.ContainsKey(pair.Key) OrElse Not combined.ContainsKey(pair.Key) Then Continue For
                Dim entry = combined(pair.Key)
                Dim factor = LineageRecencyFactor(pair.Key)
                entry.Score = Math.Round(entry.Score * factor, 1)
                If factor < 1.0R Then entry.Notes &= $" Frozen benchmark demoted by lineage recency factor {factor:0.00}."
                combined(pair.Key) = entry
            Next

            Dim evidence As New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            For Each pair In combined
                evidence(pair.Key) = New BenchmarkEvidence With {
                    .Source = "direct",
                    .Score = pair.Value.Score,
                    .Confidence = 1.0R,
                    .Status = "",
                    .BenchmarkTier = pair.Value.Tier,
                    .Notes = pair.Value.Notes
                }
            Next

            Return evidence
        End Function

        Private Shared Async Function AddSafeAsync(action As Func(Of Task)) As Task
            Try
                Await action()
            Catch ex As OperationCanceledException
                Throw
            Catch
            End Try
        End Function

        Private Async Function AddOpenLlmLeaderboardAsync(target As Dictionary(Of String, BenchmarkScoreEntry), cancellationToken As CancellationToken) As Task
            Dim offset = 0
            Dim pages = 0
            While offset < 1000 AndAlso pages < 25
                pages += 1
                Using response = Await _httpClient.GetAsync(OpenLlmRowsUrl & "&offset=" & offset.ToString(CultureInfo.InvariantCulture), cancellationToken)
                    response.EnsureSuccessStatusCode()
                    Using stream = Await response.Content.ReadAsStreamAsync(cancellationToken)
                        Using document = Await JsonDocument.ParseAsync(stream, cancellationToken:=cancellationToken)
                            Dim rows As JsonElement
                            If Not document.RootElement.TryGetProperty("rows", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Exit While
                            Dim count = 0
                            For Each item In rows.EnumerateArray()
                                count += 1
                                Dim row As JsonElement
                                If Not item.TryGetProperty("row", row) Then Continue For
                                Dim name = ReadStringAny(row, "fullname", "model", "Model")
                                Dim raw = ReadDoubleAny(row, "Average ⬆️", "Average", "average")
                                If String.IsNullOrWhiteSpace(name) OrElse Not raw.HasValue OrElse raw.Value <= 0 Then Continue For
                                AddScore(target, name, NormalizeOpenLlm(raw.Value), "frozen", "Open LLM Leaderboard v2 frozen benchmark.")
                            Next
                            If count = 0 Then Exit While
                            offset += count

                            Dim total = ReadDoubleAny(document.RootElement, "num_rows_total")
                            If total.HasValue AndAlso offset >= total.Value Then Exit While
                        End Using
                    End Using
                End Using
            End While
        End Function

        Private Async Function AddArenaAsync(target As Dictionary(Of String, BenchmarkScoreEntry), cancellationToken As CancellationToken) As Task
            Dim offset = 0
            Dim pages = 0
            While offset < 1000 AndAlso pages < 25
                pages += 1
                Using response = Await _httpClient.GetAsync(ArenaRowsUrl & "&offset=" & offset.ToString(CultureInfo.InvariantCulture), cancellationToken)
                    response.EnsureSuccessStatusCode()
                    Using stream = Await response.Content.ReadAsStreamAsync(cancellationToken)
                        Using document = Await JsonDocument.ParseAsync(stream, cancellationToken:=cancellationToken)
                            Dim rows As JsonElement
                            If Not document.RootElement.TryGetProperty("rows", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Exit While
                            Dim count = 0
                            For Each item In rows.EnumerateArray()
                                count += 1
                                Dim row As JsonElement
                                If Not item.TryGetProperty("row", row) Then Continue For
                                Dim modelName = ReadStringAny(row, "Model", "model", "name", "Name")
                                Dim org = ReadStringAny(row, "Organization", "organization", "org")
                                Dim elo = ReadDoubleAny(row, "Arena Elo", "elo", "Elo", "rating", "score")
                                If String.IsNullOrWhiteSpace(modelName) OrElse Not elo.HasValue Then Continue For
                                For Each hfId In ArenaNameToHfIds(modelName, org)
                                    AddScore(target, hfId, NormalizeArena(elo.Value), "frozen", "Chatbot Arena ELO frozen benchmark.")
                                Next
                            Next
                            If count = 0 Then Exit While
                            offset += count

                            Dim total = ReadDoubleAny(document.RootElement, "num_rows_total")
                            If total.HasValue AndAlso offset >= total.Value Then Exit While
                        End Using
                    End Using
                End Using
            End While
        End Function

        Private Sub AddLiveBench(target As Dictionary(Of String, BenchmarkScoreEntry))
            Dim raw = New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase) From {
                {"MiniMaxAI/MiniMax-M2.5", 60.3R},
                {"MiniMaxAI/MiniMax-M2.7", 65.0R},
                {"Qwen/Qwen3-235B-A22B-Instruct-2507", 48.0R},
                {"Qwen/Qwen3-235B-A22B-Thinking-2507", 52.9R},
                {"Qwen/Qwen3-32B", 42.7R},
                {"Qwen/Qwen3.6-27B", 65.6R},
                {"deepseek-ai/DeepSeek-V3.2", 63.1R},
                {"deepseek-ai/DeepSeek-V4-Flash", 67.7R},
                {"deepseek-ai/DeepSeek-V4-Pro", 74.4R},
                {"google/gemma-4-31b-it", 62.4R},
                {"moonshotai/Kimi-K2.6-Thinking", 72.4R},
                {"zai-org/GLM-5", 68.7R},
                {"zai-org/GLM-5.1", 70.6R},
                {"deepseek-ai/DeepSeek-R1-0528", 71.0R},
                {"deepseek-ai/DeepSeek-R1", 65.0R},
                {"Qwen/Qwen3-235B-A22B", 65.0R},
                {"Qwen/Qwen3-Coder-30B-A3B-Instruct", 58.0R},
                {"Qwen/QwQ-32B", 57.0R},
                {"Qwen/Qwen3-4B-Thinking-2507", 50.0R},
                {"meta-llama/Llama-3.3-70B-Instruct", 48.0R},
                {"meta-llama/Llama-4-Maverick-17B-128E-Instruct", 54.0R},
                {"meta-llama/Llama-4-Scout-17B-16E-Instruct", 49.0R},
                {"google/gemma-3-27b-it", 50.0R},
                {"google/gemma-4-26b-a4b-it", 54.0R},
                {"microsoft/phi-4", 53.0R},
                {"mistralai/Mistral-Large-Instruct-2411", 58.0R},
                {"mistralai/Devstral-Small-2505", 50.0R},
                {"openai/gpt-oss-20b", 52.0R},
                {"Qwen/Qwen3-8B", 50.0R},
                {"Qwen/Qwen3-14B", 56.0R},
                {"Qwen/Qwen3-4B-Instruct-2507", 45.0R},
                {"Qwen/Qwen3-4B", 42.0R},
                {"Qwen/Qwen2.5-7B-Instruct", 38.0R},
                {"Qwen/Qwen2.5-14B-Instruct", 42.0R},
                {"Qwen/Qwen2.5-32B-Instruct", 50.0R},
                {"meta-llama/Llama-3.1-8B-Instruct", 36.0R},
                {"google/gemma-2-9b-it", 38.0R},
                {"google/gemma-3-12b-it", 44.0R},
                {"microsoft/Phi-4-mini-instruct", 40.0R},
                {"mistralai/Mistral-Small-3.2-24B-Instruct-2506", 50.0R},
                {"mistralai/Mistral-Small-3.1-24B-Instruct-2503", 48.0R}
            }

            For Each pair In raw
                AddScore(target, pair.Key, NormalizeLiveBench(pair.Value), "current", "LiveBench current-generation snapshot.")
            Next
        End Sub

        Private Async Function AddArtificialAnalysisAsync(target As Dictionary(Of String, BenchmarkScoreEntry), cancellationToken As CancellationToken) As Task
            For Each pair In ArtificialAnalysisFallback()
                AddScore(target, pair.Key, NormalizeArtificialAnalysis(pair.Value), "current", "Artificial Analysis Intelligence Index curated fallback.")
            Next

            ' The site exposes no stable API, so the live scrape is best-effort and must
            ' never overwrite the curated fallback: a misparsed token (e.g. a year, a
            ' percentage, or a "100") would otherwise win the max-score merge and hand a
            ' model a perfect score. It only fills models the fallback does not cover and
            ' only accepts values inside a plausible Intelligence-Index band.
            Dim html = Await _httpClient.GetStringAsync(ArtificialAnalysisUrl, cancellationToken)
            For Each map In ArtificialAnalysisNameMap()
                Dim raw = TryScrapeIntelligenceIndex(html, map.Key)
                If Not raw.HasValue Then Continue For
                For Each hfId In map.Value
                    If target.ContainsKey(hfId) Then Continue For
                    AddScore(target, hfId, NormalizeArtificialAnalysis(raw.Value), "current", "Artificial Analysis Intelligence Index live scrape.")
                Next
            Next
        End Function

        Private Shared Function TryScrapeIntelligenceIndex(html As String, anchor As String) As Double?
            If String.IsNullOrEmpty(html) OrElse String.IsNullOrEmpty(anchor) Then Return Nothing
            Dim idx = html.IndexOf(anchor, StringComparison.OrdinalIgnoreCase)
            If idx < 0 Then Return Nothing
            Dim window = html.Substring(idx, Math.Min(700, html.Length - idx))
            For Each match As Match In Regex.Matches(window, "(?:index|score|quality)[^0-9]{0,80}(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)
                Dim raw As Double
                If Double.TryParse(match.Groups(1).Value, NumberStyles.Float, CultureInfo.InvariantCulture, raw) AndAlso raw >= 5.0R AndAlso raw <= 90.0R Then
                    Return raw
                End If
            Next
            Return Nothing
        End Function

        Private Async Function AddAiderAsync(target As Dictionary(Of String, BenchmarkScoreEntry), cancellationToken As CancellationToken) As Task
            Dim text = Await _httpClient.GetStringAsync(AiderPolyglotUrl, cancellationToken)
            Dim records = Regex.Split(text, "\n(?=-\s+\w)")
            For Each record In records
                Dim modelMatch = Regex.Match(record, "^\s*model[:\s]+(.+?)$", RegexOptions.IgnoreCase Or RegexOptions.Multiline)
                Dim rateMatch = Regex.Match(record, "pass_rate_2[:\s]+(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)
                If Not modelMatch.Success OrElse Not rateMatch.Success Then Continue For

                Dim modelName = modelMatch.Groups(1).Value.Trim().Trim(""""c, "'"c).Split("/"c).Last().ToLowerInvariant()
                Dim rate As Double
                If Not Double.TryParse(rateMatch.Groups(1).Value, NumberStyles.Float, CultureInfo.InvariantCulture, rate) Then Continue For

                Dim hfIds As List(Of String) = Nothing
                If AiderNameMap().TryGetValue(modelName, hfIds) Then
                    For Each hfId In hfIds
                        AddScore(target, hfId, NormalizeAider(rate) * 0.85R, "current", "Aider polyglot live benchmark soft-merged at 0.85x.")
                    Next
                End If
            Next
        End Function

        Private Shared Sub AddScore(target As Dictionary(Of String, BenchmarkScoreEntry), modelId As String, score As Double, tier As String, notes As String)
            If String.IsNullOrWhiteSpace(modelId) OrElse score <= 0 Then Return
            Dim normalizedScore = Math.Clamp(Math.Round(score, 1), 0, 100)
            Dim existing As BenchmarkScoreEntry = Nothing
            If Not target.TryGetValue(modelId, existing) OrElse normalizedScore > existing.Score Then
                target(modelId) = New BenchmarkScoreEntry With {.Score = normalizedScore, .Tier = tier, .Notes = notes}
            End If
        End Sub

        Private Shared Function NormalizeOpenLlm(avg As Double) As Double
            Return Math.Clamp(Math.Round(avg / 52.0R * 78.0R, 1), 0, 78.0R)
        End Function

        Private Shared Function NormalizeArena(elo As Double) As Double
            Return Math.Clamp(Math.Round((elo - 1030.0R) / (1430.0R - 1030.0R) * 82.0R, 1), 0, 82.0R)
        End Function

        Private Shared Function NormalizeLiveBench(score As Double) As Double
            Return Math.Clamp(Math.Round((score - 18.0R) / (75.0R - 18.0R) * 100.0R, 1), 0, 100)
        End Function

        Private Shared Function NormalizeArtificialAnalysis(index As Double) As Double
            Return Math.Clamp(Math.Round((index - 12.5R) / (56.2R - 12.5R) * 100.0R, 1), 0, 100)
        End Function

        Private Shared Function NormalizeAider(passRate As Double) As Double
            Return Math.Clamp(Math.Round(passRate / 90.0R * 100.0R, 1), 0, 100)
        End Function

        Private Shared Function BuildCuratedEvidence() As Dictionary(Of String, BenchmarkEvidence)
            Dim data As New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            AddSeed(data, "Qwen/Qwen3-14B", 66)
            AddSeed(data, "Qwen/Qwen3-8B", 56)
            AddSeed(data, "Qwen/Qwen2.5-7B-Instruct", 52)
            AddSeed(data, "meta-llama/Llama-3.1-8B-Instruct", 50)
            AddSeed(data, "google/gemma-3-12b-it", 52)
            AddSeed(data, "google/gemma-3-4b-it", 44)
            AddSeed(data, "deepseek-ai/DeepSeek-R1-Distill-Qwen-7B", 54)
            AddSeed(data, "microsoft/Phi-4-mini-instruct", 47)
            AddSeed(data, "mistralai/Mistral-7B-Instruct-v0.3", 46)
            AddSeed(data, "BAAI/bge-small-en-v1.5", 42)
            Return data
        End Function

        Private Shared Sub AddSeed(data As Dictionary(Of String, BenchmarkEvidence), key As String, score As Double)
            data(key) = New BenchmarkEvidence With {
                .Source = "direct",
                .Score = score,
                .Confidence = 0.7R,
                .Status = "",
                .BenchmarkTier = "seed",
                .Notes = "Curated GUI seed benchmark used only when live benchmark feeds are unavailable."
            }
        End Sub

        Private Shared Function LineageRecencyFactor(modelId As String) As Double
            Dim text = If(modelId, "").ToLowerInvariant()
            If text.Length = 0 Then Return 1.0R

            Dim families = New List(Of List(Of (Pattern As String, Index As Integer))) From {
                New List(Of (String, Integer)) From {("qwen3\.6", 7), ("qwen3\.5", 6), ("qwen3-next", 6), ("qwen3", 5), ("qwq", 4), ("qwen2\.5", 3), ("qwen2(?!\.5)", 2), ("qwen1", 1)},
                New List(Of (String, Integer)) From {("llama-?4\.5", 5), ("llama-?4", 4), ("llama-?3\.3", 3), ("llama-?3\.2", 3), ("llama-?3\.1", 3), ("meta-llama-?3(?!\.)", 2), ("llama-?2", 1)},
                New List(Of (String, Integer)) From {("deepseek-v4", 5), ("deepseek-v3\.2", 4), ("deepseek-v3\.1", 4), ("deepseek-r1-0528", 4), ("deepseek-r1", 3), ("deepseek-v3", 3), ("deepseek-v2\.5", 2), ("deepseek-v2", 1)},
                New List(Of (String, Integer)) From {("gemma-?4", 4), ("gemma-?3", 3), ("gemma-?2", 2), ("gemma(?!-?[2-9])", 1)},
                New List(Of (String, Integer)) From {("phi-?5", 5), ("phi-?4", 4), ("phi-?3\.5", 3), ("phi-?3(?!\.5)", 2), ("phi-?2", 1)},
                New List(Of (String, Integer)) From {("mistral-small-3\.2", 4), ("mistral-small-2506", 4), ("mistral-small-3\.1", 3), ("mistral-small-3", 3), ("mistral-small", 1)}
            }

            Dim bestFactor = 1.0R
            For Each family In families
                Dim maxIndex = family.Max(Function(item) item.Index)
                For Each item In family
                    If Regex.IsMatch(text, item.Pattern, RegexOptions.IgnoreCase) Then
                        Dim generationsOld = Math.Max(0, maxIndex - item.Index)
                        bestFactor = Math.Min(bestFactor, Math.Max(0.55R, 1.0R - 0.12R * generationsOld))
                        Exit For
                    End If
                Next
            Next

            Return bestFactor
        End Function

        Private Shared Function ReadStringAny(element As JsonElement, ParamArray names() As String) As String
            For Each name In names
                Dim prop As JsonElement
                If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(name, prop) Then
                    If prop.ValueKind = JsonValueKind.String Then Return prop.GetString()
                    If prop.ValueKind = JsonValueKind.Number OrElse prop.ValueKind = JsonValueKind.True OrElse prop.ValueKind = JsonValueKind.False Then Return prop.ToString()
                End If
            Next
            Return ""
        End Function

        Private Shared Function ReadDoubleAny(element As JsonElement, ParamArray names() As String) As Double?
            For Each name In names
                Dim prop As JsonElement
                If element.ValueKind <> JsonValueKind.Object OrElse Not element.TryGetProperty(name, prop) Then Continue For
                If prop.ValueKind = JsonValueKind.Number Then
                    Dim value As Double
                    If prop.TryGetDouble(value) Then Return value
                End If
                If prop.ValueKind = JsonValueKind.String Then
                    Dim value As Double
                    If Double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, value) Then Return value
                End If
            Next
            Return Nothing
        End Function

        Private Shared Function ArenaNameToHfIds(modelName As String, org As String) As IEnumerable(Of String)
            Dim clean = Regex.Replace(If(modelName, ""), "\s*\([\d-]+\)\s*$", "").Trim()
            clean = Regex.Replace(clean, "-(bf16|fp8|fp16)$", "", RegexOptions.IgnoreCase)
            If clean.Contains("/", StringComparison.Ordinal) Then Return New String() {clean}

            Dim prefixes As New List(Of String)
            Select Case If(org, "").Trim().ToLowerInvariant()
                Case "alibaba"
                    prefixes.Add("Qwen")
                Case "meta"
                    prefixes.Add("meta-llama")
                Case "deepseek", "deepseek ai"
                    prefixes.Add("deepseek-ai")
                Case "google"
                    prefixes.Add("google")
                Case "mistral"
                    prefixes.Add("mistralai")
                Case "microsoft"
                    prefixes.Add("microsoft")
                Case "nvidia"
                    prefixes.Add("nvidia")
                Case "ibm"
                    prefixes.Add("ibm-granite")
            End Select

            Return prefixes.Select(Function(prefix) prefix & "/" & clean).ToList()
        End Function

        Private Shared Function ArtificialAnalysisNameMap() As Dictionary(Of String, List(Of String))
            Return New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase) From {
                {"Kimi K2", New List(Of String) From {"moonshotai/Kimi-K2-Instruct"}},
                {"Kimi K2-Thinking", New List(Of String) From {"moonshotai/Kimi-K2-Thinking"}},
                {"DeepSeek V3.2", New List(Of String) From {"deepseek-ai/DeepSeek-V3.2"}},
                {"DeepSeek V4 Pro", New List(Of String) From {"deepseek-ai/DeepSeek-V4-Pro"}},
                {"DeepSeek V4 Flash", New List(Of String) From {"deepseek-ai/DeepSeek-V4-Flash"}},
                {"Qwen3 14B", New List(Of String) From {"Qwen/Qwen3-14B"}},
                {"Qwen3 8B", New List(Of String) From {"Qwen/Qwen3-8B"}},
                {"Qwen3 32B", New List(Of String) From {"Qwen/Qwen3-32B"}},
                {"Gemma 3 27B", New List(Of String) From {"google/gemma-3-27b-it"}},
                {"Gemma 4 26B-A4B", New List(Of String) From {"google/gemma-4-26b-a4b-it"}},
                {"Phi-4", New List(Of String) From {"microsoft/phi-4"}},
                {"MiniMax-M2", New List(Of String) From {"MiniMaxAI/MiniMax-M2"}}
            }
        End Function

        Private Shared Function ArtificialAnalysisFallback() As Dictionary(Of String, Double)
            Return New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase) From {
                {"moonshotai/Kimi-K2-Thinking", 50.0R},
                {"moonshotai/Kimi-K2-Instruct", 47.0R},
                {"XiaomiMiMo/MiMo-V2.5-Pro", 54.0R},
                {"deepseek-ai/DeepSeek-V4-Pro", 54.0R},
                {"deepseek-ai/DeepSeek-V4-Flash", 45.0R},
                {"deepseek-ai/DeepSeek-V3.2", 48.0R},
                {"deepseek-ai/DeepSeek-R1", 43.0R},
                {"Qwen/Qwen3-32B", 43.0R},
                {"Qwen/Qwen3-14B", 36.0R},
                {"Qwen/Qwen3-8B", 30.0R},
                {"meta-llama/Llama-3.3-70B-Instruct", 41.0R},
                {"google/gemma-3-27b-it", 37.0R},
                {"google/gemma-4-26b-a4b-it", 42.0R},
                {"microsoft/phi-4", 34.0R},
                {"MiniMaxAI/MiniMax-M2", 44.0R},
                {"openai/gpt-oss-20b", 35.0R},
                {"openai/gpt-oss-120b", 42.0R}
            }
        End Function

        Private Shared Function AiderNameMap() As Dictionary(Of String, List(Of String))
            Return New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase) From {
                {"deepseek-r1", New List(Of String) From {"deepseek-ai/DeepSeek-R1"}},
                {"deepseek-r1-0528", New List(Of String) From {"deepseek-ai/DeepSeek-R1-0528"}},
                {"deepseek-v3", New List(Of String) From {"deepseek-ai/DeepSeek-V3"}},
                {"deepseek-v3.2", New List(Of String) From {"deepseek-ai/DeepSeek-V3.2"}},
                {"deepseek-v4-pro", New List(Of String) From {"deepseek-ai/DeepSeek-V4-Pro"}},
                {"deepseek-v4-flash", New List(Of String) From {"deepseek-ai/DeepSeek-V4-Flash"}},
                {"qwen3-coder-30b-a3b-instruct", New List(Of String) From {"Qwen/Qwen3-Coder-30B-A3B-Instruct"}},
                {"qwen2.5-coder-32b-instruct", New List(Of String) From {"Qwen/Qwen2.5-Coder-32B-Instruct"}},
                {"qwen3-32b", New List(Of String) From {"Qwen/Qwen3-32B"}},
                {"qwen3.6-27b", New List(Of String) From {"Qwen/Qwen3.6-27B"}},
                {"llama-3.3-70b-instruct", New List(Of String) From {"meta-llama/Llama-3.3-70B-Instruct"}},
                {"gemma-3-27b-it", New List(Of String) From {"google/gemma-3-27b-it"}},
                {"phi-4", New List(Of String) From {"microsoft/phi-4"}},
                {"qwq-32b", New List(Of String) From {"Qwen/QwQ-32B"}}
            }
        End Function

        Private Structure BenchmarkScoreEntry
            Public Property Score As Double
            Public Property Tier As String
            Public Property Notes As String
        End Structure
    End Class
End Namespace
