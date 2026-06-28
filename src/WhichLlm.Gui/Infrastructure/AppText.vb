Option Strict On
Option Explicit On

Namespace Infrastructure
    Public Module AppText
        Public Const Japanese As String = "ja"
        Public Const English As String = "en"

        Public Property CurrentLanguage As String = InitialLanguage()

        Public ReadOnly Property IsEnglish As Boolean
            Get
                Return String.Equals(CurrentLanguage, English, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public Function Text(ja As String, en As String) As String
            Return If(IsEnglish, en, ja)
        End Function

        Public Function InitialLanguage() As String
            Dim fromEnvironment = Environment.GetEnvironmentVariable("WHICHLLM_GUI_LANG")
            If String.Equals(fromEnvironment, English, StringComparison.OrdinalIgnoreCase) Then Return English
            If String.Equals(fromEnvironment, Japanese, StringComparison.OrdinalIgnoreCase) Then Return Japanese
            Return Japanese
        End Function

        Public Function StaticText(value As String) As String
            If String.IsNullOrWhiteSpace(value) OrElse Not IsEnglish Then Return value
            Dim translated As String = Nothing
            If StaticEnglish.TryGetValue(value, translated) Then Return translated
            Return value
        End Function

        Private ReadOnly StaticEnglish As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"おすすめ", "Recommendations"},
            {"このPCに合うモデル", "Models That Fit This PC"},
            {"用途を選ぶだけで、動きやすさと根拠を合わせて並べます。", "Choose a use case and compare fit, speed, and evidence."},
            {"おすすめを探す", "Find Recommendations"},
            {"表示中のCPU、RAM、GPU、VRAMを前提に、このPCに合うモデルを探します。", "Find models that fit the CPU, RAM, GPU, and VRAM shown here."},
            {"表示中のおすすめを、ほかのアプリに貼り付けられる形でコピーします。", "Copy the current recommendations in a format you can paste into another app."},
            {"この結果をコピー  ▾", "Copy Results  ▾"},
            {"一覧表としてコピー", "Copy as Table"},
            {"見やすい表の形でコピーします。メールやメモにそのまま貼れます。", "Copy a readable table for email or notes."},
            {"データ（JSON）としてコピー", "Copy as JSON"},
            {"プログラムで扱えるJSON形式でコピーします。", "Copy machine-readable JSON."},
            {"詳細条件", "Advanced Filters"},
            {"表示件数", "Rows"},
            {"一覧に出す候補数です。迷う場合は10のままで大丈夫です。", "How many candidates to show. Ten is a good default."},
            {"文脈の長さ", "Context Length"},
            {"一度に扱える文章量です。4096は軽め、長い文章を扱うなら8192以上が目安です。", "How much text the model can handle at once. 4096 is light; use 8192+ for longer work."},
            {"用途", "Use Case"},
            {"モデルを何に使いたいかです。ランキングの重みと絞り込みに使います。", "What you want to use the model for. This affects ranking and filtering."},
            {"根拠", "Evidence"},
            {"直接ベンチマークだけに絞るか、近いモデルの推定や根拠なし候補も含めるかを選びます。", "Choose whether to require direct benchmarks or include close estimates and unevidenced models."},
            {"快適度の条件", "Fit Filter"},
            {"CPUだけも許すか、GPUを使う候補だけにするか、GPUメモリ内に収まる候補だけにするかを選びます。", "Choose whether CPU-only is allowed, GPU use is required, or the whole model must fit in GPU memory."},
            {"速度の条件", "Speed Filter"},
            {"tok/sは1秒あたりに生成できるトークン数です。10以上なら普段使い、30以上ならかなり速めの目安です。", "tok/s is generated tokens per second. 10+ is usable; 30+ feels fast."},
            {"量子化", "Quantization"},
            {"Q4_K_Mなどの圧縮形式です。空欄ならおすすめの形式を自動で見ます。", "Compression format such as Q4_K_M. Leave blank to let the app choose."},
            {"GPUを試算", "Simulate GPU"},
            {"別のGPUを買った場合などを試す入力です。例: RTX 4070, 2x RTX 4090", "Try another GPU, for example RTX 4070 or 2x RTX 4090."},
            {"CPUのみ", "CPU only"},
            {"GPUを使わない前提で候補を探します。", "Find models assuming no GPU acceleration."},
            {"再取得", "Refresh"},
            {"保存済みデータを使わず、モデル情報を取り直します。", "Ignore cached data and fetch model metadata again."},
            {"総合おすすめ順の順位です。最有力がいちばんのおすすめです。", "Overall recommendation rank. Best pick is the top choice."},
            {"モデル", "Model"},
            {"モデルの名前です。", "Model name."},
            {"この候補が得意とする用途です。", "The use case this candidate is suited for."},
            {"快適度", "Fit"},
            {"このPCでどれくらい無理なく動くかの目安です。緑=快適、橙=動くが重め、灰=要確認。", "How comfortably it should run on this PC. Green=comfortable, orange=heavy, gray=check."},
            {"速度の目安", "Speed"},
            {"返事の速さの目安です。詳しい数値は右の詳細に出ます。", "Estimated response speed. Exact values are shown in the details pane."},
            {"「おすすめを探す」を押すと、このPCで使えるモデルが一覧で出ます。", "Press Find Recommendations to list models that can run on this PC."},
            {"このPC", "This PC"},
            {"このPCの余力", "This PC's Capacity"},
            {"GPU、メモリ、CPUを確認して、推薦の前提に使います。", "Review GPU, memory, and CPU information used for recommendations."},
            {"再検出", "Detect Again"},
            {"CPU、メモリ、GPUをもう一度読み取ります。", "Read CPU, memory, and GPU information again."},
            {"手入力で調整", "Manual Overrides"},
            {"GPUメモリ上書き(GB)", "Override VRAM (GB)"},
            {"自動検出が違う時に、GPUメモリをGB単位で指定します。", "Set GPU memory in GB when automatic detection is wrong."},
            {"帯域(GB/s)", "Bandwidth (GB/s)"},
            {"GPUメモリの速さです。分かる場合だけ入力します。", "GPU memory bandwidth. Enter only if you know it."},
            {"GPUメモリ予約", "VRAM Reserve"},
            {"OSや他のアプリのために空けておくGPUメモリです。autoで安全寄りに計算します。", "GPU memory reserved for the OS and other apps. auto is conservative."},
            {"メインメモリ予算", "RAM Budget"},
            {"CPUや一部オフロードで使ってよいメモリ量です。availableで現在の空きメモリを使います。", "System RAM allowed for CPU or partial offload. available uses current free RAM."},
            {"グラフィック", "Graphics"},
            {"メーカー", "Vendor"},
            {"専用メモリ", "Dedicated Memory"},
            {"GPU専用のメモリ量（VRAM）です。モデルがここに収まるほど快適に動きます。", "Dedicated GPU memory (VRAM). Models run best when they fit here."},
            {"使える目安", "Usable"},
            {"OSなどの使用分を差し引いた、実際にモデルに使えるメモリの目安です。", "Estimated memory available to models after OS and app reserve."},
            {"メモリ速度", "Memory Speed"},
            {"GPUメモリの速さ（GB/s）です。大きいほど生成が速くなりやすいです。", "GPU memory bandwidth in GB/s. Higher usually means faster generation."},
            {"種類", "Type"},
            {"「専用」はGPU独自のメモリ、「共有」はメインメモリと共用するタイプです。", "Dedicated means GPU-only memory; shared uses system RAM."},
            {"メモ", "Notes"},
            {"プラン", "Plan"},
            {"動かす前のプラン", "Plan Before You Run"},
            {"プランを作成", "Create Plan"},
            {"指定したモデルについて、量子化ごとの必要メモリを見積もります。", "Estimate required memory for each quantization of the selected model."},
            {"モデル名", "Model Name"},
            {"探したいモデル名の一部です。例: llama 3 8b", "Part of the model name. Example: llama 3 8b"},
            {"Q4_K_Mなどの形式です。空欄なら代表的な形式をまとめて見積もります。", "Format such as Q4_K_M. Leave blank to estimate common formats."},
            {"長い文章を扱うほど必要メモリが増えます。", "Longer context needs more memory."},
            {"必要メモリ", "Required Memory"},
            {"合いやすいGPU", "Balanced GPUs"},
            {"買い替え比較", "Upgrade Compare"},
            {"GPUを替えたらどう変わるか", "How a GPU Upgrade Changes Things"},
            {"比較", "Compare"},
            {"候補GPUごとに、最有力モデルと伸び幅を比べます。", "Compare the top model and score gain for each candidate GPU."},
            {"比較するGPU", "GPUs to Compare"},
            {"カンマ区切りで複数入力できます。例: RTX 4070, RX 7900 XTX", "Enter multiple GPUs separated by commas. Example: RTX 4070, RX 7900 XTX"},
            {"最有力モデル", "Top Model"},
            {"スコア", "Score"},
            {"速度目安", "Speed"},
            {"伸び幅", "Gain"},
            {"スニペット", "Snippet"},
            {"開発者向けスニペット", "Developer Snippet"},
            {"モデルを動かすためのPythonコードとuvコマンドを作ります。", "Generate Python code and a uv command to run the model."},
            {"生成", "Generate"},
            {"指定したモデルと量子化でコードを生成します。", "Generate code for the selected model and quantization."},
            {"コピー", "Copy"},
            {"コマンドとコードをまとめてコピーします。", "Copy the command and code together."},
            {"探したいモデル名の一部です。例: qwen 7b", "Part of the model name. Example: qwen 7b"},
            {"GGUFの量子化形式です。例: Q4_K_M", "GGUF quantization format. Example: Q4_K_M"},
            {"設定", "Settings"},
            {"データの保存場所", "Data Folder"},
            {"取得したモデル情報とベンチマーク情報を保存する場所です。", "Folder for cached model and benchmark data."},
            {"接続先（通常は変更不要）", "Endpoint (usually leave as is)"},
            {"モデル情報の取得先（HF_ENDPOINT）です。通常はこのままで使います。", "Model metadata endpoint (HF_ENDPOINT). Usually leave this unchanged."},
            {"表示言語", "Display Language"},
            {"画面に表示する言語です。", "Language used by the app UI."}
        }
    End Module
End Namespace
