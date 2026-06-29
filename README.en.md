# whichllm GUI

**A Windows app that reads your PC's specs and tells you which local LLMs are likely to run well on it.**
No Python install required — just unzip and launch.

[日本語 README](./README.md)

![whichllm GUI (English UI)](./docs/images/whichllm-gui-en.png)

---

## What is this?

You want to try local LLMs (AI models that run on your own PC), but you're not sure which model to pick or whether your machine can handle it. This tool helps with exactly that.

It auto-detects your CPU, RAM, GPU, and VRAM, then lists the models likely to run comfortably on your PC — with **use case, fit, speed, and the evidence behind each rating**.

### What it does

- Find models that suit your PC (`Recommend`)
- Review your detected hardware (`This PC`)
- Estimate required memory per quantization for a given model (`Plan`)
- Compare how a GPU upgrade would change things (`Upgrade compare`)
- Generate code to run a chosen model (`Snippet`)

### What it deliberately doesn't do

This app **does not download or run models**, and there's no chat UI.
It focuses on one thing: choosing before you run. How to actually run a model is covered [below](#after-you-choose-how-do-you-run-it).

---

## Getting it running (3 steps)

### 1. Download

Grab `whichllm-gui-vX.Y.Z-win-x64.zip` from the [latest release](https://github.com/Meistertech-JP/whichllm-GUI/releases/latest).
It's a self-contained build for Windows 10 / 11 (64-bit), so **you don't need to install Python or .NET separately**.

### 2. Unzip and launch

Extract the ZIP anywhere and double-click `WhichLlm.Gui.exe`.

> **If you see "Windows protected your PC"**
> This app isn't code-signed, so Windows SmartScreen may warn you on first launch. To run it after reviewing what it is, click **"More info" → "Run anyway"**.
> If you'd rather verify the download first, see "Verifying the download" below.

### 3. Just launch — recommendations appear automatically

On launch, hardware detection runs, and once it finishes **the list of models that suit your PC appears automatically**. You don't need to press anything. That's it.

### Verifying the download (optional)

Each release ships a SHA256 checksum for its ZIP. If you want to confirm the file isn't corrupted or tampered with, verify it in PowerShell:

```powershell
Get-FileHash .\whichllm-gui-vX.Y.Z-win-x64.zip -Algorithm SHA256
```

If the printed value matches the checksum listed on the release, you're good.

---

## Reading the results (Recommend screen)

Read the `Recommend` list along these axes:

- **Use case**: everyday / chat / programming / logic & math / image / search & classification. Only models that fit your goal remain.
- **Fit**:
  - `Comfortable` … expected to fit within GPU memory. Runs most smoothly.
  - `Runs but heavy` … expected to spill out of the GPU and also use CPU/RAM. It runs, but slower.
- **Speed estimate**: a rough sense of whether it's fine for everyday use or very fast.
- **Evidence**: distinguishes what each rating is based on (a real measurement vs. an estimate from a nearby lineage) and adjusts the score by confidence.

When in doubt, start with a model that's both `Comfortable` and matches your use case.

---

## After you choose, how do you run it?

This app doesn't run models, so you run your chosen model with a separate tool.

- **Want the easy path**: apps like [Ollama](https://ollama.com) or [LM Studio](https://lmstudio.ai) let you run models with little or no command-line or code.
- **Want to run it in code**: the `Snippet` tab generates Python code and a `uv run --no-project ...` command for your chosen model. Copy and use it as-is.

So the flow is: decide *what* to run in whichllm GUI, then actually run it with one of the above.

---

## The tabs

| Tab | What it does |
| --- | --- |
| `Recommend` | Lists models that suit this PC |
| `This PC` | Shows detected CPU / RAM / GPU / VRAM / free disk |
| `Plan` | Estimates required memory per quantization for a model |
| `Upgrade compare` | Compares top models and gains per GPU candidate |
| `Snippet` | Generates Python code/commands to run a model |
| `Settings` | Change cache location, Hugging Face endpoint, and language |

---

## Troubleshooting

- **GPU not detected / VRAM looks wrong**
  If auto-detection fails, you can type VRAM and bandwidth manually on the `This PC` screen and estimate from there.
  GPU detection is tried in this order: `nvidia-smi` (NVIDIA) → `hipInfo.exe` (AMD) → `xpu-smi` (Intel) → Windows WMI/registry.
- **You have multiple GPUs**
  All detected GPUs are shown, and the `Target GPU / group` selector on `Recommend` lets you choose what to estimate for: a whole same-generation group, a single specific card, or just some of N identical cards (e.g. 2 of 3, when you have 3+). Fit and speed are estimated for the configuration you pick. Mixed-generation or mixed-architecture GPUs are **not** treated as simple combined VRAM and judged "runnable."
- **No internet / first launch**
  When neither live fetch nor cache is available, a minimal set of common small-to-mid models is shown so the screen is never empty.
- **Change language / clear cache**
  Use the `Settings` tab to switch Japanese / English and check the cache location.

---

## How it works (data and cache)

Model info is fetched from the Hugging Face API. If the `HF_ENDPOINT` environment variable is set, that endpoint is used. Beyond popularity, recently updated GGUF models and trending models are also picked up.

Benchmark info is layered across several sources:

- **current**: LiveBench / Artificial Analysis / Aider
- **frozen**: Open LLM Leaderboard v2 / Chatbot Arena ELO
- **fallback**: a minimal seed for when all live fetches fail

Models that exist only in frozen sources have their scores decayed by how old their lineage is, to avoid overrating them.

Cache location and lifetimes:

```
%LocalAppData%\whichllm-gui\cache
```

- Model info: 6 hours
- Benchmark info: 24 hours

---

## For developers

Building requires the .NET SDK.

```powershell
dotnet restore
dotnet test tests\WhichLlm.Tests\WhichLlm.Tests.vbproj
dotnet build src\WhichLlm.Gui\WhichLlm.Gui.vbproj
dotnet publish src\WhichLlm.Gui\WhichLlm.Gui.vbproj -c Release -r win-x64 --self-contained true
```

Publish output:

```
src\WhichLlm.Gui\bin\Release\net10.0-windows\win-x64\publish\
```

See [Releases](https://github.com/Meistertech-JP/whichllm-GUI/releases) for per-version changes.

---

## License and credits

whichllm GUI itself is released under the MIT License. See [LICENSE](./LICENSE) for details.

This GUI is inspired by:

- whichllm: <https://github.com/Andyyyy64/whichllm>
- llmfit: <https://github.com/AlexsJones/llmfit>

For the copyright notices of the referenced projects, see [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md).
