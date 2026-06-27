Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Text.RegularExpressions

Namespace Utilities
    Public Module Formatters
        Public Function FormatBytes(bytes As Long) As String
            Dim units = New String() {"B", "KB", "MB", "GB", "TB"}
            Dim value = CDbl(Math.Max(0, bytes))
            Dim index = 0
            While value >= 1024 AndAlso index < units.Length - 1
                value /= 1024
                index += 1
            End While

            If index = 0 Then
                Return $"{bytes} {units(index)}"
            End If
            Return value.ToString("0.0", CultureInfo.InvariantCulture) & " " & units(index)
        End Function

        Public Function FormatScore(score As Double) As String
            Return score.ToString("0.0", CultureInfo.InvariantCulture)
        End Function

        Public Function NormalizeModelName(value As String) As String
            Dim text = value.ToLowerInvariant()
            text = Regex.Replace(text, "[^a-z0-9]+", "-")
            text = Regex.Replace(text, "-(gguf|awq|gptq|fp16|bf16|int8|instruct|chat|base|q[0-9].*)$", "")
            text = Regex.Replace(text, "-+", "-").Trim("-"c)
            Return text
        End Function

        Public Function ContainsAllTerms(candidate As String, query As String) As Boolean
            If String.IsNullOrWhiteSpace(query) Then
                Return True
            End If
            Dim haystack = candidate.ToLowerInvariant()
            For Each term In Regex.Split(query.ToLowerInvariant(), "\s+")
                If term.Length > 0 AndAlso Not haystack.Contains(term, StringComparison.Ordinal) Then
                    Return False
                End If
            Next
            Return True
        End Function
    End Module
End Namespace
