Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Windows.Data
Imports System.Windows.Media

Namespace Infrastructure
    ''' <summary>
    ''' Maps a fit-kind token ("ok"/"warn"/"cpu"/...) to a chip brush.
    ''' ConverterParameter selects the role: "bg" (default), "fg", or "dot".
    ''' Color is never the only signal — the chip always carries a text label too.
    ''' </summary>
    Public Class FitKindBrushConverter
        Implements IValueConverter

        Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
            Dim kind = If(TryCast(value, String), "").Trim().ToLowerInvariant()
            Select Case If(TryCast(parameter, String), "").Trim().ToLowerInvariant()
                Case "fg"
                    Return BrushFor(kind, "#1B7A3D", "#9A6700", "#5F5E5A")
                Case "dot"
                    Return BrushFor(kind, "#1D9E75", "#EF9F27", "#B4B2A9")
                Case Else
                    Return BrushFor(kind, "#E7F6EC", "#FFF4E0", "#EFEFF0")
            End Select
        End Function

        Private Shared Function BrushFor(kind As String, ok As String, warn As String, neutral As String) As Brush
            Dim hex As String
            Select Case kind
                Case "ok"
                    hex = ok
                Case "warn"
                    hex = warn
                Case Else
                    hex = neutral
            End Select
            Dim brush = CType(New BrushConverter().ConvertFromString(hex), SolidColorBrush)
            brush.Freeze()
            Return brush
        End Function

        Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
            Throw New NotSupportedException()
        End Function
    End Class
End Namespace
