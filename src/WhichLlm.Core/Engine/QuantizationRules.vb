Option Strict On
Option Explicit On

Namespace Engine
    Public Module QuantizationRules
        Private ReadOnly Penalties As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase) From {
            {"Q8_0", 0.01R},
            {"Q6_K", 0.02R},
            {"Q5_K_M", 0.03R},
            {"Q5_K_S", 0.035R},
            {"Q4_K_M", 0.05R},
            {"Q4_K_S", 0.06R},
            {"Q3_K_M", 0.08R},
            {"Q3_K_L", 0.09R},
            {"Q2_K", 0.25R},
            {"IQ2_XXS", 0.4R},
            {"Q1_0", 0.55R},
            {"F16", 0.0R},
            {"BF16", 0.0R},
            {"FP16", 0.0R},
            {"FP8", 0.02R},
            {"QAT", 0.05R},
            {"AWQ", 0.05R},
            {"GPTQ", 0.06R}
        }

        Private ReadOnly BytesPerParameter As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase) From {
            {"Q8_0", 1.06R},
            {"Q6_K", 0.78R},
            {"Q5_K_M", 0.69R},
            {"Q5_K_S", 0.66R},
            {"Q4_K_M", 0.57R},
            {"Q4_K_S", 0.53R},
            {"Q3_K_M", 0.44R},
            {"Q3_K_L", 0.47R},
            {"Q2_K", 0.32R},
            {"IQ2_XXS", 0.28R},
            {"Q1_0", 0.18R},
            {"F16", 2.0R},
            {"BF16", 2.0R},
            {"FP16", 2.0R},
            {"FP8", 1.05R},
            {"QAT", 0.57R},
            {"AWQ", 0.65R},
            {"GPTQ", 0.65R}
        }

        Public Function QuantPenalty(quantization As String) As Double
            If String.IsNullOrWhiteSpace(quantization) Then Return 0.05R
            Dim value As Double = 0.05R
            If Penalties.TryGetValue(quantization, value) Then Return value
            If quantization.StartsWith("Q4", StringComparison.OrdinalIgnoreCase) Then Return 0.05R
            If quantization.StartsWith("Q5", StringComparison.OrdinalIgnoreCase) Then Return 0.03R
            If quantization.StartsWith("Q6", StringComparison.OrdinalIgnoreCase) Then Return 0.02R
            If quantization.StartsWith("Q8", StringComparison.OrdinalIgnoreCase) Then Return 0.01R
            If IsQat(quantization) Then Return 0.05R
            If quantization.Contains("AWQ", StringComparison.OrdinalIgnoreCase) Then Return 0.05R
            If quantization.Contains("GPTQ", StringComparison.OrdinalIgnoreCase) Then Return 0.06R
            Return 0.05R
        End Function

        Public Function BytesPerParam(quantization As String) As Double
            If String.IsNullOrWhiteSpace(quantization) Then Return 0.57R
            Dim value As Double = 0.57R
            If BytesPerParameter.TryGetValue(quantization, value) Then Return value
            If quantization.StartsWith("Q4", StringComparison.OrdinalIgnoreCase) Then Return 0.57R
            If quantization.StartsWith("Q5", StringComparison.OrdinalIgnoreCase) Then Return 0.69R
            If quantization.StartsWith("Q6", StringComparison.OrdinalIgnoreCase) Then Return 0.78R
            If quantization.StartsWith("Q8", StringComparison.OrdinalIgnoreCase) Then Return 1.06R
            If IsQat(quantization) Then Return 0.57R
            If quantization.Contains("AWQ", StringComparison.OrdinalIgnoreCase) OrElse quantization.Contains("GPTQ", StringComparison.OrdinalIgnoreCase) Then Return 0.65R
            Return 0.57R
        End Function

        Public Function IsQat(quantization As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(quantization) AndAlso
                quantization.Contains("QAT", StringComparison.OrdinalIgnoreCase)
        End Function

        Public Function PreferredQuants() As IReadOnlyList(Of String)
            Return New List(Of String) From {"Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0"}
        End Function
    End Module
End Namespace
