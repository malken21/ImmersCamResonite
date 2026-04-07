# Reso360Spout2 開発知見メモ

このファイルは後続のエージェントや開発者のための技術的知見のまとめです。

---

## プロジェクト構成

Resoniteは2プロセス構成（Renderite）で動作する。

| プロセス | 実行ファイル | BepInEx | .NET | 役割 |
|---|---|---|---|---|
| Host | `Resonite.exe` | BepInEx 6 (net10.0) | .NET Core | FrooxEngineへのアクセス、共有メモリ書き込み |
| Renderer | `Renderer/Renderite.Renderer.exe` | BepInEx 5 (net472) | Mono (Unity 2019.4) | 360度レンダリング、Spout2送信 |

### ソリューション構成

```
Reso360Spout2.sln
├── Reso360Spout2/            ← Host プラグイン (BepInEx 6 / net10.0)
├── Reso360Spout2.Renderer/   ← Renderer プラグイン (BepInEx 5 / net472)
└── Reso360Spout2.Patcher/    ← Renderer プリローダーパッチャー (net472)
```

---

## KlakSpout DLL の扱いに関する重要知見

### 問題の本質

KlakSpoutのネイティブDLLは Unity の `UnityPluginLoad(IUnityInterfaces*)` が呼ばれることで D3D11 デバイスを取得する。このコールバックが呼ばれない場合、デバイスポインタが NULL となり `CreateSharedDX11Texture` が失敗する。

### NG: `LoadLibraryExW` での手動ロード

```csharp
// これは動かない
var handle = LoadLibraryExW(fullPath, IntPtr.Zero, 0);
[DllImport("KlakSpout_send")] static extern IntPtr CreateSender(...);
```

`LoadLibraryExW` でロードしても Unity は `UnityPluginLoad` を呼び出さない。
結果: `KlakSpout.dll` の場合は `CreateSender` が `IntPtr.Zero` を返す。`KlakSpout_send.dll` の場合は `CreateSharedDX11Texture NULL device` でクラッシュ。

### 解決策: Unity の Plugins フォルダへの配置

Unity は `Renderite.Renderer_Data/Plugins/x86_64/` 内の DLL に対してのみ起動時に `UnityPluginLoad` を呼び出す。

**実装**: BepInEx プリローダーパッチャー（`Reso360Spout2.Patcher`）が Unity 初期化より前に `KlakSpout_send.dll` をこのフォルダへコピーする。

```
起動順序:
1. Doorstop (winhttp.dll) が BepInEx プリローダーを起動
2. Patcher.Initialize() → KlakSpout_send.dll を Plugins/x86_64/ にコピー
3. Unity 初期化 → Plugins/x86_64/ の DLL をロード → UnityPluginLoad 呼び出し → D3D11 デバイス登録
4. BepInEx プラグインが起動 → [DllImport("KlakSpout_send")] → 既ロード済みモジュールを使用 → 動作
```

### DLL の役割分担

| DLL | サイズ | 用途 | 備考 |
|---|---|---|---|
| `KlakSpout_send.dll` | 220KB | Spout 送信専用 | `CreateSender`, `GetRenderEventFunc` など |
| `KlakSpout.dll` | 1MB | Spout 受信 + ユーティリティ | `CreateReceiver`, `ScanSharedObjects` など。`D3D11CreateDevice` を内包 |

`KlakSpout.dll` は自前で D3D11 デバイスを作れるが、`CreateSender` は Unity のデバイスが必要なため、`KlakSpout_send.dll` と役割分担する。

### `[DllImport]` の書き方

```csharp
// Sender: KlakSpout_send.dll から
[DllImport("KlakSpout_send", EntryPoint = "GetRenderEventFunc")]
internal static extern IntPtr Sender_GetRenderEventFunc();

[DllImport("KlakSpout_send", EntryPoint = "CreateSender")]
internal static extern IntPtr CreateSender(string name, int width, int height);

// Receiver / Utility: KlakSpout.dll から
[DllImport("KlakSpout")] internal static extern IntPtr CreateReceiver(string name);
[DllImport("KlakSpout")] internal static extern int ScanSharedObjects();
```

---

## CubemapRenderer のクラッシュ修正

### 症状

`D3DKMTOpenResource` でアクセス違反クラッシュ（`d3d11.dll` 内）。

### 原因

VR シングルパスステレオモードでは `BuiltinRenderTextureType.CameraTarget` がカメラの `targetTexture` ではなく VR 共有アイテクスチャに解決される。これをコマンドバッファのコンストラクタ内で使用するとクラッシュする。

### 修正

コマンドバッファを `_tempRT` 生成後に遅延構築し、`BuiltinRenderTextureType.CameraTarget` の代わりに `_tempRT` を直接参照する。

```csharp
// NG: コンストラクタでの BuiltinRenderTextureType.CameraTarget 使用
cb.SetGlobalTexture(tid, BuiltinRenderTextureType.CameraTarget);

// OK: _tempRT 生成後に RebuildCommandBuffers() を呼び、_tempRT を直接参照
cb.SetGlobalTexture(tid, _tempRT);
```

---

## Spout 送信フロー

```csharp
// 1. Sender を作成 (InitSpout 内、Start() からは呼ばない)
Plugin = PluginEntry.CreateSender("VRCam", width, height);

// 2. 毎フレーム Update イベントを発行 (render thread で共有テクスチャを初期化)
SpoutUtil.IssueSenderPluginEvent(PluginEntry.Event.Update, Plugin);

// 3. 共有テクスチャポインタ取得 (Update 発行後でないと NULL)
if (SharedTexture == null) {
    var ptr = PluginEntry.GetTexturePointer(Plugin);
    SharedTexture = Texture2D.CreateExternalTexture(w, h, TextureFormat.ARGB32, false, false, ptr);
}

// 4. 上下反転しつつコピー (ResoniteSpout 方式)
var tempRt = RenderTexture.GetTemporary(SharedTexture.width, SharedTexture.height, 0, RenderTextureFormat.ARGB32);
Graphics.Blit(SourceTexture, tempRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
Graphics.CopyTexture(tempRt, SharedTexture);
RenderTexture.ReleaseTemporary(tempRt);
```

**注意**: `Start()` から直接 `InitSpout()` を呼ぶと D3D デバイスが未初期化でクラッシュすることがある。`Update()` で数フレーム待ってから呼ぶこと（`_initDelayFrames`）。

---

## Thunderstore パッケージ構成

```toml
# パッチャー (Renderer/BepInEx/patchers/ に配置)
[[build.copy]]
source = "./Reso360Spout2.Patcher/bin/Release/net472/Reso360Spout2.Patcher.dll"
target = "Renderer/BepInEx/patchers/Reso360Spout2.Patcher/"

[[build.copy]]
source = "./Reso360Spout2/KlakSpout_send.dll"
target = "Renderer/BepInEx/patchers/Reso360Spout2.Patcher/"

# Renderer プラグイン (Renderer/BepInEx/plugins/ に配置)
[[build.copy]]
source = "./Reso360Spout2.Renderer/bin/Release/net472/Reso360Spout2.Renderer.dll"
target = "Renderer/BepInEx/plugins/Reso360Spout2.Renderer/"
```

---

## ゲーム内設定 UI (BepisModSettings) との連携

### 前提: 2プロセス問題

BepisModSettings は **Host プロセス** (net10.0) で動く。Renderer プラグインは **Renderer プロセス** (net472 / Unity) で動く。別プロセスなので、BepisModSettings は Renderer の BepInEx ConfigFile に直接アクセスできない。

### 解決策: 共有メモリ経由のコンフィグチャンネル

カメラ状態用の共有メモリ末尾にコンフィグチャンネルを追加する。

```
共有メモリ "Reso360Spout2_Camera" のレイアウト (現在 84 bytes):

[0-39]  カメラ状態 (position / rotation / scale)
[40-43] config version (int) ← Host がインクリメント、Renderer がポーリング
[44-47] SPOUT_ENABLE      (int 0/1)
[48-51] PROJECTION_TYPE   (int)
[52-55] CUBEMAP_SIZE      (int)
[56-59] OUTPUT_WIDTH      (int)
[60-63] OUTPUT_HEIGHT     (int)
[64-67] RENDER_IN_STEREO  (int 0/1)
[68-71] NEAR_CLIP         (float)
[72-75] FAR_CLIP          (float)
[76-79] HIDE_LOCAL        (int 0/1)
[80-83] STEREO_SEPARATION (float)
```

### Host 側 (Plugin.cs) の実装

- Renderer 設定と同名の `ConfigEntry` を `"Renderer"` セクションに追加 → BepisModSettings が自動で UI に表示する
- 各 `SettingChanged` で `WriteRendererConfig()` を呼び出し、値を共有メモリに書いてからバージョンをインクリメント

```csharp
R_NEAR_CLIP.SettingChanged += (_, _) => WriteRendererConfig();

internal static void WriteRendererConfig()
{
    _sharedMemView.Write(68, R_NEAR_CLIP.Value);
    // ... 他の値 ...
    int version = _sharedMemView.ReadInt32(40);
    _sharedMemView.Write(40, version + 1);  // Renderer に変更を通知
}
```

### Renderer 側 (RendererPlugin.cs / SharedMemory.cs) の実装

- `SharedMemoryReader.ReadConfigVersion()` でバージョンを読み、前回値と異なれば `ReadConfig()` で設定を取得
- `LateUpdate()` 内でポーリング（カメラ状態の読み取りと同じタイミング）
- D3D デバイス準備完了後（`_initDelayFrames` カウントダウン後）のみ反映する。それ以前は `ApplyInitialConfig()` で直接適用

```csharp
// LateUpdate 内 (D3D 準備完了後のみ)
int ver = _sharedMem.ReadConfigVersion();
if (ver != _lastConfigVersion && ver != 0)
{
    _lastConfigVersion = ver;
    ApplyConfigFromHost(_sharedMem.ReadConfig());  // ConfigEntry.Value 経由 → SettingChanged 発火
}
```

### 設定項目の追加手順

1. `Plugin.cs`: `SHARED_MEM_SIZE` を +4、コメントのレイアウト図を更新、`ConfigEntry` 追加、`SettingChanged` 登録、`WriteRendererConfig()` に書き込み行を追加
2. `SharedMemory.cs`: `MAP_SIZE` を +4、コメントのレイアウト図を更新、`RendererConfig` 構造体にフィールド追加、`ReadConfig()` に読み取り行を追加、`Default` を更新
3. `RendererPlugin.cs`: `ConfigEntry` 追加、`SettingChanged` でコンポーネントへの適用を登録、`ApplyInitialConfig()` と `ApplyConfigFromHost()` に代入行を追加

### enum の扱い

Host 側の enum（`RendererProjectionType`, `RendererCubeMapSize`）と Renderer 側の enum（`ProjectionType`, `CubeMapSize`）は数値が一致している必要がある。共有メモリでは `(int)` にキャストして int として送受信する。

---

## ResoniteSpout を参考にする際の注意

`../ResoniteSpout/` に参照実装がある。ただし以下の点に注意:

- Thunderstore 公開版は `KlakSpout.dll` のみ含み `KlakSpout_send.dll` は含まない
- Sender 機能（`[DllImport("KlakSpout_send.dll")]` を使う部分）は開発中で、公開版では `KlakSpout_send.dll` がなく動作しない可能性が高い
- `KlakSpout_send.dll` を BepInEx プラグインフォルダに置くだけでは動かない（上記「解決策」参照）

---

## 開発環境

- Gale プロファイル: `360Spout`
- プロファイルパス: `%APPDATA%\com.kesomannen.gale\resonite\profiles\360Spout\`
- ゲームパス: `C:\Program Files (x86)\Steam\steamapps\common\Resonite\`
- Unity ネイティブプラグインフォルダ: `[ゲームパス]Renderer\Renderite.Renderer_Data\Plugins\x86_64\`
