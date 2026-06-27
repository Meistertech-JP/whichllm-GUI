Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto

Namespace Services
    Public Class SnippetGenerator
        Implements ISnippetGenerator

        Public Function Generate(model As ModelInfo, Optional quant As String = "") As SnippetResult Implements ISnippetGenerator.Generate
            Dim modelVariant = ChooseVariant(model, quant)
            Dim result As New SnippetResult With {.Model = model, .SelectedVariant = modelVariant}

            If modelVariant.RuntimeKind.Equals("gguf", StringComparison.OrdinalIgnoreCase) Then
                Dim fileName = If(String.IsNullOrWhiteSpace(modelVariant.FileName), $"*{modelVariant.Quantization}*.gguf", modelVariant.FileName)
                result.CommandLine = "uv run --no-project --with llama-cpp-python --with huggingface-hub script.py"
                result.Code = String.Join(vbCrLf, New String() {
                    "from huggingface_hub import hf_hub_download",
                    "from llama_cpp import Llama",
                    "",
                    $"repo_id = ""{EscapePython(model.RepoId)}""",
                    $"filename = ""{EscapePython(fileName)}""",
                    "model_path = hf_hub_download(repo_id=repo_id, filename=filename)",
                    "llm = Llama(model_path=model_path, n_ctx=4096, n_gpu_layers=-1)",
                    "",
                    "messages = [{""role"": ""user"", ""content"": ""Hello!""}]",
                    "response = llm.create_chat_completion(messages=messages)",
                    "print(response[""choices""][0][""message""][""content""])"})
            Else
                result.CommandLine = "uv run --no-project --with transformers --with torch --with accelerate script.py"
                result.Code = String.Join(vbCrLf, New String() {
                    "import torch",
                    "from transformers import AutoModelForCausalLM, AutoTokenizer",
                    "",
                    $"repo_id = ""{EscapePython(model.RepoId)}""",
                    "tokenizer = AutoTokenizer.from_pretrained(repo_id, trust_remote_code=True)",
                    "model = AutoModelForCausalLM.from_pretrained(",
                    "    repo_id,",
                    "    torch_dtype=torch.float16,",
                    "    device_map=""auto"",",
                    "    trust_remote_code=True,",
                    ")",
                    "prompt = ""Hello!""",
                    "inputs = tokenizer(prompt, return_tensors=""pt"").to(model.device)",
                    "outputs = model.generate(**inputs, max_new_tokens=128)",
                    "print(tokenizer.decode(outputs[0], skip_special_tokens=True))"})
            End If

            If modelVariant.IsSynthetic Then
                result.Warnings.Add("Variant is synthetic; confirm the exact model file in HuggingFace before running.")
            End If
            Return result
        End Function

        Private Shared Function ChooseVariant(model As ModelInfo, quant As String) As ModelVariant
            If Not String.IsNullOrWhiteSpace(quant) Then
                Dim exact = model.Variants.FirstOrDefault(Function(v) v.Quantization.Equals(quant, StringComparison.OrdinalIgnoreCase))
                If exact IsNot Nothing Then Return exact
                Return New ModelVariant With {.Quantization = quant, .RuntimeKind = "gguf", .IsSynthetic = True}
            End If

            Dim preferred = New String() {"Q4_K_M", "Q4_K_S", "Q5_K_M", "Q5_K_S", "Q6_K", "Q3_K_M", "Q8_0"}
            For Each q In preferred
                Dim match = model.Variants.FirstOrDefault(Function(v) v.Quantization.Equals(q, StringComparison.OrdinalIgnoreCase))
                If match IsNot Nothing Then Return match
            Next
            If model.Variants.Count > 0 Then Return model.Variants(0)
            Return New ModelVariant With {.Quantization = "Q4_K_M", .RuntimeKind = "gguf", .IsSynthetic = True}
        End Function

        Private Shared Function EscapePython(value As String) As String
            Return value.Replace("\", "\\").Replace("""", "\""")
        End Function
    End Class
End Namespace
