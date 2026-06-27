Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Text.RegularExpressions

Namespace Utilities
    Public Module InputParsers
        Public Function ParseContextLength(value As String, Optional defaultValue As Integer = 4096) As Integer
            If String.IsNullOrWhiteSpace(value) Then
                Return defaultValue
            End If

            Dim text = value.Trim().ToLowerInvariant()
            Dim multiplier As Double = 1
            If text.EndsWith("k", StringComparison.Ordinal) Then
                multiplier = 1000
                text = text.Substring(0, text.Length - 1)
            ElseIf text.EndsWith("m", StringComparison.Ordinal) Then
                multiplier = 1000 * 1000
                text = text.Substring(0, text.Length - 1)
            End If

            Dim number As Double
            If Not Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, number) Then
                Throw New ArgumentException($"Context length '{value}' is not valid.")
            End If

            Dim parsed = CInt(Math.Round(number * multiplier))
            If parsed <= 0 Then
                Throw New ArgumentException("Context length must be greater than zero.")
            End If
            Return parsed
        End Function

        Public Function ParseBytes(value As String, Optional relativeToBytes As Long? = Nothing) As Long
            If String.IsNullOrWhiteSpace(value) Then
                Throw New ArgumentException("Memory value is required.")
            End If

            Dim text = value.Trim().ToLowerInvariant().Replace(" ", "")
            If text.EndsWith("%", StringComparison.Ordinal) Then
                If Not relativeToBytes.HasValue Then
                    Throw New ArgumentException("A percentage memory value requires a base size.")
                End If
                Dim percentText = text.Substring(0, text.Length - 1)
                Dim percent As Double
                If Not Double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, percent) Then
                    Throw New ArgumentException($"Memory percentage '{value}' is not valid.")
                End If
                Return CLng(Math.Round(relativeToBytes.Value * percent / 100.0R))
            End If

            Dim match = Regex.Match(text, "^([0-9]+(?:\.[0-9]+)?)(b|kb|kib|mb|mib|gb|gib|tb|tib)?$")
            If Not match.Success Then
                Throw New ArgumentException($"Memory value '{value}' is not valid.")
            End If

            Dim number = Double.Parse(match.Groups(1).Value, CultureInfo.InvariantCulture)
            Dim unit = match.Groups(2).Value
            Dim multiplier As Double = 1
            Select Case unit
                Case "", "b"
                    multiplier = 1
                Case "kb"
                    multiplier = 1000
                Case "kib"
                    multiplier = 1024
                Case "mb"
                    multiplier = 1000 * 1000
                Case "mib"
                    multiplier = 1024 * 1024
                Case "gb"
                    multiplier = 1000 * 1000 * 1000
                Case "gib"
                    multiplier = 1024.0R * 1024.0R * 1024.0R
                Case "tb"
                    multiplier = 1000.0R * 1000.0R * 1000.0R * 1000.0R
                Case "tib"
                    multiplier = 1024.0R * 1024.0R * 1024.0R * 1024.0R
            End Select

            Return CLng(Math.Round(number * multiplier))
        End Function

        Public Function TryParseOptionalBytes(value As String, relativeToBytes As Long?, ByRef result As Long?) As Boolean
            If String.IsNullOrWhiteSpace(value) Then
                result = Nothing
                Return True
            End If

            Try
                result = ParseBytes(value, relativeToBytes)
                Return True
            Catch
                result = Nothing
                Return False
            End Try
        End Function
    End Module
End Namespace
