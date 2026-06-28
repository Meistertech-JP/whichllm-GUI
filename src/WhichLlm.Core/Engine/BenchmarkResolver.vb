Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Text.RegularExpressions
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Utilities

Namespace Engine
    Public NotInheritable Class BenchmarkResolver
        Private Shared ReadOnly RepoSuffixes As String() = {"-GGUF", "-gguf", "-AWQ", "-GPTQ", "-FP8", "-fp8", "-BF16", "-bf16", "-FP16", "-fp16"}

        Private Sub New()
        End Sub

        Public Shared Function Resolve(model As ModelInfo, benchmarks As Dictionary(Of String, BenchmarkEvidence), evidenceMode As String, Optional familyDominantParamsB As Double? = Nothing) As BenchmarkEvidence
            If model Is Nothing Then Return NoneEvidence()

            Dim index = BuildIndex(If(benchmarks, New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)))
            Dim actualParamsB As Double? = If(model.ParameterCountB > 0, model.ParameterCountB, Nothing)

            Dim direct = TryLookupExact(model.RepoId, index)
            If direct IsNot Nothing Then
                Return WithEvidenceSource(direct, "direct", 1.0R, "", "")
            End If

            Dim variantCandidates = GenerateCandidates(model.RepoId).Skip(1).ToList()
            For Each candidate In GenerateScoreNameCandidates(model.RepoId, index)
                AppendUnique(variantCandidates, candidate)
            Next

            For Each candidate In variantCandidates
                Dim evidence = TryLookup(candidate, index)
                If evidence Is Nothing Then Continue For
                If Not ParamsCompatible(actualParamsB, candidate) Then Continue For
                If Not FamilyParamsCompatible(actualParamsB, familyDominantParamsB) Then Continue For
                Return WithEvidenceSource(evidence, "variant", 0.55R, "~", "Benchmark inherited from a suffix-stripped or name-matched variant.")
            Next

            If Not String.IsNullOrWhiteSpace(model.BaseModel) Then
                For Each candidate In GenerateCandidates(model.BaseModel)
                    Dim evidence = TryLookup(candidate, index)
                    If evidence Is Nothing Then Continue For
                    If Not ParamsCompatible(actualParamsB, candidate) Then Continue For
                    If Not FamilyParamsCompatible(actualParamsB, familyDominantParamsB) Then Continue For
                    Return WithEvidenceSource(evidence, "base_model", 0.6R, "~", "Benchmark inherited from cardData.base_model.")
                Next
            End If

            If FamilyParamsCompatible(actualParamsB, familyDominantParamsB) Then
                Dim sizeHint = If(actualParamsB, ExtractParamsB(model.RepoId))
                If Not sizeHint.HasValue Then sizeHint = ExtractParamsB(model.BaseModel)

                For Each modelId In New String() {model.RepoId, model.BaseModel}
                    If String.IsNullOrWhiteSpace(modelId) Then Continue For
                    For Each line In ExtractModelLines(modelId)
                        Dim bucket As List(Of BenchmarkEvidence) = Nothing
                        If index.LineBuckets.TryGetValue(line, bucket) Then
                            Dim interp = InterpolateLineScore(bucket, sizeHint)
                            If interp.Score > 0 Then
                                Return New BenchmarkEvidence With {
                                    .Source = "line_interp",
                                    .Score = interp.Score,
                                    .Confidence = interp.Confidence,
                                    .Status = "~",
                                    .BenchmarkTier = "line_interp",
                                    .Notes = "Size-aware interpolation from benchmarked models in the same model line."
                                }
                            End If
                        End If

                        Dim lineEvidence As BenchmarkEvidence = Nothing
                        If index.LineBest.TryGetValue(line, lineEvidence) Then
                            If Not ParamsCompatible(sizeHint, lineEvidence.Notes) Then Continue For
                            Return WithEvidenceSource(lineEvidence, "line_interp", 0.22R, "~", "Fallback model-line benchmark estimate.")
                        End If
                    Next
                Next
            End If

            If model.EvalScore.HasValue AndAlso model.EvalScore.Value > 0 Then
                Return New BenchmarkEvidence With {.Source = "self_reported", .Score = model.EvalScore.Value, .Confidence = 0.4R, .Status = "!sr", .BenchmarkTier = "self_reported", .Notes = "Hugging Face model-card evalResults only."}
            End If

            Return NoneEvidence()
        End Function

        Public Shared Function EvidenceAllowed(source As String, mode As String) As Boolean
            Select Case If(mode, "").ToLowerInvariant()
                Case "strict"
                    Return String.Equals(source, "direct", StringComparison.OrdinalIgnoreCase)
                Case "base"
                    Return String.Equals(source, "direct", StringComparison.OrdinalIgnoreCase) OrElse
                        String.Equals(source, "variant", StringComparison.OrdinalIgnoreCase) OrElse
                        String.Equals(source, "base_model", StringComparison.OrdinalIgnoreCase) OrElse
                        String.Equals(source, "line_interp", StringComparison.OrdinalIgnoreCase)
                Case Else
                    Return True
            End Select
        End Function

        Friend Shared Function ExtractParamsB(modelId As String) As Double?
            If String.IsNullOrWhiteSpace(modelId) Then Return Nothing
            Dim matches = Regex.Matches(modelId.ToLowerInvariant(), "(\d+(?:\.\d+)?)b(?:-a\d+(?:\.\d+)?b)?")
            If matches.Count = 0 Then Return Nothing

            Dim best = 0.0R
            For Each match As Match In matches
                Dim value As Double
                If Double.TryParse(match.Groups(1).Value, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
                    best = Math.Max(best, value)
                End If
            Next

            If best <= 0 Then Return Nothing
            Return best
        End Function

        Friend Shared Function ParamsCompatible(actualB As Double?, referenceId As String) As Boolean
            If Not actualB.HasValue OrElse actualB.Value <= 0 Then Return True
            Dim referenceB = ExtractParamsB(referenceId)
            If Not referenceB.HasValue OrElse referenceB.Value <= 0 Then Return True
            Dim ratio = actualB.Value / referenceB.Value
            Return ratio >= 0.5R AndAlso ratio <= 2.0R
        End Function

        Friend Shared Function ExtractModelLines(modelId As String) As List(Of String)
            Dim lines As New List(Of String)
            If String.IsNullOrWhiteSpace(modelId) Then Return lines

            Dim lower = modelId.ToLowerInvariant().Replace("_", "-")
            Dim stripped = Regex.Replace(lower, "-(gguf|awq|gptq|fp8|fp16|bf16|mxfp4|nvfp4)$", "")
            stripped = Regex.Replace(stripped, "-\d{4}(-hf)?$", "")

            Dim cleaned = Regex.Replace(stripped, "-\d+(\.\d+)?b(-a\d+(\.\d+)?b)?(-[a-z][-a-z0-9]*)*$", "")
            If cleaned <> stripped Then AppendUnique(lines, cleaned)

            Dim sourceLines = If(lines.Count > 0, lines.ToList(), New List(Of String) From {stripped})
            For Each line In sourceLines
                Dim broader = Regex.Replace(line, "(\d+)\.\d+$", "$1")
                If broader <> line Then AppendUnique(lines, broader)
            Next

            Return lines
        End Function

        Private Shared Function BuildIndex(benchmarks As Dictionary(Of String, BenchmarkEvidence)) As BenchmarkIndex
            Dim index As New BenchmarkIndex()
            For Each pair In benchmarks
                Dim evidence = CloneEvidence(pair.Value)
                Dim rawKey = If(pair.Key, "")
                Dim lowerKey = rawKey.ToLowerInvariant()
                Dim normalizedKey = Formatters.NormalizeModelName(rawKey)

                AddBest(index.Exact, lowerKey, evidence)
                AddBest(index.CaseInsensitive, lowerKey, evidence)
                If Not String.Equals(normalizedKey, lowerKey, StringComparison.OrdinalIgnoreCase) Then
                    AddBest(index.CaseInsensitive, normalizedKey, evidence)
                End If

                For Each line In ExtractModelLines(rawKey)
                    Dim lineEvidence = CloneEvidence(evidence)
                    lineEvidence.Notes = rawKey
                    AddBest(index.LineBest, line, lineEvidence)
                    If Not index.LineBuckets.ContainsKey(line) Then index.LineBuckets(line) = New List(Of BenchmarkEvidence)()
                    Dim bucketEvidence = CloneEvidence(evidence)
                    bucketEvidence.Notes = rawKey
                    index.LineBuckets(line).Add(bucketEvidence)
                Next

                If Not rawKey.Contains("/", StringComparison.Ordinal) AndAlso Not index.LineBuckets.ContainsKey(normalizedKey) Then
                    Dim bucketEvidence = CloneEvidence(evidence)
                    bucketEvidence.Notes = rawKey
                    index.LineBuckets(normalizedKey) = New List(Of BenchmarkEvidence) From {bucketEvidence}
                End If
            Next
            Return index
        End Function

        Private Shared Sub AddBest(target As Dictionary(Of String, BenchmarkEvidence), key As String, evidence As BenchmarkEvidence)
            If String.IsNullOrWhiteSpace(key) Then Return
            Dim existing As BenchmarkEvidence = Nothing
            If Not target.TryGetValue(key, existing) OrElse evidence.Score > existing.Score Then
                target(key) = CloneEvidence(evidence)
            End If
        End Sub

        Private Shared Function TryLookupExact(candidate As String, index As BenchmarkIndex) As BenchmarkEvidence
            If String.IsNullOrWhiteSpace(candidate) Then Return Nothing
            Dim evidence As BenchmarkEvidence = Nothing
            If index.Exact.TryGetValue(candidate.ToLowerInvariant(), evidence) Then Return CloneEvidence(evidence)
            Return Nothing
        End Function

        Private Shared Function TryLookup(candidate As String, index As BenchmarkIndex) As BenchmarkEvidence
            If String.IsNullOrWhiteSpace(candidate) Then Return Nothing
            Dim evidence As BenchmarkEvidence = Nothing
            If index.CaseInsensitive.TryGetValue(candidate.ToLowerInvariant(), evidence) Then Return CloneEvidence(evidence)
            If index.CaseInsensitive.TryGetValue(Formatters.NormalizeModelName(candidate), evidence) Then Return CloneEvidence(evidence)
            Return Nothing
        End Function

        Private Shared Function GenerateCandidates(modelId As String) As List(Of String)
            Dim candidates As New List(Of String)
            If String.IsNullOrWhiteSpace(modelId) Then Return candidates
            candidates.Add(modelId)

            Dim baseId = modelId
            For Each suffix In RepoSuffixes
                If baseId.EndsWith(suffix, StringComparison.Ordinal) Then
                    baseId = baseId.Substring(0, baseId.Length - suffix.Length)
                    AppendUnique(candidates, baseId)
                    Exit For
                End If
            Next

            If baseId.EndsWith("-Instruct", StringComparison.Ordinal) Then
                AppendUnique(candidates, baseId.Substring(0, baseId.Length - "-Instruct".Length))
            Else
                AppendUnique(candidates, baseId & "-Instruct")
            End If

            Return candidates
        End Function

        Private Shared Function GenerateScoreNameCandidates(modelId As String, index As BenchmarkIndex) As List(Of String)
            Dim result As New List(Of String)
            If String.IsNullOrWhiteSpace(modelId) Then Return result

            Dim stripped = StripRepoSuffix(modelId)
            Dim repoName = stripped.Split("/"c).Last()
            Dim wantedNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {repoName}
            If repoName.Contains("_", StringComparison.Ordinal) Then
                Dim parts = repoName.Split(New Char() {"_"c}, 2)
                If parts.Length = 2 AndAlso parts(0).Length > 0 AndAlso parts(1).Length > 0 Then
                    AppendUnique(result, parts(0) & "/" & parts(1))
                    wantedNames.Add(parts(1))
                End If
            End If

            For Each key In index.CaseInsensitive.Keys
                Dim scoreName = key.Split("/"c).Last()
                If wantedNames.Contains(scoreName) Then AppendUnique(result, key)
            Next

            Return result
        End Function

        Private Shared Function StripRepoSuffix(modelId As String) As String
            For Each suffix In RepoSuffixes
                If modelId.EndsWith(suffix, StringComparison.Ordinal) Then
                    Return modelId.Substring(0, modelId.Length - suffix.Length)
                End If
            Next
            Return modelId
        End Function

        Private Shared Function InterpolateLineScore(bucket As List(Of BenchmarkEvidence), paramsB As Double?) As (Score As Double, Confidence As Double)
            If bucket Is Nothing OrElse bucket.Count = 0 Then Return (0, 0)

            Dim weighted As New List(Of (Weight As Double, Score As Double, Distance As Double))
            Dim scores As New List(Of Double)
            For Each evidence In bucket
                Dim p = ExtractParamsB(evidence.Notes)
                If paramsB.HasValue AndAlso p.HasValue AndAlso Not ParamsCompatible(paramsB, evidence.Notes) Then Continue For
                scores.Add(evidence.Score)
                If paramsB.HasValue AndAlso p.HasValue Then
                    Dim dist = Math.Abs(Math.Log(Math.Max(paramsB.Value, 0.1R) / Math.Max(p.Value, 0.1R), 2))
                    weighted.Add((1.0R / (0.35R + dist), evidence.Score, dist))
                End If
            Next

            If weighted.Count = 0 Then
                If scores.Count = 0 Then Return (0, 0)
                scores.Sort()
                Return (scores(scores.Count \ 2), If(paramsB.HasValue, 0.3R, 0.25R))
            End If

            Dim score = weighted.Sum(Function(x) x.Weight * x.Score) / weighted.Sum(Function(x) x.Weight)
            Dim nearest = weighted.Min(Function(x) x.Distance)
            Dim confidence = If(nearest <= 0.15R, 0.45R, If(nearest <= 0.5R, 0.34R, 0.26R))
            Return (score, confidence)
        End Function

        Private Shared Function FamilyParamsCompatible(actualB As Double?, familyDominantParamsB As Double?) As Boolean
            If Not actualB.HasValue OrElse Not familyDominantParamsB.HasValue Then Return True
            If actualB.Value <= 0 OrElse familyDominantParamsB.Value <= 0 Then Return True
            Dim ratio = actualB.Value / familyDominantParamsB.Value
            Return ratio >= 0.5R AndAlso ratio <= 2.0R
        End Function

        Private Shared Function WithEvidenceSource(value As BenchmarkEvidence, source As String, confidence As Double, status As String, extraNote As String) As BenchmarkEvidence
            Dim notes = If(value.Notes, "")
            If Not String.IsNullOrWhiteSpace(extraNote) Then
                notes = If(String.IsNullOrWhiteSpace(notes), extraNote, notes & " " & extraNote)
            End If

            Return New BenchmarkEvidence With {
                .Source = source,
                .Score = value.Score,
                .Confidence = Math.Min(value.Confidence, confidence),
                .Status = If(String.IsNullOrWhiteSpace(status), value.Status, status),
                .Notes = notes,
                .BenchmarkTier = value.BenchmarkTier
            }
        End Function

        Private Shared Function CloneEvidence(value As BenchmarkEvidence) As BenchmarkEvidence
            If value Is Nothing Then Return NoneEvidence()
            Return New BenchmarkEvidence With {.Source = value.Source, .Score = value.Score, .Confidence = value.Confidence, .Status = value.Status, .Notes = value.Notes, .BenchmarkTier = value.BenchmarkTier}
        End Function

        Private Shared Function NoneEvidence() As BenchmarkEvidence
            Return New BenchmarkEvidence With {.Source = "none", .Score = 0, .Confidence = 0, .Status = "?", .Notes = "No benchmark evidence.", .BenchmarkTier = "none"}
        End Function

        Private Shared Sub AppendUnique(target As ICollection(Of String), value As String)
            If String.IsNullOrWhiteSpace(value) Then Return
            If Not target.Any(Function(existing) String.Equals(existing, value, StringComparison.OrdinalIgnoreCase)) Then target.Add(value)
        End Sub

        Private Class BenchmarkIndex
            Public ReadOnly Property Exact As New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            Public ReadOnly Property CaseInsensitive As New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            Public ReadOnly Property LineBest As New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            Public ReadOnly Property LineBuckets As New Dictionary(Of String, List(Of BenchmarkEvidence))(StringComparer.OrdinalIgnoreCase)
        End Class
    End Class
End Namespace
