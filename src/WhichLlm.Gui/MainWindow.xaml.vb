Option Strict On
Option Explicit On

Imports System.Windows.Controls
Imports System.Windows.Data
Imports WhichLlm.Gui.ViewModels

Class MainWindow
    Private ReadOnly _viewModel As New MainViewModel()

    Public Sub New()
        InitializeComponent()
        DataContext = _viewModel
        AddHandler Loaded, AddressOf MainWindow_Loaded
    End Sub

    Private Async Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler Loaded, AddressOf MainWindow_Loaded
        Await _viewModel.InitializeAsync()
    End Sub

    Private Sub EditableSuggestionComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim combo = TryCast(sender, ComboBox)
        If combo Is Nothing OrElse combo.SelectedItem Is Nothing Then Return

        Dim selectedText = TryCast(combo.SelectedItem, String)
        If String.IsNullOrWhiteSpace(selectedText) Then Return

        Dim nextText = ReplaceLastInputSegment(combo.Text, selectedText)
        If Not String.Equals(combo.Text, nextText, StringComparison.Ordinal) Then
            combo.Text = nextText
        End If

        combo.SelectedItem = Nothing
        If Not String.Equals(combo.Text, nextText, StringComparison.Ordinal) Then
            combo.Text = nextText
        End If
        combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource()
    End Sub

    Private Shared Function ReplaceLastInputSegment(currentText As String, selectedText As String) As String
        If String.IsNullOrWhiteSpace(currentText) Then Return selectedText

        Dim delimiterIndex = currentText.LastIndexOfAny(New Char() {","c, ";"c, ControlChars.Cr, ControlChars.Lf})
        If delimiterIndex < 0 Then Return selectedText

        Dim prefix = currentText.Substring(0, delimiterIndex + 1).TrimEnd()
        If Not prefix.EndsWith(" ", StringComparison.Ordinal) Then
            prefix &= " "
        End If

        Return prefix & selectedText
    End Function
End Class
