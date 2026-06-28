Option Strict On
Option Explicit On

Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Data
Imports System.Windows.Media
Imports System.Windows.Threading
Imports WhichLlm.Gui.Infrastructure
Imports WhichLlm.Gui.ViewModels

Class MainWindow
    Private ReadOnly _viewModel As New MainViewModel()

    Public Sub New()
        InitializeComponent()
        DataContext = _viewModel
        AddHandler Loaded, AddressOf MainWindow_Loaded
        AddHandler _viewModel.LanguageChanged, AddressOf ViewModel_LanguageChanged
    End Sub

    Private Async Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler Loaded, AddressOf MainWindow_Loaded
        ApplyStaticLocalization()
        Await _viewModel.InitializeAsync()
    End Sub

    Private Sub ViewModel_LanguageChanged(sender As Object, e As EventArgs)
        ApplyStaticLocalization()
    End Sub

    Private Sub MainTabs_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If Not ReferenceEquals(e.OriginalSource, sender) Then Return
        Dispatcher.BeginInvoke(Sub() ApplyStaticLocalization(), DispatcherPriority.Loaded)
    End Sub

    Private Sub LocalizedContent_Expanded(sender As Object, e As RoutedEventArgs)
        Dim root = TryCast(sender, DependencyObject)
        If root Is Nothing Then Return

        Dispatcher.BeginInvoke(
            Sub()
                LocalizeDependencyObject(root)
                LocalizeDataGridColumns(Me)
            End Sub,
            DispatcherPriority.Loaded)
    End Sub

    Private Sub EditableSuggestionComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim combo = TryCast(sender, ComboBox)
        If combo Is Nothing OrElse e.AddedItems.Count = 0 Then Return

        Dim selectedText = TryCast(e.AddedItems(0), String)
        If String.IsNullOrWhiteSpace(selectedText) Then Return

        Dim nextText = ReplaceLastInputSegment(combo.Text, selectedText)
        combo.Dispatcher.BeginInvoke(
            Sub()
                CommitComboText(combo, nextText)
                combo.SelectedItem = Nothing
                CommitComboText(combo, nextText)
            End Sub,
            DispatcherPriority.ApplicationIdle)
    End Sub

    Private Shared Sub CommitComboText(combo As ComboBox, value As String)
        combo.Text = value

        Dim expression = combo.GetBindingExpression(ComboBox.TextProperty)
        expression?.UpdateSource()

        Dim path = expression?.ParentBinding?.Path?.Path
        If String.IsNullOrWhiteSpace(path) OrElse path.Contains(".", StringComparison.Ordinal) Then Return

        Dim dataItem = expression.DataItem
        If dataItem Is Nothing Then Return

        Dim prop = dataItem.GetType().GetProperty(path)
        If prop Is Nothing OrElse Not prop.CanWrite OrElse prop.PropertyType IsNot GetType(String) Then Return

        prop.SetValue(dataItem, value)
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

    Private Sub ApplyStaticLocalization()
        LocalizeDependencyObject(Me)
        LocalizeDataGridColumns(Me)
    End Sub

    Private Shared Sub LocalizeDependencyObject(root As DependencyObject)
        If root Is Nothing Then Return

        Dim textBlock = TryCast(root, TextBlock)
        If textBlock IsNot Nothing AndAlso BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty) Is Nothing Then
            Dim original = OriginalValue(textBlock, "Text", textBlock.Text)
            textBlock.Text = AppText.StaticText(original)
        End If

        Dim contentControl = TryCast(root, ContentControl)
        If contentControl IsNot Nothing AndAlso BindingOperations.GetBindingExpression(contentControl, ContentControl.ContentProperty) Is Nothing Then
            Dim content = TryCast(contentControl.Content, String)
            If content IsNot Nothing Then
                Dim original = OriginalValue(contentControl, "Content", content)
                contentControl.Content = AppText.StaticText(original)
            End If
        End If

        Dim headered = TryCast(root, HeaderedContentControl)
        If headered IsNot Nothing AndAlso BindingOperations.GetBindingExpression(headered, HeaderedContentControl.HeaderProperty) Is Nothing Then
            Dim header = TryCast(headered.Header, String)
            If header IsNot Nothing Then
                Dim original = OriginalValue(headered, "Header", header)
                headered.Header = AppText.StaticText(original)
            End If
        End If

        Dim menuItem = TryCast(root, MenuItem)
        If menuItem IsNot Nothing AndAlso BindingOperations.GetBindingExpression(menuItem, MenuItem.HeaderProperty) Is Nothing Then
            Dim header = TryCast(menuItem.Header, String)
            If header IsNot Nothing Then
                Dim original = OriginalValue(menuItem, "Header", header)
                menuItem.Header = AppText.StaticText(original)
            End If
        End If

        Dim framework = TryCast(root, FrameworkElement)
        If framework IsNot Nothing Then
            Dim tooltipText = TryCast(framework.ToolTip, String)
            If tooltipText IsNot Nothing Then
                Dim original = OriginalValue(framework, "ToolTip", tooltipText)
                framework.ToolTip = AppText.StaticText(original)
            End If
        End If

        Dim childCount = VisualTreeHelper.GetChildrenCount(root)
        For index = 0 To childCount - 1
            LocalizeDependencyObject(VisualTreeHelper.GetChild(root, index))
        Next
    End Sub

    Private Shared Sub LocalizeDataGridColumns(root As DependencyObject)
        For Each grid In FindVisualChildren(Of DataGrid)(root)
            For Each column In grid.Columns
                Dim headerText = TryCast(column.Header, String)
                If headerText IsNot Nothing Then
                    Dim original = OriginalValue(column, "Header", headerText)
                    column.Header = AppText.StaticText(original)
                End If

                Dim headerBlock = TryCast(column.Header, TextBlock)
                If headerBlock IsNot Nothing Then
                    Dim originalText = OriginalValue(headerBlock, "Text", headerBlock.Text)
                    headerBlock.Text = AppText.StaticText(originalText)
                    Dim tooltipText = TryCast(headerBlock.ToolTip, String)
                    If tooltipText IsNot Nothing Then
                        Dim originalTip = OriginalValue(headerBlock, "ToolTip", tooltipText)
                        headerBlock.ToolTip = AppText.StaticText(originalTip)
                    End If
                End If
            Next
        Next
    End Sub

    Private Shared Function OriginalValue(target As Object, key As String, currentValue As String) As String
        Dim values = OriginalValues.GetOrCreateValue(target)
        Dim stored As String = Nothing
        If values.TextByKey.TryGetValue(key, stored) Then Return stored
        values.TextByKey(key) = currentValue
        Return currentValue
    End Function

    Private Shared Iterator Function FindVisualChildren(Of T As DependencyObject)(root As DependencyObject) As IEnumerable(Of T)
        If root Is Nothing Then Return
        For index = 0 To VisualTreeHelper.GetChildrenCount(root) - 1
            Dim child = VisualTreeHelper.GetChild(root, index)
            Dim typed = TryCast(child, T)
            If typed IsNot Nothing Then Yield typed
            For Each descendant In FindVisualChildren(Of T)(child)
                Yield descendant
            Next
        Next
    End Function

    Private Shared ReadOnly OriginalValues As New ConditionalWeakTable(Of Object, LocalizedOriginals)()

    Private Class LocalizedOriginals
        Public ReadOnly Property TextByKey As New Dictionary(Of String, String)(StringComparer.Ordinal)
    End Class
End Class
