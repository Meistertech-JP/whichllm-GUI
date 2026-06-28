# whichllm GUI

Windows 10 / 11 向けの、ローカルLLM選びを助けるGUIアプリです。PCのCPU、RAM、GPU、VRAMを見ながら「このPCで動かしやすいモデル」を探します。

[English README](README.en.md)

![whichllm GUI Japanese screenshot](docs/images/whichllm-gui-ja.png)

## これは何？

whichllm GUIは、モデルを実行する前に「どのローカルLLMを選ぶか」「今のPCで快適に動くか」「GPUを替えるとどう変わるか」を見るためのツールです。

対象は、個人の日常用途でPCを使うユーザーです。モデルの実行、ダウンロード、チャットUIはあえて入れていません。

## すぐ使う

1. Releasesから `whichllm-gui-v0.4.0-win-x64.zip` をダウンロードします。
2. ZIPを好きな場所に展開します。
3. `WhichLlm.Gui.exe` を起動します。
4. 最初の画面で `おすすめを探す` を押します。

Pythonは不要です。配布ZIPは `win-x64` 自己完結版です。

## 主な使い方

- `おすすめ`: このPCに合うモデルを、用途・快適度・速度・根拠つきで一覧表示します。
- `このPC`: 検出したCPU、RAM、GPU、VRAM、ディスク空きを確認します。
- `プラン`: 指定したモデルについて、量子化ごとの必要メモリを見積もります。
- `買い替え比較`: 候補GPUごとに、最有力モデルや伸び幅を比較します。
- `スニペット`: 開発者向けのPythonコードと `uv run --no-project ...` コマンドを生成します。
- `設定`: キャッシュ場所、Hugging Face接続先、表示言語を確認・変更します。

## おすすめ画面の見方

- `用途`: 日常、会話、プログラミング、論理・数学、画像、検索・分類から選びます。
- `快適度`: `快適` はGPUメモリ内に収まる見込み、`動くが重め` はGPUからあふれてCPU/RAMも使う見込みです。
- `速度の目安`: 普段使いに十分か、とても速いかをざっくり見ます。
- `根拠`: direct / variant / base_model / line_interp / self_reported / none を区別し、信頼度でスコアを減衰します。

## 主な特徴

- ベンチマーク根拠は direct、variant、base_model、line_interp、self_reported の順で探します。
- ベンチ情報源は LiveBench / Artificial Analysis / Aider / Open LLM Leaderboard / Chatbot Arena を使います。Open LLM Leaderboard と Arena は凍結ソースとして扱い、古い系譜には最新性の減衰をかけます。
- Hugging Face取得では、人気順に加えて、最近更新されたGGUFモデルとtrendingモデルも確認します。
- 2倍を超えるパラメータ規模の乖離がある派生、draft、MTP、fork系のベンチ継承を避けます。
- NVIDIA Compute Capability、AMD/Apple/IntelのOS/backend互換性警告をメモリ判定のnotesに表示します。
- 日本語 / English の表示切替に対応し、最後に選んだ表示言語を次回起動時にも使います。
- GPU候補は世代順に並べ、`QAT` は実在するQAT variantまたはrepo/file名にQATがある場合だけ扱います。

## ハードウェア検出

GPU検出は次の順で試します。

- NVIDIA: `nvidia-smi`
- AMD Radeon: `%HIP_PATH%\bin\hipInfo.exe`
- Intel Arcなど: `xpu-smi`
- 失敗時: WindowsのCIM/WMIとレジストリ情報

自動検出が不正確な場合は、画面上でVRAMや帯域を手入力して試算できます。

## データ取得とキャッシュ

モデル情報はHugging Face APIから取得します。`HF_ENDPOINT` が設定されている場合は、その接続先を使います。

取得時は人気順に加えて、最近更新されたGGUFモデルとtrendingモデルも確認します。

ベンチマーク情報は次の層で統合します。

- current: LiveBench、Artificial Analysis、Aider
- frozen: Open LLM Leaderboard v2、Chatbot Arena ELO
- fallback: ライブ取得が全滅した場合の最小seed

frozenだけに存在する古い系譜のモデルは、CLI版と同じ考え方で世代の古さに応じてスコアを減衰します。

インターネット接続も既存キャッシュも使えない場合は、初回起動時でも画面が空にならないよう、主要な小型〜中型モデルの最小fallback候補を使います。

キャッシュは次の場所に保存されます。

```text
%LocalAppData%\whichllm-gui\cache
```

キャッシュの有効期間:

- モデル情報: 6時間
- ベンチマーク情報: 24時間

## 開発

開発には.NET SDKが必要です。

```powershell
dotnet restore
dotnet test tests\WhichLlm.Tests\WhichLlm.Tests.vbproj
dotnet build src\WhichLlm.Gui\WhichLlm.Gui.vbproj
dotnet publish src\WhichLlm.Gui\WhichLlm.Gui.vbproj -c Release -r win-x64 --self-contained true
```

publish結果は次に出力されます。

```text
src\WhichLlm.Gui\bin\Release\net10.0-windows\win-x64\publish\
```

## 参照元とライセンス

whichllm GUI 本体は MIT License で公開します。詳細は [LICENSE](LICENSE) を参照してください。

このGUIは、次のプロジェクトを参考にしています。

- whichllm: https://github.com/Andyyyy64/whichllm
- llmfit: https://github.com/AlexsJones/llmfit

参照元プロジェクトの著作権表示は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。
