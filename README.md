# whichllm GUI

VB.NET/WPF implementation of a Windows-first GUI for hardware-aware local LLM recommendations.

## Scope

- Target OS: x86-64 Windows 10 / 11.
- Runtime: `net10.0-windows`.
- Distribution target: portable `win-x64` self-contained publish.
- `run` is intentionally not supported in v1.
- Supported views: recommendations, hardware detection, model planning, GPU upgrade comparison, and Python snippet generation.

## Features

- Reimplements the main whichllm recommendation flow in VB.NET without calling the Python CLI at runtime.
- Detects Windows CPU/RAM/disk/GPU information with `nvidia-smi` and CIM/WMI fallback.
- Fetches HuggingFace model metadata, caches it under `%LocalAppData%\whichllm-gui\cache`, and supports `HF_ENDPOINT`.
- Ranks candidates by fit, estimated speed, quantization, benchmark evidence, model lineage, and source trust.
- Adds llmfit-style use-case selection for `general`, `chat`, `coding`, `reasoning`, `multimodal`, and `embedding`.
- Exports recommendation results as Markdown or JSON with field names close to upstream whichllm JSON.

## Build

```powershell
dotnet restore
dotnet test tests\WhichLlm.Tests\WhichLlm.Tests.vbproj
dotnet build src\WhichLlm.Gui\WhichLlm.Gui.vbproj
dotnet publish src\WhichLlm.Gui\WhichLlm.Gui.vbproj -c Release -r win-x64 --self-contained true
```

The published executable is written under:

```text
src\WhichLlm.Gui\bin\Release\net10.0-windows\win-x64\publish\
```

## Third-Party Notice

See `THIRD_PARTY_NOTICES.md` for upstream project and license references.
