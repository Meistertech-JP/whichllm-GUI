# whichllm GUI

Windows 10 / 11 向けの、ローカルLLM選びを助けるGUIアプリです。PCのCPU、RAM、GPU、VRAMを見ながら「このPCで動かしやすいモデル」を探します。

[English README](README.en.md)

![whichllm GUI Japanese screenshot](docs/images/whichllm-gui-ja.png)

## これは何？

whichllm GUIは、モデルを実行する前に「どのローカルLLMを選ぶか」「今のPCで快適に動くか」「GPUを替えるとどう変わるか」を見るためのツールです。

対象は、個人の日常用途でPCを使うユーザーです。モデルの実行、ダウンロード、チャットUIはあえて入れていません。

## すぐ使う

1. Releasesから `whichllm-gui-v0.3.0-win-x64.zip` をダウンロードします。
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
- `根拠`: 内蔵ベンチ、近いモデル、自己申告、根拠なしを区別して表示します。

## v0.3.0のポイント

- 日本語 / English の表示切替を追加しました。
- GPU候補を世代順に並べました。
- `QAT` は実在するQAT variantまたはrepo/file名にQATがある場合だけ扱います。
- 右側の詳細ペインで長文が折り返されるようにしました。
- Windows環境変数が欠ける起動元でもWPFフォント初期化で落ちないよう補正しました。

## ハードウェア検出

GPU検出は次の順で試します。

- NVIDIA: `nvidia-smi`
- AMD Radeon: `%HIP_PATH%\bin\hipInfo.exe`
- Intel Arcなど: `xpu-smi`
- 失敗時: WindowsのCIM/WMIとレジストリ情報

自動検出が不正確な場合は、画面上でVRAMや帯域を手入力して試算できます。

## データ取得とキャッシュ

モデル情報はHugging Face APIから取得します。`HF_ENDPOINT` が設定されている場合は、その接続先を使います。

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

このGUIは、次のプロジェクトを参考にしています。

- whichllm: https://github.com/Andyyyy64/whichllm
- llmfit: https://github.com/AlexsJones/llmfit

詳細は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。
