# whichllm GUI

Windows 10 / 11 向けの、ローカルLLM選びを助けるGUIアプリです。

このアプリは、PCのCPU、RAM、GPU、VRAMを見ながら「このPCで動かしやすいモデル」を探します。対象は、個人の日常用途でPCを使うユーザーです。モデルの実行やダウンロードは行わず、推薦、見積もり、比較、スニペット生成に絞っています。

[English README](README.en.md)

## 対応環境

- OS: x86-64 Windows 10 / 11
- ランタイム: `net10.0-windows`
- 配布形式: `win-x64` 自己完結publishをZIP化
- バージョン: v0.1.0

## できること

- このPCに合うローカルLLM候補を一覧表示
- CPU、RAM、GPU、VRAM、ディスク空き容量の確認
- モデルと量子化ごとの必要メモリ見積もり
- GPUを買い替えた場合の比較
- Pythonスニペットと `uv run --no-project ...` コマンドの生成
- 推薦結果のMarkdown / JSONコピー
- 用途による絞り込み
  - 日常の文章と調べ物
  - 会話
  - プログラミング
  - 論理・数学
  - 画像も使う
  - 検索・分類

## できないこと

- モデルの実行
- モデルのダウンロード
- チャットUIとしての利用
- `whichllm run` 相当の機能

このアプリは、実行環境を作る前の「どれを選ぶか」「今のPCで足りるか」を見るためのツールです。

## 画面

- おすすめ: このPCに合うモデルを用途、快適度、根拠付きで表示します。
- このPC: 検出したハードウェア情報を確認します。
- プラン: 指定したモデルの量子化別メモリ見積もりを出します。
- 買い替え比較: 候補GPUごとの変化を比較します。
- スニペット: 開発者向けのPythonコードと実行コマンドを生成します。
- 設定: キャッシュ場所とHugging Face接続先を表示します。

## データ取得とキャッシュ

モデル情報はHugging Face APIから取得します。`HF_ENDPOINT` が設定されている場合は、その接続先を使います。

キャッシュは次の場所に保存されます。

```text
%LocalAppData%\whichllm-gui\cache
```

キャッシュの有効期間:

- モデル情報: 6時間
- ベンチマーク情報: 24時間

インターネットに接続できない場合は、既存キャッシュがあればそれを使います。キャッシュがない場合は取得失敗として画面に表示されます。

## ハードウェア検出

GPU検出は次の順で試します。

- NVIDIA: `nvidia-smi`
- AMD Radeon: `%HIP_PATH%\bin\hipInfo.exe`
- Intel Arcなど: `xpu-smi`
- 失敗時: WindowsのCIM/WMIとレジストリ情報

自動検出が不正確な場合は、画面上でVRAMや帯域を手入力して試算できます。

## ビルド

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

## 配布時に含めるもの

- `WhichLlm.Gui.exe`
- publishフォルダ内の依存ファイル一式
- `README.md`
- `README.en.md`
- `THIRD_PARTY_NOTICES.md`

## ライセンスと参照元

このGUIは、次のプロジェクトを参考にしています。

- whichllm: https://github.com/Andyyyy64/whichllm
- llmfit: https://github.com/AlexsJones/llmfit

詳細は `THIRD_PARTY_NOTICES.md` を参照してください。
